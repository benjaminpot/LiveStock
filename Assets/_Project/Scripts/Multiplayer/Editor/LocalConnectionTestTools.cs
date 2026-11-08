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

    [UnityEditor.Callbacks.DidReloadScripts]
    static void ConfirmBuildReload()
    {
        string token = SessionState.GetString("LiveStock.FlowBuildReloadToken", "");
        if (!string.IsNullOrEmpty(token))
        {
            System.IO.Directory.CreateDirectory(".tmp");
            System.IO.File.WriteAllText(".tmp/flow_build_reload_done.txt", token);
            SessionState.EraseString("LiveStock.FlowBuildReloadToken");
        }
    }

    [MenuItem("LiveStock/Connection Test/Start Server + VR Menu")]
    public static void StartVRTest() => StartTest(false);
    [MenuItem("LiveStock/Connection Test/Start Server + Desktop Menu (no headset)")]
    public static void StartDesktopTest() => StartTest(true);

    static void StartTest(bool desktop)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop the current test first.");
        // Fail explicitly instead of opening a modal save dialog during automated testing.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before starting a connection test.");
        SessionState.SetInt(PreviousModeKey, (int)PeerModeTools.Current);
        SessionState.SetBool(PreviousXRKey, XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.InitManagerOnStart);
        SessionState.SetBool(DesktopKey, desktop);
        SessionState.SetBool(RunningKey, true);
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        var display = gameView.GetType().GetProperty("targetDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (display != null && display.CanWrite)
        {
            SessionState.SetInt(PreviousDisplayKey, (int)display.GetValue(gameView));
            display.SetValue(gameView, 0);
        }
        PeerModeTools.SetPeerMode(NetworkProjectConfig.PeerModes.Multiple);
        EditorSceneManager.OpenScene(ServerConnectionManager.SceneFolder + "Menu_VRClient.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    static async void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            // Additive startup preserves the menu while the Server Runner loads its own Lobby copy.
            await SceneManager.LoadSceneAsync(ServerConnectionManager.SceneFolder + "ServerBootstrap.unity", LoadSceneMode.Additive);
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(DesktopKey, false);
            if (XRGeneralSettings.Instance != null) XRGeneralSettings.Instance.InitManagerOnStart = SessionState.GetBool(PreviousXRKey, true);
            var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            var display = gameView.GetType().GetProperty("targetDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (display != null && display.CanWrite) display.SetValue(gameView, SessionState.GetInt(PreviousDisplayKey, 0));
            PeerModeTools.SetPeerMode((NetworkProjectConfig.PeerModes)SessionState.GetInt(PreviousModeKey, (int)NetworkProjectConfig.PeerModes.Single));
        }
    }

    [MenuItem("LiveStock/Connection Test/Add Test Client")]
    public static async void AddTestClient() => await AddClientAsync(0);
    [MenuItem("LiveStock/Connection Test/Add Delayed Test Client (5 seconds)")]
    public static async void AddDelayedClient() => await AddClientAsync(5);

    /// <summary>Adds an online client with no headset, rendering camera or audio listener.</summary>
    public static async System.Threading.Tasks.Task<ConnectionManager> AddClientAsync(float readyDelay)
    {
        if (!EditorApplication.isPlaying || NetworkProjectConfig.Global.PeerMode != NetworkProjectConfig.PeerModes.Multiple)
            throw new InvalidOperationException("Start a local Multiple test first.");
        var server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        if (server == null || server.Runner == null || !server.Runner.IsRunning) throw new InvalidOperationException("Wait for the server to open its Lobby.");
        var client = new GameObject("Test Client " + UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None).Length).AddComponent<ConnectionManager>();
        client.ReadyReportDelaySeconds = readyDelay;
        if (!await client.ConnectAsync(server.RoomName, server.Region)) { UnityEngine.Object.Destroy(client.gameObject); return null; }
        return client;
    }

    [MenuItem("LiveStock/Connection Test/Leave Last Test Client")]
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

    [MenuItem("LiveStock/Connection Test/Build Windows Connection Test")]
    public static void BuildWindowsTest()
    {
        var settings = XRGeneralSettings.Instance;
        bool previous = settings != null && settings.InitManagerOnStart;
        try
        {
            if (settings != null) { settings.InitManagerOnStart = false; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); }
            const string bindings = "Builds/ConnectionTest/RuntimeActionBindings.json";
            if (System.IO.File.Exists(bindings)) System.IO.File.Delete(bindings); // Generated by Meta's build hook; it does not overwrite old copies.
            PeerModeTools.Build("Builds/ConnectionTest/LiveStockFlow.exe", BuildTarget.StandaloneWindows64);
        }
        finally
        {
            if (settings != null) { settings.InitManagerOnStart = previous; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); }
        }
    }
}
