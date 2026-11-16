using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Routes apple collection to the server and presents replicated progress in the original player UI.</summary>
[DefaultExecutionOrder(1500)]
[RequireComponent(typeof(ConnectionManager))]
public sealed class PlayerGameplay : MonoBehaviour
{
    [SerializeField, Tooltip("The original Player_UI/state text used for aim, collection and completion feedback.")]
    private TMP_Text stateText;
    [SerializeField, Tooltip("Remaining-apple icons in collection order; each server-confirmed apple hides one icon.")]
    private UnityEngine.UI.Image[] appleIcons;
    [SerializeField, Tooltip("The existing Locomotor's body capsule, not a hand collider.")]
    private CapsuleCollider body;
    private ConnectionManager owner;
    private DesktopTestPlayer desktop;
    private string feedback;
    private float feedbackUntil;
    private int hoverFrame = -1;
    private FruitPickup hovered;
    private int previousCount = -1;
    private NetworkWorld previousWorld;
    public string Feedback => stateText != null ? stateText.text : feedback;
    public bool CanInteract => owner != null && owner.IsConnected && !owner.IsBusy
        && !owner.Runner.IsSceneManagerBusy && owner.State != null
        && owner.State.Phase == LobbySessionState.SessionPhase.Running;

    void Awake() => owner = GetComponent<ConnectionManager>();

    /// <summary>Samples only this client's body; desktop uses the original test-player capsule dimensions.</summary>
    public PlayerBodyPose ReadBody()
    {
        if (owner == null) return default;
        if (!owner.IsDesktop) return body != null && body.gameObject.activeInHierarchy ? PlayerBodyPose.From(body) : default;
        if (desktop == null) desktop = GetComponentInChildren<DesktopTestPlayer>(true);
        return desktop == null ? default : new PlayerBodyPose { Center = desktop.transform.position + Vector3.up,
            Axis = Vector3.up, Height = 2f, Radius = .5f };
    }

    /// <summary>Updates local aim feedback without mutating shared collection state.</summary>
    public void OfferTarget(FruitPickup fruit)
    {
        if (fruit == null || fruit.Object == null || !fruit.Object.IsValid || fruit.Runner != owner.Runner) return;
        hovered = fruit;
        hoverFrame = Time.frameCount;
    }

    /// <summary>Checks the original apple range before asking the server to collect the selected fruit.</summary>
    /// <returns>True when a request was sent; collection is confirmed later by the server.</returns>
    public bool TryCollect(FruitPickup fruit)
    {
        if (!CanInteract) { ShowFeedback("等待遊戲開始"); return false; }
        if (fruit == null || fruit.Object == null || !fruit.Object.IsValid || fruit.Runner != owner.Runner || fruit.IsCollected)
            return false;
        if (!fruit.IsNearby(ReadBody())) { ShowFeedback("請靠近蘋果"); return false; }
        ShowFeedback("等待伺服器確認…");
        NetworkWorld.For(owner.Runner).RequestCollect(fruit);
        return true;
    }

    /// <summary>Shows a short-lived action result in the user's Player_UI/state field.</summary>
    public void ShowFeedback(string message)
    {
        feedback = message;
        feedbackUntil = Time.unscaledTime + 2f;
        owner.SetGameplayFeedback(message);
    }

    void Update()
    {
        if (!owner.IsDesktop || !owner.ViewSelected || !CanInteract) return;
        if (desktop == null) desktop = GetComponentInChildren<DesktopTestPlayer>(true);
        if (desktop == null || desktop.ViewCamera == null) return;
        var ray = desktop.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
        if (!owner.Runner.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) return;
        var fruit = hit.collider.GetComponentInParent<FruitPickup>();
        if (fruit == null) return;
        OfferTarget(fruit);
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) TryCollect(fruit);
    }

    /// <summary>Updates the original icons and status text from this room's replicated apple progress.</summary>
    void LateUpdate()
    {
        var world = owner.IsConnected ? NetworkWorld.For(owner.Runner) : null;
        if (world != previousWorld)
        {
            previousWorld = world;
            previousCount = -1;
            feedbackUntil = 0;
            hovered = null;
            hoverFrame = -1;
        }
        int required = world != null ? world.RequiredFruitCount : (appleIcons != null ? appleIcons.Length : 3);
        int count = world != null ? world.CollectedFruitCount : 0;
        if (appleIcons != null)
            for (int i = 0; i < appleIcons.Length; i++) if (appleIcons[i] != null)
                appleIcons[i].gameObject.SetActive(i >= count && i < required);
        // Every client responds to replicated progress, including collection by another player.
        if (world != null && previousCount >= 0 && count > previousCount)
            ShowFeedback(world.MissionComplete ? "蘋果已收集完成！" : $"已收集蘋果 {count}/{required}");
        previousCount = count;
        string message = world != null && world.MissionComplete ? $"蘋果已收集完成！　{count}/{required}"
            : Time.unscaledTime < feedbackUntil ? feedback
            : !CanInteract ? "等待遊戲開始"
            : hoverFrame >= Time.frameCount - 1 && hovered != null && !hovered.IsCollected
                ? hovered.IsNearby(ReadBody()) ? $"按扳機收集蘋果　{count}/{required}" : $"請靠近蘋果　{count}/{required}"
                : $"瞄準蘋果　{count}/{required}";
        if (stateText != null) stateText.text = message;
        owner.SetGameplayFeedback(message);
    }
}
