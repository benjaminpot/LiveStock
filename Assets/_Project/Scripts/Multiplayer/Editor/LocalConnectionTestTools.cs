using System;
using System.Linq;
using Fusion;
using GDC.Metaverse.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

/// <summary>Project-specific test controls built on GDC's PeerModeTools; no test component is required in a scene.</summary>
[InitializeOnLoad]
public static class LocalConnectionTestTools
{
    const string RunningKey = "LiveStock.FlowTestRunning";
    const string PreviousModeKey = "LiveStock.FlowPreviousPeerMode";
    const string DesktopKey = "LiveStock.FlowDesktopTest";
    const string PreviousXRKey = "LiveStock.FlowPreviousXR";
    const string PreviousDisplayKey = "LiveStock.FlowPreviousDisplay";

    static LocalConnectionTestTools() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    const string ClientPrefab = "Assets/_Project/Prefabs/Multiplayer/ConnectionTestClient.prefab";
    const string PendingKey = "LiveStock.FlowPendingAction";

    [MenuItem("LiveStock/Connection Test/Open VR Client Menu")]
    public static void StartVRTest() => Execute("VR");
    [MenuItem("LiveStock/Connection Test/Open VR Client Menu (no headset)")]
    public static void StartVRPreview() => Execute("VR Preview");
    [MenuItem("LiveStock/Connection Test/Open Desktop Client Menu")]
    public static void StartDesktopTest() => Execute("Desktop");
    [MenuItem("LiveStock/Connection Test/Start Server")]
    public static void StartServer() => Execute("Server");
    [MenuItem("LiveStock/Connection Test/Stop Server")]
    public static void StopServer() => UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>()?.StopServer();
    [MenuItem("LiveStock/Connection Test/Disconnect Selected Client")]
    public static void DisconnectSelected() { if (ConnectionTestViews.Instance?.Selected is ConnectionManager client) client.ReturnToMenu(); }
    [MenuItem("LiveStock/Connection Test/Close Selected Client")]
    public static async void CloseSelected()
    {
        if (ConnectionTestViews.Instance?.Selected is not ConnectionManager client) return;
        if (client.IsBusy) throw new InvalidOperationException("Wait for the current connection operation to finish before closing this Client.");
        await client.DisconnectAsync();
        UnityEngine.Object.Destroy(client.gameObject);
    }
    [MenuItem("LiveStock/Connection Test/Next View (F2)")]
    public static void NextView() => ConnectionTestViews.Instance?.NextView();

