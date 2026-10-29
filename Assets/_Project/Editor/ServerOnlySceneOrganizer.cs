using System.Collections.Generic;
using System.Linq;
using LiveStock.Multiplayer;
using Unity.RenderStreaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ServerOnlySceneOrganizer
{
    private const string MainGamePath = "Assets/_Project/Scenes/_ALPHA_DEMO/MainGame.unity";
    private const string RootName = "ServerOnly";

    static ServerOnlySceneOrganizer()
    {
        EditorApplication.delayCall += OrganizeLoadedMainGame;
    }

    [MenuItem("Tools/LiveStock/Organize Server-Only Objects")]
    public static void OrganizeLoadedMainGame()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        // 分流後伺服器服務由 Launcher 載入 Prefab，不再向共用場景加入 ServerOnly。
        if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Multiplayer/MainGame_ServerSystems.prefab") != null)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.path == MainGamePath)
                Organize(scene);
        }
    }

    private static void Organize(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var serverRoot = roots.FirstOrDefault(go => go.name == RootName);
        bool changed = false;

        if (serverRoot == null)
        {
            serverRoot = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(serverRoot, scene);
            Undo.RegisterCreatedObjectUndo(serverRoot, "Create ServerOnly root");
            changed = true;
        }

        if (serverRoot.GetComponent<ServerOnly>() == null)
        {
            Undo.AddComponent<ServerOnly>(serverRoot);
            changed = true;
        }

        var candidates = new HashSet<GameObject>();
        foreach (var root in roots)
        {
            if (root == serverRoot)
                continue;
            if (root.name == "Cameras" || root.name == "RandomNPCSpawner" ||
                root.GetComponentInChildren<CCTVViewSwitcher>(true) != null ||
                root.GetComponentInChildren<SignalingManager>(true) != null ||
                root.GetComponentInChildren<GDC.Metaverse.NetworkSpawner>(true) != null)
                candidates.Add(root);
        }

        foreach (var candidate in candidates)
        {
            if (candidate.transform.parent == serverRoot.transform)
                continue;
            Undo.SetTransformParent(candidate.transform, serverRoot.transform,
                "Move object under ServerOnly");
            changed = true;
        }

        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[ServerOnly] Organized Cameras, CCTV streaming, and RandomNPCSpawner under the ServerOnly root. Review the hierarchy, then save MainGame.", serverRoot);
    }
}
