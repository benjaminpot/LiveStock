using Oculus.Interaction;
using UnityEngine;

/// <summary>Uses the existing controller selector's press edge for local, Runner-scoped apple interaction.</summary>
public sealed class FruitRayInteractor : MonoBehaviour, IGameObjectFilter
{
    [SerializeField] private RayInteractor rayInteractor;
    [SerializeField] private ControllerSelector selector;
    private ConnectionManager owner;
    private PlayerGameplay gameplay;
    private bool pressed;

    void OnEnable() => selector.WhenSelected += OnPressed;
    void OnDisable() { selector.WhenSelected -= OnPressed; pressed = false; }
    void OnPressed() => pressed = true;

    bool Bind()
    {
        if (owner == null) owner = GetComponentInParent<ConnectionManager>();
        if (owner != null && gameplay == null) gameplay = owner.GetComponent<PlayerGameplay>();
        return owner != null && gameplay != null;
    }

    /// <summary>Prevents SDK ray registries from choosing another local Runner's apple or a hidden apple.</summary>
    public bool Filter(GameObject candidate)
    {
        if (!Bind()) return false;
        var fruit = candidate.GetComponentInParent<FruitPickup>();
        if (fruit == null)
        {
            var client = candidate.GetComponentInParent<ConnectionManager>();
            return client == null || client == owner;
        }
        return gameplay.CanInteract && fruit.Object != null && fruit.Object.IsValid
            && fruit.Runner == owner.Runner && !fruit.IsCollected
            && fruit.HasClearRay(new Ray(rayInteractor.Origin, rayInteractor.Forward), owner.transform);
    }

    void LateUpdate()
    {
        bool wasPressed = pressed;
        pressed = false;
        if (!Bind() || owner.IsDesktop || !gameplay.CanInteract) return;
        var target = rayInteractor.Interactable;
        var fruit = target != null ? target.GetComponentInParent<FruitPickup>() : null;
        if (fruit == null || !Filter(fruit.gameObject)) return;
        gameplay.OfferTarget(fruit);
        // Selector emits once per press, so holding the trigger cannot collect another apple after this one disappears.
        if (wasPressed && rayInteractor.State == InteractorState.Select)
            gameplay.TryCollect(fruit, new Ray(rayInteractor.Origin, rayInteractor.Forward));
    }
}