    static void Execute(string action)
    {
        if (EditorApplication.isPlaying)
        {
            if (ConnectionTestViews.Instance == null) throw new InvalidOperationException("Stop Play Mode first, then open a Connection Test entry.");
            Perform(action);
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes first.");
        SessionState.SetInt(PreviousModeKey, (int)PeerModeTools.Current);
        SessionState.SetBool(PreviousXRKey, XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.InitManagerOnStart);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(DesktopKey, false); // Each Client owns its desktop/VR role.
        SessionState.SetBool("LiveStock.FlowVRPreview", action == "VR Preview");
        SessionState.SetString(PendingKey, action);
        if (XRGeneralSettings.Instance != null) XRGeneralSettings.Instance.InitManagerOnStart = action == "VR";
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        var display = gameView.GetType().GetProperty("targetDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (display != null && display.CanWrite) { SessionState.SetInt(PreviousDisplayKey, (int)display.GetValue(gameView)); display.SetValue(gameView, 0); }
        PeerModeTools.SetPeerMode(NetworkProjectConfig.PeerModes.Multiple);
        EditorSceneManager.OpenScene(ServerConnectionManager.SceneFolder
            + (action.StartsWith("VR") ? "Menu_VRClient.unity" : "ConnectionTestBootstrap.unity"), OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    static async void Perform(string action)
    {
        if (ConnectionTestViews.Instance == null) new GameObject("Connection Test Views").AddComponent<ConnectionTestViews>();
        if (action == "Server")
        {
            // Starting the server must not turn off the headset client's cameras and HUD.
            bool keepVRView = ConnectionTestViews.Instance.Selected is ConnectionManager client && !client.IsDesktop;
            var existing = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
            if (existing != null)
            {
                await existing.StartServerAsync();
                if (!keepVRView) ConnectionTestViews.Instance.Select(existing);
                return;
            }
            await SceneManager.LoadSceneAsync(ServerConnectionManager.SceneFolder + "ServerBootstrap.unity", LoadSceneMode.Additive);
            if (!keepVRView) ConnectionTestViews.Instance.Select(UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>());
            return;
        }
        bool desktop = action == "Desktop";
        if (!desktop)
        {
            var existing = ConnectionManager.Clients.FirstOrDefault(c => c != null && !c.IsDesktop);
            if (existing != null) { ConnectionTestViews.Instance.Select(existing); return; }
            // Only the explicit device-free action suppresses XR. Never silently change a VR menu to desktop UI.
            SessionState.SetBool("LiveStock.FlowVRPreview", action == "VR Preview");
            await SceneManager.LoadSceneAsync(ServerConnectionManager.SceneFolder + "Menu_VRClient.unity", LoadSceneMode.Additive);
            var vr = ConnectionManager.Clients.Single(c => c != null && !c.IsDesktop);
            ConnectionTestViews.Instance.Select(vr);
            if (action == "VR" && (XRGeneralSettings.Instance == null || XRGeneralSettings.Instance.Manager == null
                || XRGeneralSettings.Instance.Manager.activeLoader == null))
                Debug.LogWarning("VR menu opened without an XR loader. For headset tracking, end this test and open the VR menu first.");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClientPrefab);
        var root = UnityEngine.Object.Instantiate(prefab);
        root.name = "Desktop Client " + (ConnectionManager.Clients.Count + 1);
        root.GetComponent<ConnectionManager>().ConfigureDesktop(true);
        root.SetActive(true);
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            string action = SessionState.GetString(PendingKey, "");
            SessionState.EraseString(PendingKey);
            Perform(action);
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(DesktopKey, false);
            SessionState.SetBool("LiveStock.FlowVRPreview", false);
            if (XRGeneralSettings.Instance != null) XRGeneralSettings.Instance.InitManagerOnStart = SessionState.GetBool(PreviousXRKey, true);
            var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            var display = gameView.GetType().GetProperty("targetDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (display != null && display.CanWrite) display.SetValue(gameView, SessionState.GetInt(PreviousDisplayKey, 0));
            PeerModeTools.SetPeerMode((NetworkProjectConfig.PeerModes)SessionState.GetInt(PreviousModeKey, (int)NetworkProjectConfig.PeerModes.Single));
        }
    }

    public static async void AddTestClient() => await AddClientAsync(0);

    public static async void AddDelayedClient() => await AddClientAsync(5);

    /// <summary>Compatibility entry for automated tests, using the same desktop Client prefab.</summary>
    public static async System.Threading.Tasks.Task<ConnectionManager> AddClientAsync(float readyDelay)
    {
        if (!EditorApplication.isPlaying || NetworkProjectConfig.Global.PeerMode != NetworkProjectConfig.PeerModes.Multiple)
            throw new InvalidOperationException("Start a local Multiple test first.");
        var server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        if (server == null || server.Runner == null || !server.Runner.IsRunning) throw new InvalidOperationException("Wait for the server to open its Lobby.");
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ClientPrefab));
        var client = root.GetComponent<ConnectionManager>();
        client.ConfigureDesktop(true);
        root.name = "Test Client " + ConnectionManager.Clients.Count;
        client.ReadyReportDelaySeconds = readyDelay;
        root.SetActive(true);
        if (!await client.ConnectAsync(server.RoomName, server.Region)) { UnityEngine.Object.Destroy(client.gameObject); return null; }
        return client;
    }


    public static async void LeaveLastClient()
    {
        var client = UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)
            .Where(c => c.name.StartsWith("Test Client ")).OrderBy(c => c.name).LastOrDefault();
        if (client == null) return;
        await client.DisconnectAsync();
        UnityEngine.Object.Destroy(client.gameObject);
    }
    [MenuItem("LiveStock/Connection Test/End Test")]
    public static void EndTest() { if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; }
}
