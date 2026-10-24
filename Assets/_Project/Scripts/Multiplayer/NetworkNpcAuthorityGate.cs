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
        [SerializeField, Min(0.05f)] private float hitFlashDuration = 0.35f;
        [SerializeField] private AudioSource hitAudioSource;
        [SerializeField] private AudioClip[] hitSounds = System.Array.Empty<AudioClip>();
        [Networked, OnChangedRender(nameof(ApplyLifeState))]
        public int Health { get; private set; }
        [Networked] public byte LastHitSoundIndex { get; private set; }
        [Networked] private TickTimer HideTimer { get; set; }
        [Networked] private TickTimer RespawnTimer { get; set; }

        private bool authorityUpdatePosition;
        private bool authorityUpdateRotation;
        private bool referencesCached;
        private byte appliedMotion = byte.MaxValue;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private Collider[] colliders = System.Array.Empty<Collider>();
        private bool[] rendererDefaults = System.Array.Empty<bool>();
        private bool[] colliderDefaults = System.Array.Empty<bool>();
        private MaterialPropertyBlock[] rendererDefaultBlocks = System.Array.Empty<MaterialPropertyBlock>();
        private MaterialPropertyBlock hitColorBlock;
        private bool hitFlashApplied;

        public bool IsHitFlashing => hitFlashApplied;
        public bool IsHitAudioPlaying => hitAudioSource != null && hitAudioSource.isPlaying;

        private void Awake()
        {
            hitColorBlock = new MaterialPropertyBlock();
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
                LastHitSoundIndex = 0;
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
                    HideTimer = TickTimer.None;
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

        public override void Render()
        {
            if (Object == null || !Object.IsValid || Health > 0)
                return;

            bool showFlash = !HideTimer.Expired(Runner);
            SetRendererVisibility(showFlash);
            SetHitFlash(showFlash);
        }

        /// <summary>
        /// 傷害只能由 State Authority 套用，避免 Client 偽造或重複送出擊殺。
        /// </summary>
        public bool TryApplyDamage(int amount)
        {
            if (amount <= 0 || Object == null || !Object.IsValid || !Object.HasStateAuthority || Health <= 0)
                return false;

            return ApplyDamageAsStateAuthority(amount);
        }

        private bool ApplyDamageAsStateAuthority(int amount)
        {
            if (amount <= 0 || Object == null || !Object.IsValid || !Object.HasStateAuthority || Health <= 0)
                return false;

            int newHealth = Mathf.Max(0, Health - amount);
            if (newHealth > 0)
            {
                Health = newHealth;
                return false;
            }

            int soundCount = hitSounds != null ? hitSounds.Length : 0;
            // 只在 State Authority 抽選一次，再把同一個索引送給所有玩家。
            LastHitSoundIndex = soundCount > 0
                ? (byte)UnityEngine.Random.Range(0, soundCount)
                : (byte)0;
            HideTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, hitFlashDuration));
            RespawnTimer = TickTimer.CreateFromSeconds(Runner,
                Mathf.Max(0.05f, hitFlashDuration) + Mathf.Max(0.1f, respawnDelay));
            Health = 0;
            RpcPlayHitFeedback(LastHitSoundIndex);
            ApplyLifeState();
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All, TickAligned = true)]
        private void RpcPlayHitFeedback(byte soundIndex)
        {
            CacheReferences();
            CachePresentation();
            if (hitAudioSource != null && hitSounds != null && hitSounds.Length > 0)
            {
                AudioClip clip = hitSounds[soundIndex % hitSounds.Length];
                if (clip != null)
                    hitAudioSource.PlayOneShot(clip);
            }

            SetRendererVisibility(true);
            SetHitFlash(true);
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

            if (hitAudioSource == null)
                hitAudioSource = GetComponent<AudioSource>();

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
            rendererDefaultBlocks = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                rendererDefaultBlocks[i] = new MaterialPropertyBlock();
                if (renderers[i] != null)
                    renderers[i].GetPropertyBlock(rendererDefaultBlocks[i]);
            }
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

            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null)
                    colliders[i].enabled = alive && colliderDefaults[i];

            if (!alive)
            {
                bool showFlash = Object != null && Object.IsValid && !HideTimer.Expired(Runner);
                SetRendererVisibility(showFlash);
                SetHitFlash(showFlash);
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

            SetHitFlash(false);
            SetRendererVisibility(true);
            ApplyAuthorityState(Object != null && Object.IsValid && Object.HasStateAuthority);
            ApplyRigState();
            appliedMotion = byte.MaxValue;
            ApplyMotionState();
        }

        private void SetRendererVisibility(bool visible)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    renderers[i].enabled = visible && rendererDefaults[i];
        }

        private void SetHitFlash(bool active)
        {
            if (hitFlashApplied == active)
                return;

            hitColorBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer item = renderers[i];
                if (item == null)
                    continue;

                MaterialPropertyBlock defaultBlock = i < rendererDefaultBlocks.Length
                    ? rendererDefaultBlocks[i]
                    : null;
                item.SetPropertyBlock(defaultBlock);
                if (!active)
                    continue;

                hitColorBlock.Clear();
                item.GetPropertyBlock(hitColorBlock);
                hitColorBlock.SetColor("_BaseColor", Color.red);
                hitColorBlock.SetColor("_Color", Color.red);
                item.SetPropertyBlock(hitColorBlock);
            }

            hitFlashApplied = active;
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
