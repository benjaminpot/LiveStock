using Fusion;
using LiveStock.MotionRecording;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

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

        [Header("低頻寬動作同步")]
        [SerializeField] private VRRecordingTargetPlayer playback;
        [SerializeField] private AnimalMotionController motionController;
        [Networked, OnChangedRender(nameof(ApplyMotionState))]
        public byte MotionIndex { get; private set; }

        [Header("Alpha 戰鬥")]
        [SerializeField, Min(1)] private int maxHealth = 1;
        [SerializeField, Min(0.1f)] private float respawnDelay = 3f;
        [Networked, OnChangedRender(nameof(ApplyLifeState))]
        public int Health { get; private set; }
        [Networked] private TickTimer RespawnTimer { get; set; }

        private bool authorityUpdatePosition;
        private bool authorityUpdateRotation;
        private bool referencesCached;
        private byte appliedMotion = byte.MaxValue;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private Collider[] colliders = System.Array.Empty<Collider>();
        private bool[] rendererDefaults = System.Array.Empty<bool>();
        private bool[] colliderDefaults = System.Array.Empty<bool>();

        private void Awake()
        {
            CacheReferences();
            CachePresentation();
        }

        public override void Spawned()
        {
            CacheReferences();
            CachePresentation();
            bool hasAuthority = Object.HasStateAuthority;
            ApplyAuthorityState(hasAuthority);
            ApplyRigState();
            if (hasAuthority)
            {
                Health = Mathf.Max(1, maxHealth);
                MotionIndex = 0;
            }
            ApplyMotionState();
            ApplyLifeState();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
                return;

            if (Health <= 0)
            {
                if (RespawnTimer.Expired(Runner))
                {
                    RespawnTimer = TickTimer.None;
                    Health = Mathf.Max(1, maxHealth);
                    ApplyLifeState();
                }
                return;
            }

            int index = motionController != null ? motionController.CurrentMotionIndex : -1;
            if (index >= 0 && index <= byte.MaxValue)
                MotionIndex = (byte)index;
        }

        /// <summary>只能由 State Authority 扣血；回傳 true 代表本次造成擊殺。</summary>
        public bool TryApplyDamage(int amount)
        {
            if (amount <= 0 || Object == null || !Object.IsValid || !Object.HasStateAuthority || Health <= 0)
                return false;

            Health = Mathf.Max(0, Health - amount);
            if (Health > 0)
                return false;

            RespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, respawnDelay));
            ApplyLifeState();
            return true;
        }

        private void CacheReferences()
        {
            if (referencesCached)
                return;

            if (animalNpc == null)
                animalNpc = GetComponent<AnimalNPC>();

            if (navMeshAgent == null)
                navMeshAgent = GetComponent<NavMeshAgent>();

            if (motionController == null)
                motionController = GetComponent<AnimalMotionController>();

            if (playback == null)
                playback = GetComponentInChildren<VRRecordingTargetPlayer>(true);

            // 記住 prefab 原本的移動設定，讓權威端不會被這個網路門衛改變。
            if (navMeshAgent != null)
            {
                authorityUpdatePosition = navMeshAgent.updatePosition;
                authorityUpdateRotation = navMeshAgent.updateRotation;
            }

            referencesCached = true;
        }

        private void CachePresentation()
        {
            if (renderers.Length > 0 || colliders.Length > 0)
                return;

            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            rendererDefaults = System.Array.ConvertAll(renderers, item => item.enabled);
            colliderDefaults = System.Array.ConvertAll(colliders, item => item.enabled);
        }

        private void ApplyRigState()
        {
            if (playback != null)
                playback.enabled = true;

            foreach (var rigBuilder in GetComponentsInChildren<RigBuilder>(true))
                rigBuilder.enabled = true;
            foreach (var solver in GetComponentsInChildren<RootMotion.SolverManager>(true))
                solver.enabled = true;
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
            }

            if (motionController != null)
                motionController.enabled = Object.HasStateAuthority;
        }

        private void ApplyMotionState()
        {
            if (Object == null || !Object.IsValid || Object.HasStateAuthority || Health <= 0 ||
                playback == null || appliedMotion == MotionIndex)
                return;

            if (playback.motions == null || MotionIndex >= playback.motions.Count)
                return;

            appliedMotion = MotionIndex;
            playback.Play(MotionIndex);
        }

        private void ApplyLifeState()
        {
            CacheReferences();
            CachePresentation();
            bool alive = Health > 0;

            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    renderers[i].enabled = alive && rendererDefaults[i];
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null)
                    colliders[i].enabled = alive && colliderDefaults[i];

            if (!alive)
            {
                if (animalNpc != null) animalNpc.enabled = false;
                if (motionController != null) motionController.enabled = false;
                if (playback != null) playback.Pause();
                if (navMeshAgent != null && navMeshAgent.enabled)
                {
                    if (navMeshAgent.isOnNavMesh)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.ResetPath();
                    }
                    navMeshAgent.enabled = false;
                }
                return;
            }

            ApplyAuthorityState(Object != null && Object.IsValid && Object.HasStateAuthority);
            ApplyRigState();
            appliedMotion = byte.MaxValue;
            ApplyMotionState();
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
