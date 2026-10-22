using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LiveStock.Multiplayer;

public static class MultiplayerSetup
{
    [MenuItem("Tools/LiveStock/Setup Multiplayer Stage 4")]
    public static void Setup()
    {
        if (Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
            throw new System.InvalidOperationException("Save your open scenes before setup.");
        System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs/Multiplayer");
        const string prefabPath = "Assets/_Project/Prefabs/Multiplayer/NetworkPlayer.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(prefabPath);
        if (prefab == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name = "NetworkPlayer";
            go.AddComponent<NetworkObject>(); go.AddComponent<NetworkTransform>(); go.AddComponent<NetworkPlayer>();
            new NetworkObjectBakerEditTime().Bake(go);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath); Object.DestroyImmediate(go);
            AssetDatabase.SetLabels(saved, new[] { "FusionPrefab" }); prefab = saved.GetComponent<NetworkObject>();
        }
        const string farm = "Assets/_Project/Scenes/Farm_Multiplayer.unity";
        ConfigureRoles(prefabPath);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(farm) == null)
            AssetDatabase.CopyAsset("Assets/_Project/Scenes/test Network.unity", farm);
        var previous = EditorSceneManager.GetSceneManagerSetup();
        foreach (var path in new[] { "Assets/_Project/Scenes/Menu.unity", "Assets/_Project/Scenes/Lobby.unity", farm })
        {
            var scene = EditorSceneManager.OpenScene(path);
            if (Object.FindFirstObjectByType<SessionLauncher>() == null)
            { var launcher = new GameObject("SessionLauncher").AddComponent<SessionLauncher>(); launcher.PlayerPrefab = prefab; }
            if (Object.FindFirstObjectByType<SessionPanel>() == null) new GameObject("SessionPanel").AddComponent<SessionPanel>();
            if (scene.name != "Menu" && GameObject.Find("Multiplayer Test Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Multiplayer Test Ground"; ground.transform.position = new Vector3(0, -.25f, 0); ground.transform.localScale = new Vector3(32, .5f, 32);
            }
            var camera = Camera.main; if (camera != null) { camera.transform.position = new Vector3(0, 20, -20); camera.transform.rotation = Quaternion.Euler(45, 0, 0); camera.stereoTargetEye = StereoTargetEyeMask.None; }
            EditorSceneManager.SaveScene(scene);
        }
        var wanted = new[] { "Assets/_Project/Scenes/Menu.unity", "Assets/_Project/Scenes/Lobby.unity", farm };
        EditorBuildSettings.scenes = wanted.Select(p => new EditorBuildSettingsScene(p, true)).Concat(EditorBuildSettings.scenes.Where(s => !wanted.Contains(s.path))).ToArray();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();
        EditorSceneManager.RestoreSceneManagerSetup(previous);
        Debug.Log("LiveStock Stage 4 setup complete. Open Menu and press Play.");
    }
    public static void ConfigureRoles(string prefabPath = "Assets/_Project/Prefabs/Multiplayer/NetworkPlayer.prefab")
    {
        const string source = "Assets/_Project/model/VR_sheep_player.prefab";
        const string remotePath = "Assets/_Project/Prefabs/Multiplayer/RemoteSheepVisual.prefab";
        var vr = AssetDatabase.LoadAssetAtPath<GameObject>(source);
        if (vr == null) throw new System.InvalidOperationException("Missing VR_sheep_player prefab");
        var remote = PrefabUtility.LoadPrefabContents(source);
        foreach (var behaviour in remote.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
        {
            if (behaviour == null) continue;
            var name = behaviour.GetType().FullName;
            if (behaviour is LiveStock.MotionRecording.VRRecordingTargetPlayer) { behaviour.enabled = false; continue; }
            if (name.StartsWith("UnityEngine.Animations.Rigging.") || name.StartsWith("RootMotion.FinalIK.") || behaviour is LiveStock.MotionRecording.StretchToTargetConstraint) continue;
            Object.DestroyImmediate(behaviour);
        }
        foreach (var camera in remote.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(camera);
        foreach (var listener in remote.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(listener);
        var remoteAsset = PrefabUtility.SaveAsPrefabAsset(remote, remotePath);
        PrefabUtility.UnloadPrefabContents(remote);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var player = root.GetComponent<NetworkPlayer>(); player.LocalVRPrefab = vr; player.RemoteVRPrefab = remoteAsset;
        foreach (var renderer in root.GetComponents<Renderer>()) Object.DestroyImmediate(renderer);
        foreach (var collider in root.GetComponents<Collider>()) Object.DestroyImmediate(collider);
        new NetworkObjectBakerEditTime().Bake(root);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath); PrefabUtility.UnloadPrefabContents(root);
        NetworkProjectConfigUtilities.RebuildPrefabTable(); AssetDatabase.SaveAssets();
    }
}
