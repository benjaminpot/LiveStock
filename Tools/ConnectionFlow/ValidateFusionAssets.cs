using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Verifies archived GUIDs, prefab scripts and inactive scene previews after migration.</summary>
public static class ValidateFusionAssets
{
    public static object Run()
    {
        var first = JObject.Parse(File.ReadAllText("Docs/Legacy_Connection_Archive_Report.json"));
        var second = JArray.Parse(File.ReadAllText("Docs/Legacy_Pose_Archive_Report.json"));
        var archived = first["assets"].Concat(second).Select(item => new {
            path=(string)item["to"],
            pass=AssetDatabase.AssetPathToGUID((string)item["to"]) == (string)item["guid"]
                && AssetDatabase.GUIDToAssetPath((string)item["guid"]) == (string)item["to"]
        }).ToArray();
        var prefabs = AssetDatabase.FindAssets("t:Prefab",new[]{
            "Assets/_Project/Prefabs/Multiplayer", "Assets/_Project/Recycle Bin/LegacyMultiplayer/Prefabs"
        }).Select(AssetDatabase.GUIDToAssetPath).Distinct().Select(path => {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return new {path,missing=root.GetComponentsInChildren<Transform>(true)
                .Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))};
        }).ToArray();
        var scenes = new[] {"MainGame", "Menu_VRClient"}.Select(name => {
            var scene = EditorSceneManager.OpenPreviewScene(ServerConnectionManager.SceneFolder+name+".unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                var recycle = roots.Where(g=>g.name=="Recycle Bin").ToArray();
                return new {name,archiveRoots=recycle.Length,archivedChildren=recycle.Sum(g=>g.transform.childCount),
                    inactive=ArchiveRootsAreInactive(roots),
                    missing=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                        .Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))};
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }).ToArray();
        bool pass = archived.All(a=>a.pass) && prefabs.All(p=>p.missing==0) && scenes.All(s=>s.inactive&&s.missing==0);
        var result = new {pass,archived,prefabs,scenes};
        File.WriteAllText("Docs/Fusion_Map_Asset_Report.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new InvalidOperationException("Asset validation failed. Read the report.");
        return result;
    }

    // A map need not contain archived previews; any archive it does contain must stay inactive and unique.
    static bool ArchiveRootsAreInactive(GameObject[] roots)
    {
        var archives = roots.Where(g=>g.name=="Recycle Bin").ToArray();
        return archives.Length <= 1 && archives.All(g=>!g.activeSelf);
    }

    /// <summary>Checks optional, active and duplicate archive roots without saving or changing a scene.</summary>
    public static object RunContractChecks()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var first = new GameObject("Recycle Bin");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(first, scene);
            bool missing = ArchiveRootsAreInactive(Array.Empty<GameObject>());
            bool activeRejected = !ArchiveRootsAreInactive(new[]{first});
            first.SetActive(false);
            bool inactive = ArchiveRootsAreInactive(new[]{first});
            var second = new GameObject("Recycle Bin");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(second, scene);
            second.SetActive(false);
            bool duplicateRejected = !ArchiveRootsAreInactive(new[]{first,second});
            var result = new {pass=missing&&activeRejected&&inactive&&duplicateRejected,
                missing,activeRejected,inactive,duplicateRejected};
            File.WriteAllText("Docs/Fusion_Asset_Contract_Report.json",
                Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
            if (!result.pass) throw new InvalidOperationException("Archive root contract checks failed.");
            return result;
        }
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}
