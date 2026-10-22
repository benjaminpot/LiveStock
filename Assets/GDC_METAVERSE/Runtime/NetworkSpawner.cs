using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace GDC.Metaverse
{
    /// <summary>
    /// Scene list of networked objects to create when the scene loads: each group is a network prefab and the
    /// points where the host spawns it. Clients receive them through Fusion.
    /// Fill it with GDC_METAVERSE > Make Networked, or by hand.
    /// Call <see cref="SpawnFor"/> after a scene load and <see cref="DespawnFor"/> before a scene change (host only),
    /// e.g. from INetworkRunnerCallbacks.OnSceneLoadDone / OnSceneLoadStart.
    /// </summary>
    [AddComponentMenu("GDC_METAVERSE/Network Spawner")]
    public sealed class NetworkSpawner : MonoBehaviour
    {
        [Serializable]
        public sealed class SpawnGroup
        {
            public NetworkObject prefab;
            public List<Transform> points = new();
        }

        public List<SpawnGroup> groups = new();
        private static readonly List<NetworkSpawner> _spawners = new();
        private readonly List<NetworkObject> _spawned = new();
        private NetworkRunner _spawnedBy;

        private void Awake() => _spawners.Add(this);
        private void OnDestroy() => _spawners.Remove(this);

        /// <summary>Host: spawns every group of the spawners in the runner's current scene (once per scene load).</summary>
        public static void SpawnFor(NetworkRunner runner)
        {
            if (runner == null || !runner.IsServer) return;
            foreach (var spawner in _spawners.ToArray())
                if (spawner != null && spawner.BelongsTo(runner)) spawner.Spawn(runner);
        }

        /// <summary>Host: removes what this runner's spawners created.</summary>
        public static void DespawnFor(NetworkRunner runner)
        {
            if (runner == null || !runner.IsServer) return;
            foreach (var spawner in _spawners.ToArray())
                if (spawner != null && spawner._spawnedBy == runner) spawner.Despawn(runner);
        }

        // Fusion Multi-Peer gives each runner its own copy of the scene; Single Peer has only one.
        private bool BelongsTo(NetworkRunner runner) =>
            runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Multiple
            || NetworkRunner.GetRunnerForGameObject(gameObject) == runner;

        private void Spawn(NetworkRunner runner)
        {
            if (_spawnedBy == runner) return;
            _spawnedBy = runner;
            foreach (var group in groups)
            {
                if (group.prefab == null) { Debug.LogWarning($"{name}: a spawn group has no prefab.", this); continue; }
                foreach (var point in group.points)
                {
                    if (point == null) continue;
                    var obj = runner.Spawn(group.prefab, point.position, point.rotation);
                    if (obj != null) _spawned.Add(obj);
                }
            }
        }

        private void Despawn(NetworkRunner runner)
        {
            foreach (var obj in _spawned)
                if (obj != null && obj.IsValid) runner.Despawn(obj);
            _spawned.Clear();
            _spawnedBy = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            foreach (var group in groups)
                foreach (var point in group.points)
                {
                    if (point == null) continue;
                    Gizmos.DrawWireSphere(point.position, 0.06f);
                    Gizmos.DrawLine(point.position, point.position + point.forward * 0.12f);
                }
        }
    }
}
