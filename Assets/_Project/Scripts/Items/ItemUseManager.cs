using System;
using System.Collections.Generic;
using LiveStock.Economy;
using UnityEngine;
using UnityEngine.Pool;

namespace LiveStock.Items
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class ItemUseManager : MonoBehaviour
    {
        [Serializable]
        private sealed class ProjectileBinding
        {
            public string itemId;
            public ItemProjectile prefab;
            public ProjectileBinding(string itemId) { this.itemId = itemId; }
        }

        [SerializeField] private ShopManager shopManager;
        [SerializeField, Min(0f)] private float useCooldown = 0.2f;
        [SerializeField] private ProjectileBinding[] projectiles = {
            new ProjectileBinding("capture-ball"),
            new ProjectileBinding("bow"),
            new ProjectileBinding("sniper"),
            new ProjectileBinding("bomb")
        };

        public readonly struct ItemImpact
        {
            public readonly int RequestId;
            public readonly string ItemId;
            public readonly int Slot;
            public readonly int Index;
            public readonly string Status;
            public readonly string ObjectName;

            public ItemImpact(int requestId, string itemId, int slot, int index, string status, string objectName)
            {
                RequestId = requestId;
                ItemId = itemId;
                Slot = slot;
                Index = index;
                Status = status;
                ObjectName = objectName;
            }
        }

        internal sealed class ProjectilePool : IDisposable
        {
            private readonly ItemProjectile prefab;
            private readonly ObjectPool<ItemProjectile> instances;
            private bool disposed;
            internal ItemUseManager Owner { get; }
            internal bool IsAvailable => !disposed && prefab != null;
            internal bool CanLaunch(Vector3 direction) => IsAvailable && prefab.CanLaunch(direction);

            internal ProjectilePool(ItemUseManager owner, ItemProjectile prefab)
            {
                Owner = owner;
                this.prefab = prefab;
                // Retain the observed peak instead of discarding bursts above a small limit.
                instances = new ObjectPool<ItemProjectile>(Create, null, Reset, DestroyInstance,
                    collectionCheck: false, defaultCapacity: 1, maxSize: int.MaxValue);
            }

            private ItemProjectile Create()
            {
                // Parenting during Instantiate places physics in this manager's owning Runner scene.
                var instance = UnityEngine.Object.Instantiate(prefab, Owner.transform, false);
                instance.BindPool(this);
                instance.ResetForPool();
                return instance;
            }

            private static void Reset(ItemProjectile projectile) => projectile.ResetForPool();
            private static void DestroyInstance(ItemProjectile projectile)
            {
                if (projectile != null) UnityEngine.Object.Destroy(projectile.gameObject);
            }

            internal ItemProjectile Get() => instances.Get();
            internal void Release(ItemProjectile projectile)
            {
                if (disposed)
                {
                    projectile.ResetForPool();
                    DestroyInstance(projectile);
                }
                else instances.Release(projectile);
            }

            internal void Warm()
            {
                var instance = Get();
                Release(instance);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                instances.Dispose();
            }
        }

        private readonly Dictionary<string, ProjectilePool> pools =
            new Dictionary<string, ProjectilePool>(StringComparer.Ordinal);
        private readonly List<ItemProjectile> active = new List<ItemProjectile>();
        private ItemUseState state;
        private string registrationItemId;
        private bool configured;

        public event Action<string, ItemImpact> Impact;
        public int ActiveProjectileCount => active.Count;

        private void Awake()
        {
            if (!TryInitialize(out string error))
            {
                Debug.LogError("[ItemUseManager] " + error, this);
                enabled = false;
            }
        }

        private bool TryInitialize(out string error)
        {
            if (shopManager == null || !shopManager.isActiveAndEnabled || shopManager.ProductCount == 0)
            {
                error = "Assign an initialized, enabled scene ShopManager before starting.";
                return false;
            }
            if (!ItemProjectile.IsFinite(useCooldown) || useCooldown < 0f)
            {
                error = "useCooldown must be finite and nonnegative.";
                return false;
            }
            if (projectiles == null || projectiles.Length != shopManager.ProductCount)
            {
                error = "Provide exactly one projectile prefab for every shop product.";
                return false;
            }

            var definitions = new Dictionary<string, ItemProjectile>(StringComparer.Ordinal);
            var distinctPrefabs = new HashSet<ItemProjectile>();
            foreach (var binding in projectiles)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.itemId) || binding.prefab == null ||
                    definitions.ContainsKey(binding.itemId) || !distinctPrefabs.Add(binding.prefab))
                {
                    error = "Projectile bindings need unique product IDs and independent, non-null prefabs.";
                    return false;
                }
                if (!binding.prefab.ValidateConfiguration(out error)) return false;
                definitions.Add(binding.itemId, binding.prefab);
            }

            var itemIds = new string[shopManager.ProductCount];
            for (int i = 0; i < itemIds.Length; i++)
            {
                string itemId = shopManager.GetProduct(i).Id;
                if (!definitions.ContainsKey(itemId))
                {
                    error = "No projectile is configured for shop product '" + itemId + "'.";
                    return false;
                }
                itemIds[i] = itemId;
            }

            state = new ItemUseState(itemIds, shopManager.TryConsumeItem, useCooldown);
            registrationItemId = itemIds[0];
            foreach (var binding in projectiles)
            {
                var pool = new ProjectilePool(this, binding.prefab);
                pools.Add(binding.itemId, pool);
                pool.Warm();
            }
            configured = true;
            error = null;
            return true;
        }

        public bool IsConfiguredFor(ShopManager shop) => configured && state != null && isActiveAndEnabled &&
            shopManager != null && shopManager == shop && shopManager.isActiveAndEnabled &&
            gameObject.scene.IsValid() && gameObject.scene.isLoaded;

        public bool RegisterPlayer(string playerId) => IsConfiguredFor(shopManager) &&
            shopManager.TryGetQuantity(playerId, registrationItemId, out _) && state.RegisterPlayer(playerId);

        public bool RemovePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            bool removed = state != null && state.RemovePlayer(playerId);
            for (int i = active.Count - 1; i >= 0; i--)
                if (string.Equals(active[i].PlayerId, playerId, StringComparison.Ordinal)) CancelProjectile(active[i]);
            return removed;
        }

        public bool TryGetLastRequestId(string playerId, out int requestId)
        {
            requestId = 0;
            return state != null && state.TryGetLastRequestId(playerId, out requestId);
        }

        public ItemUseState.UseResult Use(string playerId, string itemId, int requestId, Ray ray,
            int slot, int index, bool allowed)
        {
            if (!IsConfiguredFor(shopManager))
                return new ItemUseState.UseResult(requestId, itemId, ItemUseState.UseStatus.Unavailable, false);

            ProjectilePool pool = null;
            if (itemId != null) pools.TryGetValue(itemId, out pool);
            bool validRay = TryNormalizeRay(ray, out Ray launchRay);
            bool canLaunch = allowed && validRay && pool != null && pool.CanLaunch(launchRay.direction);
            var result = state.Use(playerId, itemId, requestId, canLaunch, Time.unscaledTimeAsDouble);
            if (!result.ShouldLaunch) return result;

            var projectile = pool.Get();
            projectile.ActiveIndex = active.Count;
            active.Add(projectile);
            projectile.Launch(playerId, itemId, requestId, launchRay, slot, index);
            return result;
        }

        private static bool TryNormalizeRay(Ray ray, out Ray normalized)
        {
            normalized = default;
            if (!ItemProjectile.IsFinite(ray.origin) || !ItemProjectile.IsFinite(ray.direction)) return false;
            // Scale first so finite, very large directions cannot overflow a squared magnitude.
            float largest = Mathf.Max(Mathf.Abs(ray.direction.x),
                Mathf.Max(Mathf.Abs(ray.direction.y), Mathf.Abs(ray.direction.z)));
            if (largest <= 0f) return false;
            normalized = new Ray(ray.origin, (ray.direction / largest).normalized);
            return true;
        }

        internal void FinishProjectile(ItemProjectile projectile, string status, string objectName)
        {
            if (!IsActive(projectile)) return;
            ulong generation = projectile.Generation;
            string playerId = projectile.PlayerId;
            var impact = new ItemImpact(projectile.RequestId, projectile.ItemId, projectile.Slot,
                projectile.Index, status, objectName);
            try
            {
                if (state != null && state.TryGetLastRequestId(playerId, out _)) Impact?.Invoke(playerId, impact);
            }
            finally
            {
                // A listener may disconnect, clear, or even reuse this instance before returning.
                if (projectile != null && projectile.Generation == generation && IsActive(projectile))
                    CancelProjectile(projectile);
            }
        }

        private bool IsActive(ItemProjectile projectile) => projectile != null && projectile.Pool != null &&
            projectile.Pool.Owner == this && projectile.ActiveIndex >= 0 && projectile.ActiveIndex < active.Count &&
            active[projectile.ActiveIndex] == projectile;

        internal void CancelProjectile(ItemProjectile projectile)
        {
            if (!IsActive(projectile)) return;
            int index = projectile.ActiveIndex;
            int last = active.Count - 1;
            if (index != last)
            {
                active[index] = active[last];
                active[index].ActiveIndex = index;
            }
            active.RemoveAt(last);
            projectile.ActiveIndex = -1;
            projectile.Pool.Release(projectile);
        }

        public void Clear()
        {
            state?.Clear();
            while (active.Count > 0) CancelProjectile(active[active.Count - 1]);
        }

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            Clear();
            foreach (var pool in pools.Values) pool.Dispose();
            pools.Clear();
            configured = false;
            Impact = null;
        }
    }
}
