using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Reads LogInMenu inputs, joins the server's Lobby and keeps one local VR rig across stages.</summary>
[DefaultExecutionOrder(-20000)]
public class ConnectionManager : MonoBehaviour
{
    [Header("LogInMenu references")]
    [SerializeField] TMP_InputField roomNameInput;
    [SerializeField] TMP_InputField regionInput;
    [SerializeField] Button connectButton;
    [SerializeField] TMP_Text statusText;
    [Header("Connection defaults")]
    [SerializeField] string defaultRoomName = "LIVESTOCK-DEV";
    [SerializeField] string defaultRegion = "asia";
    [Tooltip("Must be enabled at the same build index on server and client.")]
    [SerializeField] string networkScenePath = ServerConnectionManager.SceneFolder + "Lobby.unity";
    [Header("Local presentation - never a network avatar")]
    [SerializeField] GameObject localVRRig;
    [SerializeField] GameObject vrEventSystem;
    [SerializeField] Canvas menuCanvas;
    [SerializeField] Canvas sessionCanvas;
    [SerializeField] TMP_Text sessionStatusText;
    [SerializeField] Camera desktopCamera;
    [SerializeField] GameObject[] menuWorldRoots = Array.Empty<GameObject>();

    static readonly List<ConnectionManager> instances = new();
    Vector3 menuRigPosition;
    Quaternion menuRigRotation;
    int shownStage;
    bool returning;
    int desktopWorldMask;
    public NetworkRunner Runner { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsConnected => Runner != null && Runner.IsRunning && Runner.IsClient;
    public string Status { get; private set; } = "Ready to connect.";
    // Editor tests may delay this acknowledgement to exercise the server's loaded-player gate.
    [NonSerialized] public float ReadyReportDelaySeconds;
    public LobbySessionState State => LobbySessionState.For(Runner);
    public static ConnectionManager For(NetworkRunner runner) => instances.FirstOrDefault(c => c != null && c.Runner == runner);

    void Awake()
    {
        instances.Add(this);
        Application.runInBackground = true;
        // Reparent only explicitly assigned local objects. Never steal another Runner's rig.
        if (localVRRig != null)
        {
            // RuntimeInitialize callbacks run at startup, not on every return to the menu.
            if (FlowLaunchOptions.DesktopClient) localVRRig.SetActive(false);
            menuRigPosition = localVRRig.transform.position;
            menuRigRotation = localVRRig.transform.rotation;
            localVRRig.transform.SetParent(transform, true);
        }
        if (vrEventSystem != null) vrEventSystem.transform.SetParent(transform, true);
        if (menuCanvas != null) menuCanvas.transform.SetParent(transform, true);
        if (sessionCanvas != null) { sessionCanvas.transform.SetParent(transform, true); sessionCanvas.gameObject.SetActive(false); }
        if (desktopCamera != null)
        {
            desktopWorldMask = desktopCamera.cullingMask;
            desktopCamera.transform.SetParent(transform, true);
            desktopCamera.targetDisplay = 0;
            // The fallback camera is for desktop test runs without a headset.
            desktopCamera.gameObject.SetActive(FlowLaunchOptions.DesktopClient);
            // The scene's preview listener may be disabled; desktop mode owns exactly one listener.
            var listener = desktopCamera.GetComponent<AudioListener>();
            if (listener == null && FlowLaunchOptions.DesktopClient) listener = desktopCamera.gameObject.AddComponent<AudioListener>();
            if (listener != null) listener.enabled = FlowLaunchOptions.DesktopClient;
            if (FlowLaunchOptions.DesktopClient)
            {
                desktopCamera.clearFlags = CameraClearFlags.SolidColor;
                desktopCamera.backgroundColor = new Color(.04f, .06f, .09f);
                desktopCamera.cullingMask = 0;
            }
        }
        if (FlowLaunchOptions.DesktopClient && menuCanvas != null)
        {
            // A mouse-operated test uses its own scoped input module; the server has a separate UI root.
            if (vrEventSystem != null) vrEventSystem.SetActive(false);
            foreach (var canvas in new[] { menuCanvas, sessionCanvas })
                if (canvas != null)
                {
                    foreach (var pointable in canvas.GetComponents<Oculus.Interaction.PointableCanvas>()) pointable.enabled = false;
                    foreach (var ray in canvas.GetComponents<Oculus.Interaction.RayInteractable>()) ray.enabled = false;
                }
            foreach (var overlay in menuCanvas.GetComponents<OVROverlayCanvas>()) overlay.enabled = false;
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            menuCanvas.transform.localScale = Vector3.one;
            var menuScaler = menuCanvas.GetComponent<CanvasScaler>();
            if (menuScaler != null) { menuScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; menuScaler.referenceResolution = new Vector2(1280, 720); }
            // Keep the mouse-operated menu clear of the server panel at the top left.
            foreach (Transform child in menuCanvas.transform)
                if (child is RectTransform rect) rect.anchoredPosition += new Vector2(200, 0);
            if (sessionCanvas != null)
            {
                sessionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                sessionCanvas.transform.localScale = Vector3.one;
                var scaler = sessionCanvas.GetComponent<CanvasScaler>() ?? sessionCanvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                if (sessionStatusText != null) sessionStatusText.rectTransform.anchoredPosition = new Vector2(0, -180);
                var background = sessionCanvas.transform.Find("Background") as RectTransform;
                if (background != null) { background.anchoredPosition = new Vector2(0, -200); background.sizeDelta = new Vector2(650, 240); }
                foreach (var button in sessionCanvas.GetComponentsInChildren<Button>(true))
                    button.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -270);
            }
            var input = new GameObject("Desktop Client Input", typeof(UnityEngine.InputSystem.UI.MultiplayerEventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            input.transform.SetParent(transform, false);
            input.GetComponent<UnityEngine.InputSystem.UI.MultiplayerEventSystem>().playerRoot = gameObject;
            input.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        }
        DontDestroyOnLoad(gameObject);
    }
    void OnDestroy() => instances.Remove(this);

    [ContextMenu("Connect to server")]
    public async void Connect()
    {
        string room = roomNameInput != null ? roomNameInput.text : defaultRoomName;
        string region = regionInput != null ? regionInput.text : defaultRegion;
        await ConnectAsync(room, region);
    }

    /// <summary>Joins an existing room; this never falls back to creating a room or offline mode.</summary>
    public async Task<bool> ConnectAsync(string room, string region)
    {
        if (!Application.isPlaying) { SetStatus("Enter Play Mode before connecting."); return false; }
        if (IsBusy || IsConnected) return false;
        room = room?.Trim(); region = region?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(room)) { SetStatus("Please enter a room name."); return false; }
        if (string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdFusion))
        { SetStatus("Set App Id Fusion in Fusion > Realtime Settings first."); return false; }
        int index = SceneUtility.GetBuildIndexByScenePath(networkScenePath);
        if (index < 0) { SetStatus("Enable the Lobby scene in the build Scene List."); return false; }
        IsBusy = true;
        GameObject go = null;
        try
        {
            SetStatus($"Connecting to {room}...");
            go = new GameObject(name + " Runner");
            DontDestroyOnLoad(go);
            Runner = go.AddComponent<NetworkRunner>();
            go.AddComponent<RunnerEnableVisibility>();
            Runner.ProvideInput = false; // Head/hand network input is intentionally a later feature.
            var manager = go.AddComponent<NetworkSceneManagerDefault>();
            manager.IsSceneTakeOverEnabled = false;
            var events = go.AddComponent<NetworkEvents>();
            events.OnShutdown ??= new NetworkEvents.ShutdownEvent();
            events.OnShutdown.AddListener((runner, reason) =>
            {
                if (Runner == runner) Runner = null;
                SetStatus("Disconnected: " + reason);
                Destroy(runner.gameObject);
            });
            Runner.AddCallbacks(events);
            var settings = PhotonAppSettings.Global.AppSettings.GetCopy();
            if (!string.IsNullOrEmpty(region)) settings.FixedRegion = region;
            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(SceneRef.FromIndex(index), LoadSceneMode.Single);
            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client, SessionName = room, CustomPhotonAppSettings = settings,
                EnableClientSessionCreation = false, SceneManager = manager, Scene = scene
            });
            if (!result.Ok || !IsConnected)
            {
                SetStatus("Connection failed: " + result.ShutdownReason);
                Runner = null;
                if (go != null) Destroy(go);
                return false;
            }
            // Only the visible VR/desktop operator client renders its copy of the world.
            if (Runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple) Runner.SetVisible(localVRRig != null || desktopCamera != null);
            SetStatus($"Connected: {Runner.SessionInfo.Name}. Loading Lobby...");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus("Connection failed: " + exception.Message);
            Debug.LogException(exception, this);
            var failed = Runner; Runner = null;
            if (failed != null) await failed.Shutdown();
            if (go != null) Destroy(go);
            return false;
        }
        finally { IsBusy = false; }
    }

    public async void Disconnect() => await DisconnectAsync();
    public async Task DisconnectAsync()
    {
        if (IsBusy || Runner == null) return;
        IsBusy = true;
        try { await Runner.Shutdown(); SetStatus("Disconnected. Return to Menu to join again."); }
        finally { IsBusy = false; }
    }

    /// <summary>Returns to the existing local menu in Multi-Peer, or reloads it in a standalone client.</summary>
    public async void ReturnToMenu()
    {
        if (IsBusy || returning) return;
        returning = true;
        await DisconnectAsync();
        if (NetworkProjectConfig.Global.PeerMode == NetworkProjectConfig.PeerModes.Multiple)
        {
            shownStage = 0;
            foreach (var root in menuWorldRoots) if (root != null) root.SetActive(true);
            if (localVRRig != null) localVRRig.transform.SetPositionAndRotation(menuRigPosition, menuRigRotation);
            if (menuCanvas != null) menuCanvas.gameObject.SetActive(true);
            if (sessionCanvas != null) sessionCanvas.gameObject.SetActive(false);
            if (desktopCamera != null && FlowLaunchOptions.DesktopClient) desktopCamera.cullingMask = 0;
            returning = false;
            SetStatus("Ready to connect.");
        }
        else
        {
            Destroy(gameObject); // Destroy the preserved rig/UI before a fresh menu creates replacements.
            await Task.Yield(); // Let the old rig's OnDestroy clear headset SDK singletons first.
            SceneManager.LoadScene(ServerConnectionManager.SceneFolder + "Menu_VRClient.unity");
        }
    }

    void Update()
    {
        var state = State;
        var marker = IsConnected && !Runner.IsSceneManagerBusy ? FlowSceneMarker.For(Runner) : null;
        if (marker != null && marker.Stage != shownStage)
        {
            shownStage = marker.Stage;
            foreach (var root in menuWorldRoots) if (root != null) root.SetActive(false);
            if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
            if (sessionCanvas != null) sessionCanvas.gameObject.SetActive(true);
            var spawn = marker.VRSpawnPoint;
            if (spawn != null)
            {
                if (localVRRig != null) localVRRig.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
                if (desktopCamera != null && desktopCamera.gameObject.activeSelf)
                {
                    desktopCamera.transform.SetPositionAndRotation(spawn.position + Vector3.up * 1.65f, spawn.rotation);
                    desktopCamera.cullingMask = desktopWorldMask;
                }
                if (sessionCanvas != null)
                    sessionCanvas.transform.SetPositionAndRotation(spawn.position + spawn.rotation * new Vector3(0, 1.5f, 2), spawn.rotation);
            }
        }
        if (state != null)
            Status = $"{state.Phase} | Players: {state.PlayerCount} | Loaded: {state.LoadedPlayers}/{state.PlayerCount}"
                + (state.Phase == LobbySessionState.SessionPhase.Lobby ? "\nWaiting for the server to start." : "");
        if (statusText != null) statusText.text = Status;
        if (sessionStatusText != null) sessionStatusText.text = Status;
        bool canEdit = !IsBusy && !IsConnected;
        if (connectButton != null) connectButton.interactable = canEdit;
        if (roomNameInput != null) roomNameInput.interactable = canEdit;
        if (regionInput != null) regionInput.interactable = canEdit;
    }
    void SetStatus(string message) { Status = message; Debug.Log("[ConnectionManager] " + message, this); }
}
