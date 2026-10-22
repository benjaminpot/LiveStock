using UnityEngine;
using UnityEngine.InputSystem;

public sealed class DataStreamingTest : MonoBehaviour
{
    // Connect this to an Input Receiver event using the dynamic callback option.
    public void OnInput(InputAction.CallbackContext context)
    {
        // Input Receiver also invokes events for started and canceled phases.
        if (!context.performed)
            return;

        Debug.Log($"[DataStreamingTest] Received action: {context.action.name}", this);
    }
}
