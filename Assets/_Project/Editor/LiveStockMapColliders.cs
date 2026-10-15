using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveStock.EditorTools
{
    /// <summary>Adds editable, static map collision to the imported LIVEStock map.</summary>
    [InitializeOnLoad]
    public static class LiveStockMapColliders
    {
        private const string RequestPath = "Library/LiveStockColliderSetup/request.json";
        private const string ResponsePath = "Library/LiveStockColliderSetup/result.json";
        private const string TargetScene = "Assets/_Project/Scenes/testBlenderFarmScene.unity";
        private static readonly string[] SolidPrefixes =
        {
            "Terrain_v02_", "Cascade_Source_Cliff", "Tree_Trunk", "Landmark_Boulder",
            "Boulder_Satellite", "Shoreline_Rock", "Meadow_Hiding_Rock", "Forest_Stump",
            "Stacked_Forest_Log", "Forest_Rest_Post", "Forest_Rest_Roof", "Forest_Rest_Bench",
            "Meadow_Shelter_Post", "Meadow_Shelter_Roof", "Meadow_Feed_Trough",
            "Supply_Crate", "Crate_Lid_Plank", "Hay_Bale", "Wayfinding_Post",
            "Barn_Stone_Foundation", "Barn_Interior_Floor", "Barn_Back_Wall", "Barn_Side_Wall",
            "Barn_Front_Panel", "Barn_Sliding_Door", "Barn_Door_Header", "Barn_Gables",
            "Barn_Roof_Board", "Barn_Ridge_Cap", "Silo_Plint", "Silo_Red_Body", "Silo_Blue_Roof",
            "Windmill_Leg", "Windmill_Brace", "Windmill_Diagonal", "Windmill_Cap",
            "Farm_Drinking_Trough", "Trough_Rim", "Tower_Stone_Plinth", "Tower_Main_Post",
            "Tower_Platform_Board", "Tower_Balustrade", "Tower_Railing_Post", "Tower_X_Brace",
            "Tower_Side_Brace", "Tower_Pyramid_Roof", "Tower_Access_Stair", "Tower_Stair_Stringer",
            "Tower_Stair_Handrail", "Tower_Upper_Ladder", "Tower_Upper_Rung"
        };

        [Serializable]
        private class Result
        {
            public string status;
            public string message;
            public string scene;
            public string backup;
            public int mapRoots;
            public int meshObjectsInspected;
            public int boxCollidersAdded;
            public int meshCollidersAdded;
            public int existingCollidersPreserved;
            public int visualOnlyMeshesSkipped;
            public int colliderValidationFailures;
            public int bridgeDeckProbes;
            public int bridgeDeckProbeHits;
            public int terrainProbes;
            public int terrainProbeHits;
            public string[] bridgesChecked;
            public bool sceneSaved;
            public bool undoSupported;
            public string[] addedObjects;
        }

        static LiveStockMapColliders()
        {
            if (File.Exists(RequestPath))
                EditorApplication.update += ProcessRequest;
        }

        private static void ProcessRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= ProcessRequest;
            if (!File.Exists(RequestPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ResponsePath));
            File.Move(RequestPath, RequestPath + ".processing");
            try
            {
                var result = Apply(SceneManager.GetActiveScene(), true);
                File.WriteAllText(ResponsePath, JsonUtility.ToJson(result, true));
                Debug.Log("LIVEStock map colliders: " + JsonUtility.ToJson(result));
            }
            catch (Exception exception)
            {
                File.WriteAllText(ResponsePath, JsonUtility.ToJson(new Result
                {
                    status = "failed", message = exception.ToString(),
                    scene = SceneManager.GetActiveScene().path
                }, true));
                Debug.LogException(exception);
            }
            finally { File.Delete(RequestPath + ".processing"); }
        }

        [MenuItem("Tools/LiveStock/Add Map Colliders")]
        public static void AddToCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before adding map colliders.");
            var result = Apply(SceneManager.GetActiveScene(), false);
            Debug.Log("LIVEStock map colliders: " + JsonUtility.ToJson(result, true));
        }

        private static Result Apply(Scene scene, bool save)
        {
            if (!scene.IsValid() || !scene.isLoaded || (save && scene.path != TargetScene))
                throw new InvalidOperationException("Expected the open LIVEStock test scene; no scene was changed.");
            var roots = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith("LiveStock_Terrain_v02", StringComparison.Ordinal))
                .Where(t => t.gameObject.activeInHierarchy).Select(t => t.gameObject).ToArray();
            if (roots.Length != 1)
                throw new InvalidOperationException("Expected one active imported LIVEStock map; found " + roots.Length);
            var filters = roots[0].GetComponentsInChildren<MeshFilter>(true);
            if (!filters.Any(f => f.name.StartsWith("Terrain_v02_", StringComparison.Ordinal)))
                throw new InvalidOperationException("The map must be the editable FBX with individual named meshes.");

            var result = new Result { scene = scene.path, mapRoots = roots.Length, undoSupported = true };
            // Save a copy of the LIVE scene, including any changes not saved by the user yet.
            const string backupDirectory = "Assets/_Project/Scenes/_ColliderBackups";
            if (!AssetDatabase.IsValidFolder(backupDirectory))
                AssetDatabase.CreateFolder("Assets/_Project/Scenes", "_ColliderBackups");
            result.backup = AssetDatabase.GenerateUniqueAssetPath(backupDirectory + "/" +
                scene.name + "_BeforeColliders_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
            if (!EditorSceneManager.SaveScene(scene, result.backup, true))
                throw new IOException("Could not preserve the current scene before adding colliders.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add LIVEStock Map Colliders");
            var added = new List<Collider>();
            var bridges = new HashSet<string>();
            try
            {
                foreach (var filter in filters)
                {
                    if (!filter.sharedMesh || !filter.gameObject.activeInHierarchy) continue;
                    result.meshObjectsInspected++;
                    if (!IsSolid(filter.name)) { result.visualOnlyMeshesSkipped++; continue; }
                    if (filter.GetComponent<Collider>()) { result.existingCollidersPreserved++; continue; }
                    if (filter.GetComponentInParent<Rigidbody>())
                        throw new InvalidOperationException("A map part has a Rigidbody: " + filter.name);
                    Collider collider;
                    if (IsBox(filter.sharedMesh))
                    {
                        var box = Undo.AddComponent<BoxCollider>(filter.gameObject);
                        box.center = filter.sharedMesh.bounds.center;
                        box.size = filter.sharedMesh.bounds.size;
                        collider = box;
                        result.boxCollidersAdded++;
                    }
                    else
                    {
                        var mesh = Undo.AddComponent<MeshCollider>(filter.gameObject);
                        mesh.convex = false;
                        mesh.sharedMesh = filter.sharedMesh;
                        collider = mesh;
                        result.meshCollidersAdded++;
                    }
                    collider.isTrigger = false;
                    collider.enabled = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                    added.Add(collider);
                }
                Physics.SyncTransforms();
                foreach (var collider in added)
                {
                    if (!collider.enabled || collider.isTrigger || collider.bounds.size.sqrMagnitude < 1e-8f)
                        result.colliderValidationFailures++;
                    if (collider is MeshCollider mesh && (mesh.convex || !mesh.sharedMesh))
                        result.colliderValidationFailures++;
                    if (collider.name.StartsWith("Bridge_", StringComparison.Ordinal) && collider.name.Contains("_Deck"))
                    {
                        result.bridgeDeckProbes++;
                        if (ProbeFromAbove(collider, collider.bounds.center)) result.bridgeDeckProbeHits++;
                        bridges.Add(collider.name.Substring(0, collider.name.IndexOf("_Deck", StringComparison.Ordinal)));
                    }
                    if (collider.name.StartsWith("Terrain_v02_", StringComparison.Ordinal))
                    {
                        Bounds bounds = collider.bounds;
                        for (int x = 1; x < 12; x++)
                            for (int z = 1; z < 12; z++)
                            {
                                Vector3 point = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 12f),
                                    bounds.center.y, Mathf.Lerp(bounds.min.z, bounds.max.z, z / 12f));
                                result.terrainProbes++;
                                if (ProbeFromAbove(collider, point)) result.terrainProbeHits++;
                            }
                    }
                }
                result.bridgesChecked = bridges.OrderBy(n => n).ToArray();
                result.addedObjects = added.Select(c => c.name).ToArray();
                if (added.Count > 0 && (result.colliderValidationFailures != 0 || result.bridgeDeckProbes == 0 ||
                    result.bridgeDeckProbes != result.bridgeDeckProbeHits || bridges.Count != 10 || result.terrainProbeHits < 20))
                    throw new InvalidOperationException("Collision verification failed: " + JsonUtility.ToJson(result));
                EditorSceneManager.MarkSceneDirty(scene);
                if (save && !EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save colliders.");
                result.sceneSaved = save;
                Undo.CollapseUndoOperations(undoGroup);
                result.status = "success";
                result.message = "Static map colliders added; water, foliage, small decorative parts and preview animals retain their original state.";
                return result;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static bool ProbeFromAbove(Collider collider, Vector3 point)
        {
            float offset = collider.bounds.extents.y + 1f;
            point.y = collider.bounds.center.y + offset;
            return collider.Raycast(new Ray(point, Vector3.down), out _, offset * 2f + 1f);
        }

        private static bool IsSolid(string name)
        {
            if (name.Contains("_Tip") || name.StartsWith("Forest_Stump_Cut", StringComparison.Ordinal)) return false;
            if (SolidPrefixes.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal) ||
                (prefix.EndsWith("_", StringComparison.Ordinal) && name.StartsWith(prefix, StringComparison.Ordinal)))) return true;
            if (name.StartsWith("Bridge_", StringComparison.Ordinal))
                return name.Contains("_Deck") || name.Contains("_Approach") || name.Contains("_Rail") ||
                    name.Contains("_Pile") || name.Contains("_Support");
            if ((name.Contains("_Fence_") || name.StartsWith("Meadow_Boundary_", StringComparison.Ordinal) ||
                name.StartsWith("Crop_Plot_End_", StringComparison.Ordinal)) &&
                (name.Contains("_Post") || name.Contains("_Rail"))) return true;
            return Regex.IsMatch(name, @"^CCTV_\d\d_(Timber_Leg|Platform_Board|Concrete_Foot|Pole|Tower_X|Railing|Outboard_Arm|Housing)(\.|$)");
        }

        private static bool IsBox(Mesh mesh)
        {
            if (mesh.vertexCount > 36) return false;
            Bounds b = mesh.bounds;
            Vector3 e = b.extents;
            if (Mathf.Min(e.x, Mathf.Min(e.y, e.z)) < 1e-6f) return false;
            float tolerance = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 1e-5f;
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 d = v - b.center;
                if (Mathf.Abs(Mathf.Abs(d.x) - e.x) > tolerance || Mathf.Abs(Mathf.Abs(d.y) - e.y) > tolerance ||
                    Mathf.Abs(Mathf.Abs(d.z) - e.z) > tolerance) return false;
            }
            return true;
        }
    }
}
