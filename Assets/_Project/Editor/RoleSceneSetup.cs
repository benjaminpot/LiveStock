using System;
using System.Linq;
using System.Reflection;
using Fusion;
using Fusion.Editor;
using LiveStock.Multiplayer;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>五個角色場景與共用 Prefab 的一次性遷移；舊版場景移入 Legacy 保存。</summary>
public static class RoleSceneSetup
{
    const string Scenes = "Assets/_Project/Scenes/_ALPHA_DEMO/";
    const string Prefabs = "Assets/_Project/Prefabs/Multiplayer/";
    const string Profiles = "Assets/_Project/BuildProfiles/";
    public static readonly string[] ServerScenes = { Scenes + "Lobby_server.unity", Scenes + "MainGame_server.unity" };
    public static readonly string[] ClientScenes = { Scenes + "Menu_VRClient.unity", Scenes + "Lobby_VRClient.unity", Scenes + "MainGame_VRClient.unity" };

    [MenuItem("Tools/LiveStock/Build Entry/Server")]
    public static void ServerBuildEntry() => SelectProfile("LiveStock_Server");
    [MenuItem("Tools/LiveStock/Build Entry/VR Client PC")]
    public static void ClientBuildEntry() => SelectProfile("LiveStock_VRClient_PC");
    [MenuItem("Tools/LiveStock/Build Entry/VR Client Quest")]
    public static void QuestBuildEntry() => SelectProfile("LiveStock_VRClient_Quest");
    static void SelectProfile(string name) => BuildProfile.SetActiveBuildProfile(AssetDatabase.LoadAssetAtPath<BuildProfile>(Profiles + name + ".asset"));

    [MenuItem("Tools/LiveStock/Setup Five Role Scenes")]
    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scenes + "Lobby_server.unity") != null)
            throw new InvalidOperationException("Five role scenes already exist; migration must not overwrite edited scenes.");
        System.IO.Directory.CreateDirectory(Profiles);
        System.IO.Directory.CreateDirectory(Scenes + "_LegacyRoleFlow");
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.OpenScene(Scenes + "Lobby.unity");
        var oldLauncher = UnityEngine.Object.FindFirstObjectByType<SessionLauncher>();
        var player = oldLauncher.PlayerPrefab;
        string appId = oldLauncher.AppIdFusion;
        string room = oldLauncher.RoomName;
        var serverSystems = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "MainGame_ServerSystems.prefab");
        var session = CreateSessionPrefab();

        // 共用地圖只留正式內容；原始測試物件已保存在 OriginalContent Prefab。
        var world = PrefabUtility.LoadPrefabContents(Prefabs + "MainGame_World.prefab");
        foreach (Transform child in world.transform.Cast<Transform>().ToArray())
            if (child.name == "NetworkPlayer" || child.name == "NPC_sheep" || child.name == "SK_Sheep_white_final")
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        PrefabUtility.SaveAsPrefabAsset(world, Prefabs + "MainGame_World.prefab");
        PrefabUtility.UnloadPrefabContents(world);

        foreach (string name in new[] { "Lobby_server", "Menu_VRClient", "Lobby_VRClient" })
        {
            string source = name == "Menu_VRClient" ? "Menu" : "Lobby";
            scene = EditorSceneManager.OpenScene(Scenes + source + ".unity");
            var launcher = UnityEngine.Object.FindFirstObjectByType<SessionLauncher>();
            var panel = UnityEngine.Object.FindFirstObjectByType<SessionPanel>();
            if (name == "Lobby_VRClient")
            {
                UnityEngine.Object.DestroyImmediate(launcher.gameObject);
                if (panel != null) UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
            else
            {
                launcher.Role = name == "Lobby_server" ? SessionLauncher.EntryRole.Server : SessionLauncher.EntryRole.VRClient;
                launcher.UseVR = name != "Lobby_server";
                launcher.PlayerPrefab = player;
                launcher.SessionStatePrefab = session;
                launcher.AppIdFusion = appId;
                launcher.RoomName = room;
                launcher.Region = "asia";
                launcher.AppVersion = "livestock-roleflow-v2";
            }
            EditorSceneManager.SaveScene(scene, Scenes + name + ".unity");
        }
        foreach (string name in new[] { "MainGame_server", "MainGame_VRClient" })
        {
            scene = EditorSceneManager.OpenScene(Scenes + "MainGame.unity");
            if (name == "MainGame_server") PrefabUtility.InstantiatePrefab(serverSystems, scene);
            EditorSceneManager.SaveScene(scene, Scenes + name + ".unity");
        }
        foreach (string old in new[] { "Lobby", "Menu", "MainGame", "Server", "VRclient", "WaitingRoom" })
        {
            string error = AssetDatabase.MoveAsset(Scenes + old + ".unity", Scenes + "_LegacyRoleFlow/" + old + ".unity");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }
        // Editor 可同時測五個場景；各角色 Build Profile 則只包含自己的兩／三個場景。
        EditorBuildSettings.scenes = ClientScenes.Concat(ServerScenes).Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        CreateProfile("LiveStock_Server", BuildTarget.StandaloneWindows64, ServerScenes);
        CreateProfile("LiveStock_VRClient_PC", BuildTarget.StandaloneWindows64, ClientScenes);
        CreateProfile("LiveStock_VRClient_Quest", BuildTarget.Android, ClientScenes);
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(ServerScenes[0]);
        return "Created five role scenes, persistent session prefab and Server/VRClient PC/Quest profiles. Archived six legacy role scenes.";
    }

    static NetworkObject CreateSessionPrefab()
    {
        string path = Prefabs + "GameSessionState.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
        if (existing != null) return existing;
        var go = new GameObject("GameSessionState");
        go.AddComponent<NetworkObject>();
        go.AddComponent<GameSessionState>();
        new NetworkObjectBakerEditTime().Bake(go);
        var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.SetLabels(asset, new[] { "FusionPrefab" });
        return asset.GetComponent<NetworkObject>();
    }

    static void CreateProfile(string name, BuildTarget target, string[] paths)
    {
        const string questBaseline = "Assets/Settings/Build Profiles/Meta Quest.asset";
        if (target == BuildTarget.Android && AssetDatabase.LoadAssetAtPath<BuildProfile>(questBaseline) != null)
        {
            // 沿用專案既有 Quest 設定；新的角色 Profile 只覆寫自己的場景清單。
            AssetDatabase.CopyAsset(questBaseline, Profiles + name + ".asset");
            var quest = AssetDatabase.LoadAssetAtPath<BuildProfile>(Profiles + name + ".asset");
            quest.name = name;
            quest.overrideGlobalScenes = true;
            quest.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            EditorUtility.SetDirty(quest);
            return;
        }
        // Unity 6000.3 的建構器為 internal；集中使用已檢查的工廠方法，避免手寫 Profile YAML。
        var factory = typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
        if (factory == null) throw new InvalidOperationException("BuildProfile factory unavailable in this Editor.");
        var profile = (BuildProfile)factory.Invoke(null, new object[] { target, StandaloneBuildSubtarget.Player });
        profile.name = name;
        profile.overrideGlobalScenes = true;
        profile.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        AssetDatabase.CreateAsset(profile, Profiles + name + ".asset");
    }
}
