using Fusion.Editor;
using LiveStock.MotionRecording;
using LiveStock.Multiplayer;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NetworkSheepRigSetup
{
    const string Path = "Assets/Prefabs/Networked/NetworkSK_Sheep_white_final.prefab";
    static NetworkSheepRigSetup() => EditorApplication.delayCall += Setup;

    [MenuItem("Tools/LiveStock/Setup Network Sheep Rig")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (asset == null || asset.GetComponent<NetworkSheepRig>() != null) return;
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var sync = root.AddComponent<NetworkSheepRig>();
            sync.Playback = root.GetComponentInChildren<VRRecordingTargetPlayer>(true);
            new NetworkObjectBakerEditTime().Bake(root);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        NetworkProjectConfigUtilities.ImportGlobalConfig();
        Debug.Log("網路羊三個 Rig Target 同步已設定並存檔。");
    }
}
