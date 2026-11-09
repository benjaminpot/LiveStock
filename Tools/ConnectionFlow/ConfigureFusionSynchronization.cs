using System;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using LiveStock.MotionRecording;
using LiveStock.Multiplayer;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Configures the shared target components and the existing MainGame navigation/spawner.</summary>
public static class ConfigureFusionSynchronization
{
    static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes first.");
    }

    public static object ConfigurePrefabs()
    {
        RequireEditMode();
        foreach (string name in new[] { "NetworkVRSheep", "NPC_sheep" })
        {
            string path = "Assets/_Project/Prefabs/Multiplayer/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var target = root.GetComponent<NetworkTargetPose>();
                if (target == null) target = root.AddComponent<NetworkTargetPose>();
                var settings = new SerializedObject(target);
                Transform head, left, right;
                bool npc = name == "NPC_sheep";
                if (npc)
                {
                    var playback = root.GetComponentInChildren<VRRecordingTargetPlayer>(true);
                    head = playback.headTarget; left = playback.leftHandTarget; right = playback.rightHandTarget;
                }
                else
                {
                    var bindings = new SerializedObject(root.GetComponentInChildren<RemoteVRPoseTargets>(true));
                    head = (Transform)bindings.FindProperty("headTarget").objectReferenceValue;
                    left = (Transform)bindings.FindProperty("leftHandTarget").objectReferenceValue;
                    right = (Transform)bindings.FindProperty("rightHandTarget").objectReferenceValue;
                }
                settings.FindProperty("headTarget").objectReferenceValue = head;
                settings.FindProperty("leftTarget").objectReferenceValue = left;
                settings.FindProperty("rightTarget").objectReferenceValue = right;
                settings.FindProperty("captureOnServer").boolValue = npc;
                settings.ApplyModifiedPropertiesWithoutUndo();
                if (!target.IsConfigured) throw new InvalidOperationException("Missing targets: " + path);
                new NetworkObjectBakerEditTime().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // RPC layout changed on the room object as well.
        var room = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab");
        try
        {
            new NetworkObjectBakerEditTime().Bake(room);
            PrefabUtility.SaveAsPrefabAsset(room, "Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(room); }
        return new { configured = new[] { "NetworkVRSheep", "NPC_sheep", "LobbySessionState" } };
    }

    public static object ConfigureMap()
    {
        RequireEditMode();
        const string path = "Assets/_Project/Scenes/_flow test/MainGame.unity";
        Directory.CreateDirectory(".tmp/FusionSyncBefore");
        if (!File.Exists(".tmp/FusionSyncBefore/MainGame.unity")) File.Copy(path, ".tmp/FusionSyncBefore/MainGame.unity");
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(path);
            var nodes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var surface = nodes.Select(t => t.GetComponent<NavMeshSurface>()).Single(s => s != null);
            if (surface.navMeshData == null)
            {
                surface.BuildNavMesh();
                if (surface.navMeshData == null) throw new InvalidOperationException("NavMesh bake produced no data.");
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Navigation")) AssetDatabase.CreateFolder("Assets/_Project", "Navigation");
                AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Navigation/MainGameNavMesh.asset"));
            }
            var spawner = nodes.Select(t => t.GetComponent<RandomNPCSpawner>()).FirstOrDefault(s => s != null);
            if (spawner == null) spawner = new GameObject("Network NPCs").AddComponent<RandomNPCSpawner>();
            spawner.transform.position = new Vector3(0, 2, 0);
            spawner.AreaSize = new Vector2(50, 50);
            spawner.MaxNetworkedNPCs = 60;
            spawner.MinimumSpacing = 1.5f;
            spawner.Seed = 1049;
            spawner.SnapToGround = false; // The Agent filter selects baked walkable ground directly.
            spawner.Groups = new[] { new RandomNPCSpawner.NPCGroup {
                Prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Multiplayer/NPC_sheep.prefab"),
                MinCount = 60, MaxCount = 60
            } };
            var agents = nodes.Select(t => t.GetComponent<UnityEngine.AI.NavMeshAgent>()).Where(a => a != null)
                .Select(a => new { a.name, networked = a.GetComponent<NetworkObject>() != null,
                    scripts = a.GetComponents<MonoBehaviour>().Where(c => c != null).Select(c => c.GetType().Name).ToArray() }).ToArray();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return new { navMesh = AssetDatabase.GetAssetPath(surface.navMeshData), npcCount = 60, existingAgents = agents };
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }

    public static object ConfigureExamples()
    {
        RequireEditMode();
        const string folder = "Assets/_Project/Prefabs/Multiplayer/Examples";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Multiplayer", "Examples");
        string materialPath = folder + "/NetworkExample.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var prefabs = new NetworkObject[2];
        for (int i = 0; i < 2; i++)
        {
            string name = i == 0 ? "NetworkMovingBox" : "NetworkSwitch";
            string path = folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
            if (existing != null) { prefabs[i] = existing; continue; }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.name = name; go.transform.localScale = Vector3.one * .65f;
                go.GetComponent<Renderer>().sharedMaterial = material;
                go.AddComponent<NetworkObject>(); go.AddComponent<NetworkTransform>();
                if (i == 0) go.AddComponent<NetworkMover>(); else go.AddComponent<NetworkSwitch>();
                new NetworkObjectBakerEditTime().Bake(go);
                var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
                AssetDatabase.SetLabels(asset, new[] { "FusionPrefab" });
                prefabs[i] = asset.GetComponent<NetworkObject>();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        const string roomPath = "Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab";
        var root = PrefabUtility.LoadPrefabContents(roomPath);
        try
        {
            var so = new SerializedObject(root.GetComponent<NetworkWorld>());
            var array = so.FindProperty("sceneObjects"); array.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                array.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue = prefabs[i];
                array.GetArrayElementAtIndex(i).FindPropertyRelative("Offset").vector3Value = new Vector3(i == 0 ? -1.5f : 1f, .75f, 2f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            new NetworkObjectBakerEditTime().Bake(root); PrefabUtility.SaveAsPrefabAsset(root, roomPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        return new { examples = prefabs.Select(p => AssetDatabase.GetAssetPath(p)).ToArray() };
    }
}
