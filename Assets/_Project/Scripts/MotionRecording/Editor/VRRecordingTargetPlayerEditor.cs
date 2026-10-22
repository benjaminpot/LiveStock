using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LiveStock.MotionRecording.Editor
{
    [CustomEditor(typeof(VRRecordingTargetPlayer))]
    public sealed class VRRecordingTargetPlayerEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var player = (VRRecordingTargetPlayer)target;
            var motions = serializedObject.FindProperty("motions");
            root.Add(new HelpBox("Add entries to Motions, set each Action Name and assign a recording. Enter Play Mode to use the playback buttons.", HelpBoxMessageType.Info));
            root.Add(new PropertyField(motions));
            root.Add(new PropertyField(serializedObject.FindProperty("defaultMotionIndex")));
            var buttons = new VisualElement { name = "motionButtons" };
            root.Add(buttons);

            void RebuildButtons()
            {
                buttons.Clear();
                for (int i = 0; i < motions.arraySize; i++)
                {
                    int index = i;
                    var entry = motions.GetArrayElementAtIndex(i);
                    string title = entry.FindPropertyRelative("actionName").stringValue;
                    if (string.IsNullOrWhiteSpace(title)) title = "Motion " + (i + 1);
                    bool assigned = entry.FindPropertyRelative("recording").objectReferenceValue != null ||
                        (entry.FindPropertyRelative("metadata").objectReferenceValue != null &&
                         entry.FindPropertyRelative("binaryFrames").objectReferenceValue != null);
                    var button = new Button(() =>
                    {
                        if (!Application.isPlaying) return;
                        serializedObject.ApplyModifiedProperties();
                        player.Play(index);
                    }) { name = "playMotion" + i, text = "Play: " + title };
                    button.SetEnabled(assigned);
                    buttons.Add(button);
                }
            }
            RebuildButtons();
            root.TrackPropertyValue(motions, _ => RebuildButtons());

            var fallback = new Foldout { text = "Single Recording (used when Motions is empty)", value = false };
            AddFields(fallback, "recording", "metadata", "binaryFrames");
            root.Add(fallback);
            AddFields(root, "headTarget", "leftHandTarget", "rightHandTarget", "mappingSpace", "applyHandRotation");
            AddFields(root, "alignToInitialTargets", "calibrationRecording", "positionScale", "axisCorrectionEuler");
            AddFields(root, "playOnStart", "loop", "playbackSpeed");

            var controls = new VisualElement { name = "playbackControls" };
            controls.Add(new Button(() =>
            {
                if (!Application.isPlaying) return;
                serializedObject.ApplyModifiedProperties();
                player.Play();
            }) { text = "Play Default Motion", name = "playDefault" });
            controls.Add(new Button(() => { if (Application.isPlaying) player.Pause(); }) { text = "Pause", name = "pause" });
            controls.Add(new Button(() => { if (Application.isPlaying) player.Resume(); }) { text = "Resume", name = "resume" });
            controls.Add(new Button(() => { if (Application.isPlaying) player.Stop(); }) { text = "Stop / Restore Targets", name = "stop" });
            root.Add(controls);
            var status = new Label { name = "playbackStatus" };
            root.Add(status);
            void RefreshStatus()
            {
                if (player == null) return;
                bool playing = Application.isPlaying;
                buttons.SetEnabled(playing);
                controls.SetEnabled(playing);
                status.text = !playing ? "Enter Play Mode to preview." : string.IsNullOrEmpty(player.CurrentMotionName)
                    ? "Not playing" : player.CurrentMotionName + " | " + player.PlaybackTime.ToString("F2") +
                        " / " + player.Duration.ToString("F2") + " s | " + (player.IsPlaying ? "Playing" : "Paused / Finished");
            }
            RefreshStatus();
            root.schedule.Execute(RefreshStatus).Every(100);
            return root;
        }

        private void AddFields(VisualElement parent, params string[] names)
        {
            foreach (string name in names)
                parent.Add(new PropertyField(serializedObject.FindProperty(name)));
        }
    }
}
