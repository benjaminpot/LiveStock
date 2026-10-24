using UnityEditor;
using UnityEngine;

namespace LiveStock.VR.EditorTools
{
    [CustomEditor(typeof(VRRecenterController))]
    [CanEditMultipleObjects]
    public sealed class VRRecenterControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            bool isRecentering = false;
            foreach (Object inspectedTarget in targets)
            {
                if (((VRRecenterController)inspectedTarget).IsRecentering)
                {
                    isRecentering = true;
                    break;
                }
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || isRecentering))
            {
                string label = isRecentering ? "正在重新定位..." : "重新定位 VR 裝置";
                if (GUILayout.Button(label, GUILayout.Height(30f)))
                {
                    foreach (Object inspectedTarget in targets)
                    {
                        ((VRRecenterController)inspectedTarget).RequestRecenter();
                    }
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("請進入 Play Mode 後再執行 VR 重新定位。", MessageType.Info);
            }
            else if (isRecentering)
            {
                EditorGUILayout.HelpBox("正在等待 VR 追蹤穩定並重新定位。", MessageType.Info);
                Repaint();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "請玩家站在預期中心並面向遊戲正前方，再按下重新定位。",
                    MessageType.None);
            }
        }
    }
}
