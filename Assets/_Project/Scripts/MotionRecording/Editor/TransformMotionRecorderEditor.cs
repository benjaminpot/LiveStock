#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LiveStock.MotionRecording.Editor
{
    [CustomEditor(typeof(TransformMotionRecorder))]
    public sealed class TransformMotionRecorderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var recorder = (TransformMotionRecorder)target;
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (!recorder.IsRecording)
                {
                    if (GUILayout.Button("Start Recording"))
                    {
                        recorder.StartRecording();
                    }
                }
                else if (GUILayout.Button("Stop Recording"))
                {
                    recorder.StopRecording();
                }
            }

            EditorGUILayout.LabelField("Status", recorder.IsRecording ? "Recording" : "Idle");
            EditorGUILayout.LabelField("Recorded Frames", recorder.RecordedFrameCount.ToString());

            if (GUILayout.Button("Open Recording Folder"))
            {
                string path = recorder.GetRecordingRootPath();
                Directory.CreateDirectory(path);
                EditorUtility.RevealInFinder(path);
            }

            if (!string.IsNullOrEmpty(recorder.LastRecordingDirectory))
            {
                EditorGUILayout.LabelField("Last Output", recorder.LastRecordingDirectory, EditorStyles.wordWrappedLabel);
            }
        }
    }
}
#endif
