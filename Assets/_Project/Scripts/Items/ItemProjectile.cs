using LiveStock.Multiplayer;
using UnityEngine;

namespace LiveStock.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class ItemProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float speed = 12f;
        [SerializeField] private bool useGravity = true;
        [SerializeField] private float upwardSpeed = 3f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0.01f)] private float diameter = 0.22f;
        [SerializeField, Min(0.01f)] private float lifetime = 5f;

        private Rigidbody body;
        private SphereCollider sphere;
        private bool inFlight;
        private double expiresAt;

        internal ItemUseManager.ProjectilePool Pool { get; private set; }
        internal int ActiveIndex { get; set; } = -1;
        internal ulong Generation { get; private set; }
        internal string PlayerId { get; private set; }
        internal string ItemId { get; private set; }
        internal int RequestId { get; private set; }
        internal int Slot { get; private set; }
        internal int Index { get; private set; }

        private void Awake() => CacheComponents();

        private void CacheComponents()
        {
            if (body == null) TryGetComponent(out body);
            if (sphere == null) TryGetComponent(out sphere);
        }

        internal bool ValidateConfiguration(out string error)
        {
            CacheComponents();
            if (!enabled || body == null || sphere == null || !sphere.enabled || sphere.isTrigger || body.isKinematic)
            {
                error = "Each projectile needs an enabled ItemProjectile, a dynamic Rigidbody and a solid SphereCollider.";
                return false;
            }
            if (!IsFinite(speed) || speed <= 0f || !IsFinite(lifetime) || lifetime <= 0f ||
                !IsFinite(diameter) || diameter <= 0f || !IsFinite(upwardSpeed) || damage < 1)
            {
                error = "Projectile speed, lifetime and diameter must be finite and positive; upwardSpeed must be finite and damage at least one.";
                return false;
            }
            error = null;
            return true;
        }

        internal bool CanLaunch(Vector3 direction) => IsFinite(LaunchVelocity(direction));

        internal void BindPool(ItemUseManager.ProjectilePool pool)
        {
            Pool = pool;
            CacheComponents();
        }

        internal void Launch(string playerId, string itemId, int requestId, Ray ray, int slot, int index)
        {
            Generation++;
            PlayerId = playerId;
            ItemId = itemId;
            RequestId = requestId;
            Slot = slot;
            Index = index;
            expiresAt = Time.timeAsDouble + lifetime;

            var rotation = Quaternion.LookRotation(ray.direction);
            transform.SetPositionAndRotation(ray.origin, rotation);
            transform.localScale = Vector3.one * diameter;
            sphere.center = Vector3.zero;
            sphere.radius = 0.5f;
            sphere.isTrigger = false;
            sphere.enabled = true;
            inFlight = true;
            // Activation recreates the physics actor; apply velocity only afterward.
            gameObject.SetActive(true);
            body.isKinematic = false;
            body.useGravity = useGravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.detectCollisions = true;
            body.position = ray.origin;
            body.rotation = rotation;
            body.linearVelocity = LaunchVelocity(ray.direction);
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        internal void ResetForPool()
        {
            inFlight = false;
            expiresAt = 0d;
            PlayerId = null;
            ItemId = null;
            RequestId = 0;
            Slot = -1;
            Index = -1;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.detectCollisions = false;
                body.Sleep();
            }
            gameObject.SetActive(false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one * diameter;
        }

        private Vector3 LaunchVelocity(Vector3 direction) => direction * speed + Vector3.up * upwardSpeed;

        private void Update()
        {
            if (!inFlight || Time.timeAsDouble < expiresAt) return;
            inFlight = false;
            body.detectCollisions = false;
            Pool.Owner.FinishProjectile(this, "miss", null);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!inFlight) return;
            // Claim the shot before applying damage: further contacts cannot apply it twice.
            inFlight = false;
            body.detectCollisions = false;
            var target = collision.collider.GetComponentInParent<NetworkNpcAuthorityGate>();
            bool killed = target != null && target.TryApplyDamage(damage);
            Pool.Owner.FinishProjectile(this, killed ? "kill" : "hit",
                target != null ? target.gameObject.name : collision.collider.gameObject.name);
        }

        private void OnDisable()
        {
            if (inFlight && Pool != null) Pool.Owner.CancelProjectile(this);
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
