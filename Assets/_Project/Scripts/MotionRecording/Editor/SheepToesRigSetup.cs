using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LiveStock.MotionRecording.Editor
{
    public static class SheepToesRigSetup
    {
        public const string PrefabPath = "Assets/_Project/model/SK_Sheep_white_V2.prefab";
        private const string SkinPath = "Assets/_Project/model/SK_Sheep_white_V2_FrontLegSkin.asset";

        [MenuItem("Tools/LiveStock/Configure V2 Stretchable Front Legs")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure the rig in Edit Mode.");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Configure(root.GetComponent<VRRecordingTargetPlayer>(), false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(SkinPath));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Configure V2 stretchable front legs");
            int group = Undo.GetCurrentGroup();
            // Also support the user's unpacked V2 copy without replacing or reconnecting it.
            foreach (var player in UnityEngine.Object.FindObjectsByType<VRRecordingTargetPlayer>(FindObjectsSortMode.None))
            {
                if (player.name != "SK_Sheep_white_V2" || EditorUtility.IsPersistent(player)) continue;
                Undo.RegisterFullObjectHierarchyUndo(player.gameObject, "Configure V2 front legs");
                Configure(player, true);
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("Tools/LiveStock/Apply V2 Front Legs To Active VR Sheep")]
        public static void ApplyToActiveVrSheep()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply the V2 model changes in Edit Mode.");

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var drivers = UnityEngine.Object.FindObjectsByType<SheepVrAnchorDriver>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(x => !EditorUtility.IsPersistent(x) && x.gameObject.scene == activeScene)
                .ToArray();
            if (drivers.Length != 1)
                throw new InvalidOperationException(
                    "Expected exactly one SheepVrAnchorDriver in the active scene, found " + drivers.Length + ".");

            var root = drivers[0].transform;
            Transform leftTarget = FindFrontFootTarget(root, "L");
            Transform rightTarget = FindFrontFootTarget(root, "R");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Apply V2 front legs to active VR sheep");
            int group = Undo.GetCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Apply V2 front legs to active VR sheep");
            ConfigureRoot(root, leftTarget, rightTarget, true);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = root.gameObject;
            Undo.CollapseUndoOperations(group);

            Debug.Log("Applied V2 stretchable front legs to the active VR sheep (scene not auto-saved).", root);
        }

        private static void Configure(VRRecordingTargetPlayer player, bool undo)
        {
            if (player == null) throw new InvalidOperationException("V2 has no VRRecordingTargetPlayer.");
            ConfigureRoot(player.transform, player.leftHandTarget, player.rightHandTarget, undo);
        }

        private static void ConfigureRoot(Transform root, Transform leftTarget, Transform rightTarget, bool undo)
        {
            ConfigureLeg(root, "L", leftTarget, undo);
            ConfigureLeg(root, "R", rightTarget, undo);
            PrepareStretchSkin(root, undo);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(SkinPath));
        }

        private static void PrepareStretchSkin(Transform root, bool undo)
        {
            var bones = root.GetComponentsInChildren<Transform>(true);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = renderer.sharedMesh;
                if (source == null || AssetDatabase.GetAssetPath(source) == SkinPath) continue;
                var weights = source.boneWeights;
                if (weights.Length == 0 || renderer.bones.Length != 1 || weights.Any(x => x.weight0 < 0.999f)) continue;
                var parent = renderer.bones[0];
                if (!(parent.name.StartsWith("HipFront_") || parent.name.StartsWith("KneeFront_"))) continue;
                string childName = parent.name.StartsWith("HipFront_") ? parent.name.Replace("HipFront_", "KneeFront_") :
                    parent.name.Replace("KneeFront_", "AnkleFront_");
                var child = bones.Single(x => x.name == childName);
                string meshName = renderer.name + "_FrontStretch";
                var generated = AssetDatabase.LoadAllAssetsAtPath(SkinPath).OfType<Mesh>().FirstOrDefault(x => x.name == meshName);
                if (generated == null)
                {
                    var imported = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(source));
                    var importedBones = imported.GetComponentsInChildren<Transform>(true);
                    var bindParent = importedBones.First(x => x.name == parent.name);
                    var bindChild = importedBones.First(x => x.name == child.name);
                    Matrix4x4 childToParent = bindParent.worldToLocalMatrix * bindChild.localToWorldMatrix;
                    Vector3 segment = childToParent.MultiplyPoint3x4(Vector3.zero);
                    var bind = source.bindposes[0];
                    var vertices = source.vertices;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        float blend = Mathf.Clamp01(Vector3.Dot(bind.MultiplyPoint3x4(vertices[i]), segment) / segment.sqrMagnitude);
                        weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - blend, boneIndex1 = 1, weight1 = blend };
                    }
                    generated = UnityEngine.Object.Instantiate(source);
                    generated.name = meshName;
                    generated.bindposes = new[] { bind, childToParent.inverse * bind };
                    generated.boneWeights = weights;
                    if (AssetDatabase.LoadMainAssetAtPath(SkinPath) == null) AssetDatabase.CreateAsset(generated, SkinPath);
                    else AssetDatabase.AddObjectToAsset(generated, SkinPath);
                    EditorUtility.SetDirty(generated);
                }
                if (undo) Undo.RecordObject(renderer, "Use V2 front-leg stretch skin");
                renderer.bones = new[] { parent, child };
                renderer.sharedMesh = generated;
                renderer.quality = SkinQuality.Bone2;
                renderer.updateWhenOffscreen = true;
                EditorUtility.SetDirty(renderer);
                if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        private static void ConfigureLeg(Transform root, string side, Transform target, bool undo)
        {
            if (target == null) throw new InvalidOperationException("Missing hand Target: " + side);
            var bones = root.GetComponentsInChildren<Transform>(true);
            Transform Bone(string prefix) => bones.First(x => x.name == prefix + "Front_" + side + "_joint");
            var hip = Bone("Hip"); var knee = Bone("Knee");
            var ankle = Bone("Ankle"); var toes = Bone("Toes");
            if (knee.parent != hip || ankle.parent != knee || toes.parent != ankle)
                throw new InvalidOperationException("Unexpected front-leg hierarchy: " + side);
            var ik = root.GetComponentsInChildren<TwoBoneIKConstraint>(true)
                .Single(x => x.data.tip == toes || x.data.tip == ankle);
            var rig = ik.GetComponentInParent<Rig>();
            if (rig == null) throw new InvalidOperationException("Missing front-leg Rig: " + side);

            var proxy = target.Find("Ankle IK");
            if (proxy == null) proxy = Child(target, "Ankle IK", undo);
            // A target is the desired Toes pose, not the ankle pose. Preserve the actual bind offset.
            proxy.localPosition = toes.InverseTransformPoint(ankle.position);
            proxy.localRotation = Quaternion.Inverse(toes.rotation) * ankle.rotation;
            proxy.localScale = Vector3.one;
            if (target.GetComponent<RigTransform>() == null) Add<RigTransform>(target.gameObject, undo);

            string stretchName = side + " Front Leg Stretch";
            var stretchObject = rig.transform.Find(stretchName);
            if (stretchObject == null) stretchObject = Child(rig.transform, stretchName, undo);
            stretchObject.SetSiblingIndex(0);
            var stretch = stretchObject.GetComponent<StretchToTargetConstraint>() ?? Add<StretchToTargetConstraint>(stretchObject.gameObject, undo);
            var stretchData = stretch.data;
            if (stretchData.root != hip || !stretch.IsValid())
            {
                stretchData.midRestPosition = knee.localPosition;
                stretchData.tipRestPosition = ankle.localPosition;
            }
            stretchData.root = hip; stretchData.mid = knee; stretchData.tip = ankle; stretchData.target = proxy;
            stretch.data = stretchData;
            stretch.weight = 1;
            var data = ik.data;
            data.root = hip; data.mid = knee; data.tip = ankle; data.target = proxy;
            data.targetPositionWeight = data.targetRotationWeight = 1;
            data.maintainTargetPositionOffset = data.maintainTargetRotationOffset = false;
            ik.data = data;
            ik.weight = rig.weight = 1;
            var exactObject = ik.transform.Find("Exact Ankle Pose");
            if (exactObject == null) exactObject = Child(ik.transform, "Exact Ankle Pose", undo);
            var exact = exactObject.GetComponent<MultiParentConstraint>() ?? Add<MultiParentConstraint>(exactObject.gameObject, undo);
            var exactData = exact.data;
            exactData.constrainedObject = ankle;
            var sources = new WeightedTransformArray();
            sources.Add(new WeightedTransform(proxy, 1));
            exactData.sourceObjects = sources;
            exactData.maintainPositionOffset = exactData.maintainRotationOffset = false;
            exact.data = exactData;
            exact.weight = 1;
            EditorUtility.SetDirty(ik); EditorUtility.SetDirty(stretch); EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(exact);
            if (undo)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(ik);
                PrefabUtility.RecordPrefabInstancePropertyModifications(stretch);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(exact);
            }
        }

        private static Transform FindFrontFootTarget(Transform root, string side)
        {
            string toesName = "ToesFront_" + side + "_joint";
            string ankleName = "AnkleFront_" + side + "_joint";
            var constraint = root.GetComponentsInChildren<TwoBoneIKConstraint>(true)
                .SingleOrDefault(x => x.data.tip != null
                    && (x.data.tip.name == toesName || x.data.tip.name == ankleName));
            if (constraint == null || constraint.data.target == null)
                throw new InvalidOperationException("Missing front-foot IK target: " + side);
            return constraint.data.target;
        }

        private static Transform Child(Transform parent, string name, bool undo)
        {
            var child = new GameObject(name);
            if (undo) Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static T Add<T>(GameObject go, bool undo) where T : Component =>
            undo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
    }
}
