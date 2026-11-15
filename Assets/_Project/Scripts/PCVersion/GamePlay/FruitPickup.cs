using Fusion;
using Oculus.Interaction;
using UnityEngine;

/// <summary>A server-collected apple with separate ray surface and body-proximity trigger.</summary>
public class FruitPickup : NetworkBehaviour
{
    [SerializeField, Tooltip("The original large sphere trigger; collection still requires body overlap.")]
    private SphereCollider proximityTrigger;
    [SerializeField, Tooltip("The small solid collider on the apple, not the proximity trigger.")]
    private Collider rayCollider;
    [SerializeField] private RayInteractable rayInteractable;
    [Networked, OnChangedRender(nameof(ApplyCollected))] public NetworkBool IsCollected { get; private set; }
    private Renderer[] visuals;
    private Collider[] colliders;
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private bool appliedCollected;
    public Collider RayCollider => rayCollider;

    void Awake()
    {
        visuals = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    public override void Spawned() => ApplyCollected();

    public override void FixedUpdateNetwork()
    {
        // Physics must reflect collection even on a Client whose rendering is hidden in Multi-Peer.
        if (appliedCollected != IsCollected) ApplyCollected();
    }

    /// <summary>Tests the same capsule-versus-trigger rule for local feedback and the server.</summary>
    public bool IsNearby(PlayerBodyPose body) => body.Overlaps(proximityTrigger);

    /// <summary>Requires the small apple surface and rejects nearer solid obstacles in this Runner.</summary>
    public bool HasClearRay(Ray ray, Transform ignoredPlayer = null)
    {
        if (rayCollider == null || !rayCollider.enabled || !PlayerBodyPose.Finite(ray.origin)
            || !PlayerBodyPose.Finite(ray.direction) || ray.direction.sqrMagnitude < .001f) return false;
        ray.direction = ray.direction.normalized;
        if (!rayCollider.Raycast(ray, out var target, float.MaxValue)) return false;
        var physics = Runner.GetPhysicsScene();
        int count = physics.Raycast(ray.origin, ray.direction, hits, target.distance + .01f, ~0, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.GetComponentInParent<FruitPickup>() == this
                || (ignoredPlayer != null && hit.collider.transform.IsChildOf(ignoredPlayer))) continue;
            if (hit.distance < target.distance - .01f) return false;
        }
        return true;
    }

    /// <summary>Called after NetworkWorld validates the authenticated player's request.</summary>
    public bool CollectAsServer()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || IsCollected) return false;
        IsCollected = true;
        ApplyCollected();
        return true;
    }

    void ApplyCollected()
    {
        appliedCollected = IsCollected;
        foreach (var visual in visuals) visual.enabled = !IsCollected;
        foreach (var collider in colliders) collider.enabled = !IsCollected;
        if (rayInteractable != null) rayInteractable.enabled = !IsCollected;
    }

    /// <summary>Retains the offline test entry; network apples require an authenticated request.</summary>
    public void Collect()
    {
        if (Object != null && Object.IsValid) return;
        if (FruitManager.Instance == null) return;
        FruitManager.Instance.CollectFruitCount();
        gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (Object != null && Object.IsValid) return;
        if (other.CompareTag("Player")) other.GetComponentInParent<PlayerControl>()?.EnterFruitRange(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (Object != null && Object.IsValid) return;
        if (other.CompareTag("Player")) other.GetComponentInParent<PlayerControl>()?.ExitFruitRange(this);
    }
}
