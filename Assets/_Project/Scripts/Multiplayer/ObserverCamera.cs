using UnityEngine;
using UnityEngine.InputSystem;
namespace LiveStock.Multiplayer
{
    public class ObserverCamera : MonoBehaviour
    {
        float yaw, pitch;
        void Start() { yaw = transform.eulerAngles.y; pitch = transform.eulerAngles.x; }
        void Update()
        {
            var k = Keyboard.current; var m = Mouse.current; if (k == null || m == null) return;
            if (m.rightButton.isPressed) { var d = m.delta.ReadValue(); yaw += d.x * .15f; pitch = Mathf.Clamp(pitch - d.y * .15f, -89, 89); transform.rotation = Quaternion.Euler(pitch, yaw, 0); }
            var v = new Vector3((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.eKey.isPressed ? 1 : 0) - (k.qKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            transform.position += transform.rotation * Vector3.ClampMagnitude(v, 1) * ((k.leftShiftKey.isPressed ? 18 : 6) * Time.deltaTime);
        }
    }
}
