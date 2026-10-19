using System;
using System.Linq;
using System.Text;
using LiveStock.MotionRecording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;

public static class ValidateSheepToes
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Run in Edit Mode.");
        var report = new StringBuilder();
        foreach (var variant in new[] { new Vector2(0, 1), new Vector2(90, 1), new Vector2(180, 1.5f) })
            report.AppendLine(Check(variant.x, variant.y));
        return report.ToString();
    }

    private static string Check(float yaw, float scale)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        AnimationClip clip = null;
        PlayableGraph graph = default;
        RigBuilder builder = null;
        int samples = 0;
        float maxPositionError = 0, maxRotationError = 0, maxStretch = 1;
        float recordingStretch = 1;
        try
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/model/SK_Sheep_white_V2.prefab"), scene);
            root.transform.rotation = Quaternion.Euler(0, yaw, 0) * root.transform.rotation;
            root.transform.localScale *= scale;
            var player = root.GetComponent<VRRecordingTargetPlayer>();
            var animator = root.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            builder = root.GetComponent<RigBuilder>();
            var stretches = root.GetComponentsInChildren<StretchToTargetConstraint>();
            Require(stretches.Length == 2, "Exactly two front-leg stretch constraints.");
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(x =>
                AssetDatabase.GetAssetPath(x.sharedMesh).EndsWith("SK_Sheep_white_V2_FrontLegSkin.asset")).ToArray();
            Require(skins.Length == 4, "Four V2-only thigh/shin mesh copies required.");
            foreach (var skin in skins)
            {
                Require(skin.bones.Length == 2 && skin.sharedMesh.bindposes.Length == 2, "Stretch skin needs two bones.");
                Require(skin.sharedMesh.boneWeights.All(x => Mathf.Abs(x.weight0 + x.weight1 - 1) < 0.00001f), "Invalid skin weights.");
            }
            var targets = new[] { player.leftHandTarget, player.rightHandTarget };
            var toes = new[] { "ToesFront_L_joint", "ToesFront_R_joint" }
                .Select(name => root.GetComponentsInChildren<Transform>().Single(x => x.name == name)).ToArray();
            foreach (var stretch in stretches)
            {
                Require(stretch.IsValid(), "Invalid stretch data.");
                var constraints = stretch.GetComponentInParent<Rig>().GetComponentsInChildren<IRigConstraint>();
                Require(ReferenceEquals(constraints[0], stretch) && constraints[1] is TwoBoneIKConstraint &&
                    constraints[2] is MultiParentConstraint, "Expected Stretch -> IK -> Exact Ankle Pose.");
            }
            var skeleton = root.GetComponentsInChildren<Transform>().Single(x => x.name == "Base_joint");
            clip = new AnimationClip();
            foreach (var bone in skeleton.GetComponentsInChildren<Transform>())
            {
                string path = AnimationUtility.CalculateTransformPath(bone, root.transform);
                var p = bone.localPosition; var q = bone.localRotation;
                string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                    "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                for (int i = 0; i < properties.Length; i++)
                    clip.SetCurve(path, typeof(Transform), properties[i], AnimationCurve.Constant(0, 1, values[i]));
            }
            player.SendMessage("Awake");
            builder.enabled = false;
            animator.Rebind();
            graph = PlayableGraph.Create("V2 Toes validation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(graph, "Base pose", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
            Require(builder.Build(graph), "Cannot build rig.");
            graph.Play();
            void Evaluate()
            {
                builder.SyncLayers(); graph.Evaluate(0);
                for (int i = 0; i < 2; i++)
                {
                    maxPositionError = Mathf.Max(maxPositionError, Vector3.Distance(toes[i].position, targets[i].position));
                    maxRotationError = Mathf.Max(maxRotationError, Quaternion.Angle(toes[i].rotation, targets[i].rotation));
                    Require(float.IsFinite(toes[i].position.x) && float.IsFinite(toes[i].rotation.w), "Non-finite Toes pose.");
                }
                foreach (var stretch in stretches)
                    maxStretch = Mathf.Max(maxStretch, stretch.data.mid.localPosition.magnitude / stretch.data.midRestPosition.magnitude,
                        stretch.data.tip.localPosition.magnitude / stretch.data.tipRestPosition.magnitude);
                Require(maxPositionError < 0.0001f, "Toes position error: " + maxPositionError + " at sample " + samples +
                    string.Join(";", stretches.Select(x => " ankleGap=" + Vector3.Distance(x.data.tip.position, x.data.target.position) +
                        " root=" + x.data.root.position.ToString("F5") + " ankle=" + x.data.tip.position.ToString("F5") +
                        " goal=" + x.data.target.position.ToString("F5") + " rootScale=" + x.data.root.lossyScale +
                        " upper=" + x.data.mid.localPosition.magnitude + " lower=" + x.data.tip.localPosition.magnitude)) +
                    " toeRotationError=" + maxRotationError);
                Require(maxRotationError < 0.1f, "Toes rotation error: " + maxRotationError);
                samples++;
            }

            for (int motion = 0; motion < player.motions.Count; motion++)
            {
                var entry = player.motions[motion];
                var frames = entry.recording != null ? entry.recording.LoadFrames(out _) :
                    TransformMotionBinaryFormat.ReadAllFrames(entry.metadata.text, entry.binaryFrames.bytes, out _);
                player.Play(motion); player.Pause();
                Require(player.Duration > 0, "Recording did not load.");
                foreach (var frame in frames)
                {
                    player.Seek((float)(frame.time - frames[0].time));
                    Evaluate();
                }
                player.Stop(); Evaluate();
            }
            recordingStretch = maxStretch;

            // Direct targets exercise long reach, inner workspace and large target rotations.
            foreach (float distance in new[] { 0f, 0.001f, 0.02f, 0.3f, 1f, 3f })
            foreach (var rotation in new[] { Vector3.zero, new Vector3(80, 120, 35), new Vector3(0, 180, 0) })
            {
                for (int i = 0; i < 2; i++)
                {
                    var stretch = stretches.Single(x => x.data.tip == toes[i].parent);
                    var proxy = stretch.data.target;
                    var desiredRotation = root.transform.rotation * Quaternion.Euler(rotation);
                    targets[i].rotation = desiredRotation;
                    // Place the ankle at a known distance from the hip, with the Toes offset respected.
                    targets[i].position = stretch.data.root.position + root.transform.forward * distance -
                        targets[i].TransformVector(proxy.localPosition);
                }
                Evaluate();
            }
            player.Stop(); Evaluate();
            return "PASS yaw=" + yaw + " scale=" + scale + " samples=" + samples +
                " maxToesPositionErrorMetres=" + maxPositionError.ToString("G6") +
                " maxToesRotationErrorDegrees=" + maxRotationError.ToString("G6") +
                " recordingMaxStretchRatio=" + recordingStretch.ToString("G6") +
                " maxStretchRatio=" + maxStretch.ToString("G6");
        }
        finally
        {
            if (builder != null) builder.Clear();
            if (graph.IsValid()) graph.Destroy();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
