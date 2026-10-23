using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace LiveStock.Multiplayer
{
    /// <summary>
    /// 限制網路 NPC 只能由 State Authority 執行 AI 與 NavMesh 移動。
    /// Client 上的複本只接收 NetworkTransform 同步，避免本地尋路與網路位置互相爭搶。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NetworkNpcAuthorityGate : NetworkBehaviour
    {
        [Header("受權限控制的元件")]
        [SerializeField] private AnimalNPC animalNpc; // 只允許權威端執行需求判斷與選擇目的地。
        [SerializeField] private NavMeshAgent navMeshAgent; // 只允許權威端寫入 NPC 的位置與旋轉。

        private bool authorityUpdatePosition;
        private bool authorityUpdateRotation;
        private bool referencesCached;

        private void Awake()
        {
            CacheReferences();
        }

        public override void Spawned()
        {
            CacheReferences();
            ApplyAuthorityState(Object.HasStateAuthority);
        }

        private void CacheReferences()
        {
            if (referencesCached)
                return;

            if (animalNpc == null)
                animalNpc = GetComponent<AnimalNPC>();

            if (navMeshAgent == null)
                navMeshAgent = GetComponent<NavMeshAgent>();

            // 記住 prefab 原本的移動設定，讓權威端不會被這個網路門衛改變。
            if (navMeshAgent != null)
            {
                authorityUpdatePosition = navMeshAgent.updatePosition;
                authorityUpdateRotation = navMeshAgent.updateRotation;
            }

            referencesCached = true;
        }

        private void ApplyAuthorityState(bool hasStateAuthority)
        {
            if (animalNpc != null)
                animalNpc.enabled = hasStateAuthority;

            if (navMeshAgent == null)
                return;

            if (hasStateAuthority)
            {
                // Dedicated Server 是移動的唯一來源，保留 prefab 原本的 NavMesh 設定。
                navMeshAgent.enabled = true;
                navMeshAgent.updatePosition = authorityUpdatePosition;
                navMeshAgent.updateRotation = authorityUpdateRotation;
                return;
            }

            // Client 可能在 Spawned 前已經由 AnimalNPC.Start 設定路徑，因此先清掉路徑再關閉 Agent。
            if (navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
            }

            // Client 的 Transform 只交給 NetworkTransform，不再讓 NavMeshAgent 產生第二個位置寫入者。
            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
            navMeshAgent.enabled = false;
        }
    }
}
