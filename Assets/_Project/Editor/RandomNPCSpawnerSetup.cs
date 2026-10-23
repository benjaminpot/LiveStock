using Fusion;
using LiveStock.Multiplayer;
using UnityEditor;
using UnityEngine;

// 首次匯入建立 Prefab；之後不覆蓋使用者調整的設定。
[InitializeOnLoad]
public static class RandomNPCSpawnerSetup
{
    public const string Path = "Assets/_Project/Prefabs/Multiplayer/RandomNPCSpawner.prefab";

    static RandomNPCSpawnerSetup() => EditorApplication.delayCall += CreatePrefab;

    [MenuItem("Tools/LiveStock/Create Random NPC Spawner Prefab")]
    public static void CreatePrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null) return;
        var go = new GameObject("RandomNPCSpawner");
        try
        {
            var spawner = go.AddComponent<RandomNPCSpawner>();
            spawner.Groups = new[] { new RandomNPCSpawner.NPCGroup
            {
                Prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/Prefabs/Networked/NetworkSK_Sheep_white_final.prefab")
            } };
            PrefabUtility.SaveAsPrefabAsset(go, Path);
            Debug.Log("NPC 生成器 Prefab 已存檔：" + Path);
        }
        finally { Object.DestroyImmediate(go); }
    }
}
