using System.IO;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiveStock.Multiplayer.Editor
{
    public static class MultiplayerUISetup
    {
        const string PrefabPath = "Assets/_Project/Resources/Multiplayer/MultiplayerUI.prefab";
        const string RayCanvasTemplateGuid = "8369d93f7b6b99742bbea0649a41b7b1";

        [InitializeOnLoadMethod]
        static void EnsurePrefabAfterReload()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                EditorApplication.delayCall += CreatePrefab;
        }

        [MenuItem("Tools/LiveStock/Create Multiplayer UI Prefab")]
        public static void CreatePrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

            var root = new GameObject("MultiplayerUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));

            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);

                var templatePath = AssetDatabase.GUIDToAssetPath(RayCanvasTemplateGuid);
                var template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
                if (template == null)
                    throw new FileNotFoundException("Meta Ray Canvas interaction template was not found.", templatePath);

                var interaction = Object.Instantiate(template, root.transform);
                interaction.name = "ISDK_RayCanvasInteraction";

                var rect = interaction.GetComponent<RectTransform>();
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = Vector2.zero;

                interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Created Meta Interaction SDK multiplayer UI prefab at " + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
