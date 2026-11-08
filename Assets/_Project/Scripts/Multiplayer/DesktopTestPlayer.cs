using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A local PC test view for exercising the same connection flow without a headset.</summary>
/// <remarks>Uses free camera movement for inspecting local scenes during connection tests.</remarks>
public sealed class DesktopTestPlayer : MonoBehaviour
{
    [SerializeField, Min(0.1f), Tooltip("Local camera movement speed in metres per second.")]
    float moveSpeed = 3f;
    [SerializeField, Min(0.01f), Tooltip("Degrees of rotation per mouse pixel while holding the right button.")]
    float lookSensitivity = 0.12f;

    ConnectionManager owner;

    /// <summary>Input is accepted only in this client's loaded Lobby or MainGame.</summary>
    public bool CanControl => owner != null && FlowLaunchOptions.DesktopClient && owner.IsConnected
        && !owner.IsBusy && !owner.Runner.IsSceneManagerBusy && owner.State != null
        && owner.State.Phase != LobbySessionState.SessionPhase.LoadingMainGame
        && FlowSceneMarker.For(owner.Runner) != null;

    /// <summary>Binds the view to its own client, so other local test Runners never receive its input.</summary>
    public void Initialize(ConnectionManager client) => owner = client;

    void Update()
    {
        if (!CanControl) return;
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            // Keep the pointer available for Server/UI buttons when the right button is released.
            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            Vector3 angles = transform.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, angles.x);
            transform.rotation = Quaternion.Euler(Mathf.Clamp(pitch - delta.y, -80f, 80f), angles.y + delta.x, 0f);
        }

        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        Vector3 input = new Vector3(
            (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
            (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
            (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
        // Horizontal movement follows yaw; Q/E deliberately bypass gravity and collision for inspection.
        Vector3 direction = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.ClampMagnitude(input, 1f);
        float speed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? moveSpeed * 2f : moveSpeed;
        transform.position += direction * speed * Mathf.Min(Time.unscaledDeltaTime, 0.1f);
    }
}
