using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One local player's fruit feedback, body sampling and desktop test interaction.</summary>
[DefaultExecutionOrder(1500)]
[RequireComponent(typeof(ConnectionManager))]
public sealed class PlayerGameplay : MonoBehaviour
{
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private UnityEngine.UI.Image[] appleIcons;
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

    /// <summary>Checks the selected apple's proximity again at press time, then asks the server.</summary>
    public bool TryCollect(FruitPickup fruit, Ray ray)
    {
        if (!CanInteract) { ShowFeedback("等待遊戲開始"); return false; }
        if (fruit == null || fruit.Object == null || !fruit.Object.IsValid || fruit.Runner != owner.Runner || fruit.IsCollected)
            return false;
        if (!fruit.IsNearby(ReadBody())) { ShowFeedback("請靠近蘋果"); return false; }
        if (!fruit.HasClearRay(ray, transform)) { ShowFeedback("請重新瞄準蘋果"); return false; }
        ShowFeedback("等待伺服器確認…");
        NetworkWorld.For(owner.Runner).RequestCollect(fruit, ray);
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
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) TryCollect(fruit, ray);
    }

    void LateUpdate()
    {
        var world = owner.IsConnected ? NetworkWorld.For(owner.Runner) : null;
        if (world != previousWorld)
        {
            previousWorld = world;
            previousCount = -1;
            feedbackUntil = 0;
        }
        int count = world != null ? world.CollectedFruitCount : 0;
        if (appleIcons != null)
            for (int i = 0; i < appleIcons.Length; i++) if (appleIcons[i] != null) appleIcons[i].gameObject.SetActive(i >= count);
        if (world != null && world.MissionComplete && previousCount != count) ShowFeedback("蘋果已收集完成！");
        previousCount = count;
        string message = world != null && world.MissionComplete ? "蘋果已收集完成！"
            : Time.unscaledTime < feedbackUntil ? feedback
            : !CanInteract ? "等待遊戲開始"
            : hoverFrame >= Time.frameCount - 1 && hovered != null && !hovered.IsCollected
                ? hovered.IsNearby(ReadBody()) ? "按扳機收集蘋果" : "請靠近蘋果"
                : $"瞄準蘋果　{count}/{(world != null ? world.RequiredFruitCount : 3)}";
        if (stateText != null) stateText.text = message;
        owner.SetGameplayFeedback(message);
    }
}
