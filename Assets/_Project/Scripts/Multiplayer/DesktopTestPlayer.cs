using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Prefab-configured PC inspection player with a third-person camera and penguin visual.</summary>
public sealed class DesktopTestPlayer : MonoBehaviour
{
    [SerializeField] Camera viewCamera;
    [SerializeField, Min(0.1f), Tooltip("Movement speed in metres per second.")] float moveSpeed = 3f;
    [SerializeField, Min(0.01f), Tooltip("Degrees per mouse pixel.")] float lookSensitivity = 0.12f;
    [SerializeField, Tooltip("Offset from the scene spawn point for the third-person test rig.")] Vector3 spawnOffset = new Vector3(0, 0, 3);
    [SerializeField, Min(0f), Tooltip("Horizontal spacing between connected test players.")] float spawnSpacing = 1.5f;
    ConnectionManager owner;
    public Camera ViewCamera => viewCamera;
    public bool CanControl => owner != null && owner.IsDesktop && owner.ViewSelected && owner.IsConnected
        && !owner.IsBusy && !owner.Runner.IsSceneManagerBusy && owner.State != null
        && owner.State.Phase != LobbySessionState.SessionPhase.LoadingMainGame && FlowSceneMarker.For(owner.Runner) != null;
    public void Initialize(ConnectionManager client) => owner = client;
    /// <summary>Moves the prefab root after Fusion finishes loading a stage.</summary>
    public void Warp(Transform spawn)
    {
        // Separate test players so their penguins can be inspected side by side.
        var offset = Vector3.right * Mathf.Max(0, owner.Runner.LocalPlayer.PlayerId - 1) * spawnSpacing;
        transform.SetPositionAndRotation(spawn.position + spawn.rotation * (offset + spawnOffset), spawn.rotation);
    }
    void Update()
    {
        if (CanControl)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, delta.x, 0f, Space.World);
                float pitch = Mathf.DeltaAngle(0f, viewCamera.transform.localEulerAngles.x);
                viewCamera.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - delta.y, -80f, 80f), 0f, 0f);
            }
            var keys = Keyboard.current;
            if (keys != null)
            {
                Vector3 input = new Vector3((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),
                    (keys.eKey.isPressed?1:0)-(keys.qKey.isPressed?1:0), (keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
                float speed = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed ? moveSpeed * 2f : moveSpeed;
                transform.position += transform.rotation * Vector3.ClampMagnitude(input, 1f) * speed * Mathf.Min(Time.unscaledDeltaTime, .1f);
            }
        }
    }
}
