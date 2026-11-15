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

    [Header("Local presentation - never a network avatar")]
    [SerializeField] GameObject localVRRig;
    [SerializeField] GameObject vrEventSystem;
    [SerializeField] Canvas menuCanvas;
    [SerializeField] Canvas sessionCanvas;
    [SerializeField] TMP_Text sessionStatusText;
    [SerializeField] Camera desktopCamera;
    [SerializeField] GameObject[] menuWorldRoots = Array.Empty<GameObject>();

    [SerializeField] bool desktopMode;
    [SerializeField, Tooltip("Editor/device-free VR role. Uses serialized tracking anchors without starting XR.")]
    bool vrPreviewMode;
    [SerializeField] DesktopTestPlayer desktopPlayerPrefab;
    DesktopTestPlayer desktopPlayer;
    public bool IsDesktop => desktopMode || FlowLaunchOptions.DesktopClient;
    public bool IsVRPreview => !IsDesktop && (vrPreviewMode || FlowLaunchOptions.VRPreview);
    public bool ViewSelected { get; private set; } = true;
    public static IReadOnlyList<ConnectionManager> Clients => instances;
    public void ConfigureDesktop(bool desktop) => desktopMode = desktop;
    static readonly List<ConnectionManager> instances = new();
    Vector3 menuRigPosition;
    Quaternion menuRigRotation;
    int shownStage;
    string gameplayFeedback;
    public void SetGameplayFeedback(string message) => gameplayFeedback = message;
    bool returning;
    int desktopWorldMask;
    readonly Dictionary<UnityEngine.Behaviour, bool> viewStates = new();
    Vector3 menuCameraPosition;
    Quaternion menuCameraRotation;
    NetworkSessionController session;
    public NetworkRunner Runner => session != null ? session.Runner : null;
    public bool IsBusy { get; private set; }
    public bool IsConnected => Runner != null && Runner.IsRunning && Runner.IsClient && Runner.IsConnectedToServer;
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
            if (IsDesktop || IsVRPreview) localVRRig.SetActive(false);
            menuRigPosition = localVRRig.transform.position;
            menuRigRotation = localVRRig.transform.rotation;
            localVRRig.transform.SetParent(transform, true);
        }
        if (vrEventSystem != null) vrEventSystem.transform.SetParent(transform, true);
        if (menuCanvas != null) menuCanvas.transform.SetParent(transform, true);
        if (sessionCanvas != null) { sessionCanvas.transform.SetParent(transform, true); sessionCanvas.gameObject.SetActive(false); }
        // Keep this scene's offline environment available when returning from a network scene.
        foreach (var root in menuWorldRoots) if (root != null) root.transform.SetParent(transform, true);
        if (IsDesktop && desktopPlayerPrefab != null)
        {
            if (desktopCamera != null) desktopCamera.gameObject.SetActive(false);
            desktopPlayer = Instantiate(desktopPlayerPrefab, transform);
            desktopPlayer.Initialize(this);
            desktopCamera = desktopPlayer.ViewCamera;
        }
        if (desktopCamera != null)
        {
            desktopWorldMask = desktopCamera.cullingMask;
            if (desktopPlayer == null) desktopCamera.transform.SetParent(transform, true);
            desktopCamera.targetDisplay = 0;
            // The fallback camera is for desktop test runs without a headset.
            desktopCamera.gameObject.SetActive(IsDesktop || IsVRPreview);
            desktopCamera.enabled = IsDesktop || IsVRPreview;
            // The scene's preview listener may be disabled; desktop mode owns exactly one listener.
            var listener = desktopCamera.GetComponent<AudioListener>();
            if (listener == null && (IsDesktop || IsVRPreview)) listener = desktopCamera.gameObject.AddComponent<AudioListener>();
            if (listener != null) listener.enabled = IsDesktop || IsVRPreview;
            if (IsDesktop)
            {
                desktopCamera.clearFlags = CameraClearFlags.SolidColor;
                desktopCamera.backgroundColor = new Color(.04f, .06f, .09f);
                desktopCamera.cullingMask = 0;

            }
        }
        if (IsDesktop && menuCanvas != null)
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
                if (sessionStatusText != null) { sessionStatusText.rectTransform.anchoredPosition = new Vector2(-390, 270); sessionStatusText.rectTransform.sizeDelta = new Vector2(440, 110); sessionStatusText.fontSize = 20; sessionStatusText.alignment = TextAlignmentOptions.TopLeft; }
                var background = sessionCanvas.transform.Find("Background") as RectTransform;
                if (background != null) { background.anchoredPosition = new Vector2(-390, 235); background.sizeDelta = new Vector2(480, 210); }
                foreach (var button in sessionCanvas.GetComponentsInChildren<Button>(true))
                    { var rect = button.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(-390, 160); rect.sizeDelta = new Vector2(240, 44); }
            }
            var input = new GameObject("Desktop Client Input", typeof(UnityEngine.InputSystem.UI.MultiplayerEventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            input.transform.SetParent(transform, false);
            input.GetComponent<UnityEngine.InputSystem.UI.MultiplayerEventSystem>().playerRoot = gameObject;
            input.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        }
        if (IsVRPreview && desktopCamera != null && menuCanvas != null)
        {
            // Device-free observation keeps the original world-space VR menu and its scene.
            desktopCamera.transform.position = localVRRig.transform.position + Vector3.up * 1.65f;
            desktopCamera.transform.LookAt(menuCanvas.transform.position);
            desktopCamera.stereoTargetEye = StereoTargetEyeMask.None;
            var data = desktopCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data != null) data.allowXRRendering = false;
        }
        if (desktopCamera != null)
        {
            menuCameraPosition = desktopCamera.transform.position;
            menuCameraRotation = desktopCamera.transform.rotation;
        }
        DontDestroyOnLoad(gameObject);
        if (!IsDesktop) GetComponent<VRClientPoseSource>()?.Initialize(this);
        if (ConnectionTestViews.Instance != null) ConnectionTestViews.Instance.Select(this);
    }
    /// <summary>Routes rendering and input to the peer selected with F2.</summary>
    public void SetViewSelected(bool selected)
    {
        if (ViewSelected == selected) return;
        ViewSelected = selected;
        foreach (var root in menuWorldRoots) if (root != null) root.SetActive(selected && shownStage == 0);
        if (desktopPlayer != null)
            foreach (var visual in desktopPlayer.GetComponentsInChildren<Renderer>(true)) visual.enabled = selected;
        // Preserve each camera's SDK/authored state. Enabling every eye and fallback camera breaks VR rendering.
        if (!selected)
        {
            viewStates.Clear();
            foreach (var component in GetComponentsInChildren<UnityEngine.Behaviour>(true))
                if (component is Canvas || component is Camera || component is AudioListener
                    || component is UnityEngine.EventSystems.EventSystem)
                {
                    viewStates[component] = component.enabled;
                    component.enabled = false;
                }
        }
        else
            foreach (var pair in viewStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (Runner != null && Runner.IsRunning) Runner.SetVisible(selected);
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
        int index = SceneUtility.GetBuildIndexByScenePath(NetworkSceneSettings.Current.LobbyScene);
        if (index < 0) { SetStatus("Enable the Lobby scene in the build Scene List."); return false; }
        IsBusy = true;
        NetworkSessionController connecting = null;
        try
        {
            SetStatus($"Connecting to {room}...");
            connecting = NetworkSessionController.Create(name + " Runner");
            session = connecting;
            connecting.BindClient(this);
            connecting.Stopped += (stopped, reason) =>
            {
                if (session == stopped)
                {
                    session = null;
                    SetStatus("Disconnected: " + reason);
                }
            };
            var result = await connecting.StartAsync(GameMode.Client, room, region, index);
            if (!result.Ok || !IsConnected)
            {
                await connecting.ShutdownAsync();
                SetStatus("Connection failed: " + result.ShutdownReason);
                if (session == connecting) session = null;
                if (connecting != null) Destroy(connecting.gameObject);
                return false;
            }
            // Only the visible VR/desktop operator client renders its copy of the world.
            if (Runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple) Runner.SetVisible(ViewSelected);
            SetStatus($"Connected: {Runner.SessionInfo.Name}. Loading Lobby...");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (connecting != null) await connecting.ShutdownAsync();
            if (session == connecting) session = null;
            if (connecting != null) Destroy(connecting.gameObject);
            SetStatus("Connection failed: " + exception.Message);
            return false;
        }
        finally { IsBusy = false; }
    }

    public async void Disconnect() => await DisconnectAsync();
    public async Task DisconnectAsync()
    {
        if (IsBusy || Runner == null) return;
        IsBusy = true;
        try { await session.ShutdownAsync(); SetStatus("Disconnected. Return to Menu to join again."); }
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
            if (desktopCamera != null && (IsDesktop || IsVRPreview))
            {
                desktopCamera.cullingMask = IsDesktop ? 0 : desktopWorldMask;
                desktopCamera.transform.SetPositionAndRotation(menuCameraPosition, menuCameraRotation);
            }
            foreach (var root in menuWorldRoots) if (root != null) root.SetActive(ViewSelected);
            returning = false;
            SetStatus("Ready to connect.");
        }
        else
        {
            Destroy(gameObject); // Destroy the preserved rig/UI before a fresh menu creates replacements.
            await Task.Yield(); // Let the old rig's OnDestroy clear headset SDK singletons first.
            SceneManager.LoadScene(NetworkSceneSettings.Current.MenuScene);
        }
    }

    void Update()
    {
        var state = IsConnected ? State : null;
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
                    if (desktopPlayer != null) desktopPlayer.Warp(spawn);
                    else desktopCamera.transform.SetPositionAndRotation(spawn.position + Vector3.up * 1.65f, spawn.rotation);
                    desktopCamera.cullingMask = desktopWorldMask;
                }
                if (sessionCanvas != null)
                    sessionCanvas.transform.SetPositionAndRotation(spawn.position + spawn.rotation * new Vector3(0, 1.5f, 2), spawn.rotation);
            }
        }
        if (state != null)
            Status = $"{state.Phase} | Players: {state.PlayerCount} | Loaded: {state.LoadedPlayers}/{state.PlayerCount}"
                + (state.Phase == LobbySessionState.SessionPhase.Lobby ? "\nWaiting for the server to start." : "");
        if (state != null && !IsDesktop && !IsVRPreview)
        {
            var source = GetComponent<VRClientPoseSource>();
            if (source != null && !source.TrackingAvailable) Status += "\nWaiting for VR tracking.";
        }
        string viewLabel = ConnectionTestViews.Instance != null ? name + " | F2 next view\n" : "";
        if (statusText != null) statusText.text = viewLabel + Status;
        if (sessionStatusText != null) sessionStatusText.text = viewLabel + Status
            + (IsDesktop && IsConnected ? "\n" + gameplayFeedback : "")
            + (IsDesktop && IsConnected ? "\nPC: WASD move | RMB look | Q/E down/up | Shift fast" : "");
        bool canEdit = !IsBusy && !IsConnected;
        if (connectButton != null) connectButton.interactable = canEdit;
        if (roomNameInput != null) roomNameInput.interactable = canEdit;
        if (regionInput != null) regionInput.interactable = canEdit;
    }
    void LateUpdate()
    {
        // The preview camera follows the same anchor hierarchy sampled by the VR pose sender.
        if (!IsVRPreview || !IsConnected || desktopCamera == null) return;
        var source = GetComponent<VRClientPoseSource>();
        if (source != null && source.HeadAnchor != null)
            desktopCamera.transform.SetPositionAndRotation(source.HeadAnchor.position, source.HeadAnchor.rotation);
    }
    void SetStatus(string message) { Status = message; Debug.Log("[ConnectionManager] " + message, this); }
}
