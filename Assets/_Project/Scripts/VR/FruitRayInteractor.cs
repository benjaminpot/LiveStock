using Oculus.Interaction;
using UnityEngine;

/// <summary>Collects a Runner-scoped apple once per trigger press after the SDK confirms ray selection.</summary>
public sealed class FruitRayInteractor : MonoBehaviour, IGameObjectFilter
{
    [SerializeField] private RayInteractor rayInteractor;
    [SerializeField] private ControllerSelector selector;
    private ConnectionManager owner;
    private PlayerGameplay gameplay;
    private bool pressed;

    void OnEnable()
    {
        selector.WhenSelected += OnPressed;
        selector.WhenUnselected += OnReleased;
    }
    void OnDisable()
    {
        selector.WhenSelected -= OnPressed;
        selector.WhenUnselected -= OnReleased;
        pressed = false;
    }
    void OnPressed() => pressed = true;
    void OnReleased() => pressed = false;

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
        return fruit.Object != null && fruit.Object.IsValid
            && fruit.Runner == owner.Runner && !fruit.IsCollected;
    }

    /// <summary>Offers the aimed apple and consumes at most one confirmed selection per trigger press.</summary>
    void LateUpdate()
    {
        if (!Bind() || owner.IsDesktop || !gameplay.CanInteract) { pressed = false; return; }
        // The selector and SDK may update in different orders. Keep the press until selection or release.
        bool selectedThisPress = pressed && rayInteractor.State == InteractorState.Select;
        if (selectedThisPress) pressed = false;
        var target = rayInteractor.Interactable;
        var fruit = target != null ? target.GetComponentInParent<FruitPickup>() : null;
        if (fruit == null || !Filter(fruit.gameObject)) return;
        gameplay.OfferTarget(fruit);
        // Consuming the press once prevents holding the trigger from collecting a second apple.
        if (selectedThisPress)
            gameplay.TryCollect(fruit);
    }
}
