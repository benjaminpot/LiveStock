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

    /// <summary>Called after NetworkWorld validates the authenticated player's request.</summary>
    public bool CollectAsServer()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || IsCollected) return false;
        IsCollected = true;
        ApplyCollected();
        return true;
    }

    /// <summary>Keeps collected meshes hidden even when Fusion reapplies the selected peer's renderer visibility.</summary>
    void ApplyCollected()
    {
        appliedCollected = IsCollected;
        foreach (var visual in visuals) visual.forceRenderingOff = IsCollected;
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
