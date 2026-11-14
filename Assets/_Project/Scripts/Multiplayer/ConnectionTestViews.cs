using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Cycles local peers and routes desktop input to the selected view.</summary>
public sealed class ConnectionTestViews : MonoBehaviour
{
    public static ConnectionTestViews Instance { get; private set; }
    public Object Selected { get; private set; }
    public string ViewName => Selected != null ? Selected.name : "No peers";
    void Awake() { Instance = this; DontDestroyOnLoad(gameObject); }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame) NextView();
        Apply();
    }
    public void NextView()
    {
        var peers = Object.FindObjectsByType<ServerConnectionManager>(FindObjectsSortMode.None).Cast<Object>()
            .Concat(ConnectionManager.Clients.Where(c => c != null).Cast<Object>()).ToArray();
        if (peers.Length == 0) { Selected = null; return; }
        Selected = peers[(System.Array.IndexOf(peers, Selected) + 1) % peers.Length];
        Apply();
    }
    public void Select(Object peer) { Selected = peer; Apply(); }
    void Apply()
    {
        if (Selected == null) Selected = ConnectionManager.Clients.FirstOrDefault(c => c != null);
        if (Selected == null) Selected = Object.FindFirstObjectByType<ServerConnectionManager>();
        foreach (var client in ConnectionManager.Clients.ToArray())
            if (client != null) client.SetViewSelected(client == Selected);
        foreach (var server in Object.FindObjectsByType<ServerConnectionManager>(FindObjectsSortMode.None))
        {
            bool selected = server == Selected;
            foreach (var canvas in server.transform.root.GetComponentsInChildren<Canvas>(true)) canvas.enabled = selected;
            var camera = server.transform.root.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                camera.enabled = selected;
                camera.cullingMask = selected ? -1 : 0;
                var marker = server.Runner != null ? FlowSceneMarker.For(server.Runner) : null;
                if (marker != null && marker.VRSpawnPoint != null)
                    camera.transform.SetPositionAndRotation(marker.VRSpawnPoint.position + Vector3.up * 1.65f, marker.VRSpawnPoint.rotation);
            }
            foreach (var input in server.transform.root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true)) input.enabled = selected;
            if (server.Runner != null && server.Runner.IsRunning) server.Runner.SetVisible(selected);
        }
    }
}
