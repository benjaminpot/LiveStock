#if GDC_HAS_PHOTON_VOICE
using System.Linq;
using Fusion;
using Fusion.Editor;
using Photon.Voice.Fusion;
using UnityEditor;
using UnityEngine;

namespace GDC.Metaverse.Editor
{
    /// <summary>Adds Photon Voice's VoiceNetworkObject to network player prefabs (idempotent).</summary>
    public static class VoiceTools
    {
        private const string Menu = "GDC_METAVERSE/Voice/Add Voice To Selected Network Prefab";

        [MenuItem(Menu, priority = 50)]
        private static void AddToSelected()
        {
            foreach (var prefab in Selection.gameObjects.Where(IsNetworkPrefabAsset)) AddVoice(prefab);
        }

        [MenuItem(Menu, true)]
        private static bool CanAdd() => !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Any(IsNetworkPrefabAsset);

        private static bool IsNetworkPrefabAsset(GameObject go) => EditorUtility.IsPersistent(go) && go.GetComponent<NetworkObject>() != null;

        /// <summary>Adds VoiceNetworkObject to a network prefab asset and re-bakes it.</summary>
        public static void AddVoice(GameObject prefabAsset)
        {
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<VoiceNetworkObject>() == null) root.AddComponent<VoiceNetworkObject>();
                new NetworkObjectBakerEditTime().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            NetworkProjectConfigUtilities.ImportGlobalConfig();
            Debug.Log($"[GDC_METAVERSE] Voice added to {path}. Set up the runner with FusionVoiceSetup.Configure.");
        }
    }
}
#endif
