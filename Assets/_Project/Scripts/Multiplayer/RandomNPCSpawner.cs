using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace LiveStock.Multiplayer
{
    /// <summary>場景載入後由伺服器生成 NPC；客戶端由 Fusion 接收結果。</summary>
    public class RandomNPCSpawner : MonoBehaviour
    {
        [Serializable]
        public class NPCGroup
        {
            public NetworkObject Prefab;
            [Min(0)] public int MinCount = 3, MaxCount = 5;
        }

        public NPCGroup[] Groups = Array.Empty<NPCGroup>();
        [Tooltip("以生成器為中心的矩形範圍，單位為公尺；跟隨 Y 軸旋轉。")]
        public Vector2 AreaSize = new(10, 10);
        [Min(0)] public float MinimumSpacing = 1.5f;
        [Min(1)] public int AttemptsPerNPC = 30;
        [Tooltip("Maximum generated network NPCs for this spawner.")]
        [Min(1)] public int MaxNetworkedNPCs = 12;
        [SerializeField, Min(1), Tooltip("Spread spawning across rendered frames to limit setup spikes.")]
        private int spawnPerFrame = 4;
        [Tooltip("0 每次不同，其他值可重現相同隨機排列。")]
        public int Seed;
        public bool SnapToGround = true;
        public LayerMask GroundLayers = 1; // 預設 Default；建議另外建立 Ground Layer。
        [Min(0.1f)] public float GroundRayHeight = 20;
        public float HeightOffset;

        readonly List<NetworkObject> spawned = new();
        readonly List<Vector3> positions = new();
        NetworkRunner owner;
        Coroutine pendingSpawn;
        AnimalWaypoint[] waypoints;

        // 只計算仍存在的網路 NPC，方便 Inspector 與測試工具查詢。
        public int AliveCount => spawned.FindAll(npc => npc != null && npc.IsValid).Count;
        public bool IsSpawning => pendingSpawn != null;

        [ContextMenu("NPC/Clear Generated (Server)")]
        public void ClearGenerated()
        {
            if (owner == null || !owner.IsRunning || !owner.IsServer) return;
            if (pendingSpawn != null) StopCoroutine(pendingSpawn);
            pendingSpawn = null;
            foreach (var npc in spawned)
                if (npc != null && npc.IsValid) owner.Despawn(npc);
            spawned.Clear();
            positions.Clear();
        }

        [ContextMenu("NPC/Respawn Generated (Server)")]
        public void RespawnGenerated()
        {
            if (owner == null || !owner.IsRunning || !owner.IsServer) return;
            var runner = owner;
            ClearGenerated();
            owner = null; // 解除「只生成一次」的保護，再依目前參數生成。
            RequestSpawn(runner);
        }

        // NetworkWorld calls this after every connected player reaches MainGame.
        public static void SpawnFor(NetworkRunner runner)
        {
            if (!runner.IsServer || runner.SceneManager is not NetworkSceneManagerDefault manager) return;
            foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<RandomNPCSpawner>(true))
                    if (spawner.isActiveAndEnabled) spawner.RequestSpawn(runner);
        }

        void RequestSpawn(NetworkRunner runner)
        {
            if (owner != null || pendingSpawn != null)
                return;
            owner = runner;
            var scene = ((NetworkSceneManagerDefault)runner.SceneManager).MainRunnerScene;
            var scoped = new List<AnimalWaypoint>();
            foreach (var root in scene.GetRootGameObjects()) scoped.AddRange(root.GetComponentsInChildren<AnimalWaypoint>(true));
            waypoints = scoped.ToArray();
            pendingSpawn = StartCoroutine(SpawnAfterPhysicsReady(runner));
        }

        IEnumerator SpawnAfterPhysicsReady(NetworkRunner runner)
        {
            // Multi-Peer's new scene needs a physics update before ground queries.
            yield return new WaitForFixedUpdate();
            if (runner == null || !runner.IsRunning || !runner.IsServer) { pendingSpawn = null; yield break; }
            var random = Seed == 0 ? new System.Random() : new System.Random(Seed);
            int remaining = Mathf.Clamp(MaxNetworkedNPCs, 1, 200);
            int batch = 0;
            foreach (var group in Groups)
            {
                if (group == null || group.Prefab == null || remaining <= 0) continue;
                int min = Mathf.Clamp(group.MinCount, 0, 200);
                int max = Mathf.Clamp(group.MaxCount, min, 200);
                int count = Mathf.Min(random.Next(min, max + 1), remaining);
                var agent = group.Prefab.GetComponent<NavMeshAgent>();
                int made = 0;
                for (int i = 0; i < count; i++)
                {
                    if (runner == null || !runner.IsRunning) { pendingSpawn = null; yield break; }
                    if (!TryPosition(random, agent, out var position)) continue;
                    var rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                    var npc = runner.Spawn(group.Prefab, position, rotation, onBeforeSpawned: (r, obj) =>
                    {
                        var animal = obj.GetComponent<AnimalNPC>();
                        if (animal != null) animal.waypoints = waypoints;
                    });
                    if (npc == null) continue;
                    spawned.Add(npc);
                    positions.Add(position);
                    made++;
                    remaining--;
                    if (++batch >= spawnPerFrame) { batch = 0; yield return null; }
                }
                if (made < count) Debug.LogWarning($"{name}: {group.Prefab.name} 生成 {made}/{count}；請加大範圍或檢查地面 Layer。", this);
            }

            int aliveCount = AliveCount;
            if (aliveCount > 0)
                Debug.Log($"[RandomNPCSpawner] '{runner.name}' generated {aliveCount} network NPCs.", this);
            else
                owner = null; // A later explicit spawn request may retry after navigation is corrected.
            pendingSpawn = null;
        }

        bool TryPosition(System.Random random, NavMeshAgent agent, out Vector3 position)
        {
            for (int attempt = 0; attempt < Mathf.Clamp(AttemptsPerNPC, 1, 100); attempt++)
            {
                // 只採用位置與 Y 軸角度，縮放和傾斜不改變生成區域。
                var offset = new Vector3(((float)random.NextDouble() - .5f) * Mathf.Abs(AreaSize.x),
                    0, ((float)random.NextDouble() - .5f) * Mathf.Abs(AreaSize.y));
                position = transform.position + Quaternion.Euler(0, transform.eulerAngles.y, 0) * offset;
                if (SnapToGround)
                {
                    var origin = position + Vector3.up * GroundRayHeight;
                    if (!owner.GetPhysicsScene().Raycast(origin, Vector3.down, out var hit,
                        GroundRayHeight * 2, GroundLayers, QueryTriggerInteraction.Ignore)) continue;
                    position.y = hit.point.y;
                }
                if (agent != null)
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    if (!NavMesh.SamplePosition(position, out var navHit, 2f, filter)) continue;
                    position = navHit.position;
                }
                position.y += HeightOffset;
                // 只比較本生成器產生的 NPC 中心距離，不代表完整碰撞檢查。
                var candidate = position;
                if (positions.Exists(p => new Vector2(p.x - candidate.x, p.z - candidate.z).sqrMagnitude
                    < MinimumSpacing * MinimumSpacing)) continue;
                return true;
            }
            position = default;
            return false;
        }

        public static void DespawnFor(NetworkRunner runner)
        {
            if (!runner.IsServer || runner.SceneManager is not NetworkSceneManagerDefault manager) return;
            foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<RandomNPCSpawner>(true))
                {
                    spawner.ClearGenerated();
                    spawner.owner = null;
                }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Mathf.Abs(AreaSize.x), .1f, Mathf.Abs(AreaSize.y)));
        }
    }
}
