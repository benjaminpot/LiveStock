using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDC.Metaverse.Editor
{
    /// <summary>
    /// Turns placed objects into multiplayer objects: creates a network prefab (a variant of the object's prefab, or a
    /// new prefab for a plain scene object) with NetworkObject and a sync component, registers it with Fusion, and
    /// replaces the scene objects with points of the scene's <see cref="NetworkSpawner"/>. Undoable; save the scene after.
    /// The sync component comes from <see cref="Rules"/> (first match), or Fusion's NetworkTransform by default.
    /// </summary>
    public static class MakeNetworkedTool
    {
        /// <summary>Where new network prefabs are saved.</summary>
        public static string OutputFolder = "Assets/Prefabs/Networked";

        /// <summary>
        /// Project-specific sync components, e.g. from an [InitializeOnLoad] editor class:
        /// <c>MakeNetworkedTool.Rules.Add((go => go.GetComponent&lt;MyPickup&gt;() != null, typeof(MyNetworkPickup)));</c>
        /// </summary>
        public static readonly List<(Func<GameObject, bool> matches, Type component)> Rules = new();

        [MenuItem("GDC_METAVERSE/Multiplayer/Make Selected Networked", priority = 20)]
        private static void MakeSelected() => Run(allInstances: false);

        [MenuItem("GDC_METAVERSE/Multiplayer/Make All Instances Of Selected Networked", priority = 21)]
        private static void MakeAllInstances() => Run(allInstances: true);

        [MenuItem("GDC_METAVERSE/Multiplayer/Make Selected Networked", true)]
        [MenuItem("GDC_METAVERSE/Multiplayer/Make All Instances Of Selected Networked", true)]
        private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Length > 0;

        private static void Run(bool allInstances)
        {
            Undo.SetCurrentGroupName(allInstances ? "Make All Instances Networked" : "Make Selected Networked");
            int undoGroup = Undo.GetCurrentGroup();
            var made = new List<string>();
            try
            {
                foreach (var selected in Selection.gameObjects.Distinct().ToArray())
                {
                    if (selected == null) continue;
                    bool isAsset = EditorUtility.IsPersistent(selected);
                    var root = isAsset ? selected : PrefabUtility.GetOutermostPrefabInstanceRoot(selected) ?? selected;
                    if (!isAsset && root.GetComponentInParent<NetworkSpawner>(true) != null) continue;
                    var prefab = GetOrCreateNetworkPrefab(root, isAsset);
                    made.Add(prefab.name);
                    if (isAsset) continue;
                    var targets = allInstances ? FindInstances(root) : new List<GameObject> { root };
                    ReplaceWithSpawnPoints(root.scene, prefab, targets);
                }
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            AssetDatabase.SaveAssets();
            // Rebuilds Fusion's network prefab table with the new prefabs.
            NetworkProjectConfigUtilities.ImportGlobalConfig();
            Debug.Log($"[GDC_METAVERSE] Networked: {string.Join(", ", made.Distinct())}. Save the scene to keep the spawn points.");
        }

        /// <summary>Creates (or reuses) the network prefab for a scene object or prefab asset.</summary>
        public static NetworkObject GetOrCreateNetworkPrefab(GameObject root, bool isAsset)
        {
            var source = isAsset ? root : PrefabUtility.GetCorrespondingObjectFromSource(root);
            // Already a network prefab (or an instance of one): reuse it.
            if (source != null && source.GetComponent<NetworkObject>() != null) return source.GetComponent<NetworkObject>();
            EnsureFolder(OutputFolder);
            string baseName = source != null ? source.name : root.name;
            string path = $"{OutputFolder}/Network{baseName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
            if (existing != null) return existing;

            // Variant of the original prefab, or a new prefab from a copy of the plain scene object.
            var working = source != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(source)
                : UnityEngine.Object.Instantiate(root);
            try
            {
                working.name = $"Network{baseName}";
                working.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                working.AddComponent<NetworkObject>();
                var rule = Rules.FirstOrDefault(r => r.matches(working));
                if (rule.component != null) working.AddComponent(rule.component);
                else if (working.GetComponent<NetworkBehaviour>() == null) working.AddComponent<NetworkTransform>();
                new NetworkObjectBakerEditTime().Bake(working);
                var prefab = PrefabUtility.SaveAsPrefabAsset(working, path);
                AssetDatabase.SetLabels(prefab, AssetDatabase.GetLabels(prefab)
                    .Append(NetworkProjectConfigImporter.FusionPrefabTag).Distinct().ToArray());
                return prefab.GetComponent<NetworkObject>();
            }
            finally { UnityEngine.Object.DestroyImmediate(working); }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // Scene instances sharing the selected object's original prefab (not already under a spawner).
        private static List<GameObject> FindInstances(GameObject selected)
        {
            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(selected);
            if (original == null) return new List<GameObject> { selected };
            return selected.scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject)
                .Where(go => PrefabUtility.IsOutermostPrefabInstanceRoot(go)
                    && PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) == original
                    && go.GetComponentInParent<NetworkSpawner>(true) == null)
                .ToList();
        }

        private static void ReplaceWithSpawnPoints(UnityEngine.SceneManagement.Scene scene, NetworkObject prefab, List<GameObject> targets)
        {
            var spawner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkSpawner>(true)).FirstOrDefault();
            if (spawner == null)
            {
                var go = new GameObject("Network Spawner");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create Network Spawner");
                spawner = Undo.AddComponent<NetworkSpawner>(go);
            }
            Undo.RecordObject(spawner, "Add spawn points");
            var group = spawner.groups.FirstOrDefault(g => g.prefab == prefab);
            if (group == null)
            {
                group = new NetworkSpawner.SpawnGroup { prefab = prefab };
                spawner.groups.Add(group);
            }
            foreach (var target in targets)
            {
                var point = new GameObject($"{prefab.name} Spawn {group.points.Count + 1}").transform;
                Undo.RegisterCreatedObjectUndo(point.gameObject, "Create spawn point");
                Undo.SetTransformParent(point, spawner.transform, "Parent spawn point");
                point.SetPositionAndRotation(target.transform.position, target.transform.rotation);
                group.points.Add(point);
                Undo.DestroyObjectImmediate(target);
            }
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
