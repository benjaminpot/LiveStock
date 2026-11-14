using System;
using System.IO;
using System.Text.RegularExpressions;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>Moves existing avatar prefab bindings onto NetworkWorld and rebakes the room prefab.</summary>
public static class ConfigureNetworkWorld
{
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before migrating the room prefab.");
        const string path = "Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab";
        string serialized = File.ReadAllText(path);
        Directory.CreateDirectory(".tmp/FusionP1Before");
        string backup = ".tmp/FusionP1Before/LobbySessionState.prefab";
        if (!File.Exists(backup)) File.Copy(path, backup);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var world = root.GetComponent<NetworkWorld>();
            if (world == null) world = root.AddComponent<NetworkWorld>();
            var settings = new SerializedObject(world);
            foreach (string field in new[] { "desktopAvatarPrefab", "vrAvatarPrefab" })
            {
                var match = Regex.Match(serialized, field + @": \{fileID: \d+, guid: ([a-f0-9]+)");
                if (!match.Success) throw new InvalidOperationException("Missing existing avatar binding: " + field);
                var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value));
                if (prefab == null) throw new InvalidOperationException("Invalid avatar binding: " + field);
                settings.FindProperty(field).objectReferenceValue = prefab;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            new NetworkObjectBakerEditTime().Bake(root);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save migrated room prefab.");
            return new { prefab = path, worldBound = true, networkBehaviours = root.GetComponent<NetworkObject>().NetworkedBehaviours.Length };
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
