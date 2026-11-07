using System;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Standalone LogInMenu-to-Fusion connection template.
/// This handles room joining, not account authentication or VR avatar spawning.
/// </summary>
public class ConnectionManager : MonoBehaviour
{
    [Header("LogInMenu references")]
    [SerializeField] TMP_InputField roomNameInput;
    [SerializeField] TMP_InputField regionInput;
    [Tooltip("Assign this for interactability, and wire On Click () to Connect manually.")]
    [SerializeField] Button connectButton;
    [SerializeField] TMP_Text statusText;

    [Header("Default Value")]
    [SerializeField] string defaultRoomName = "LIVESTOCK-DEV";
    [Tooltip("Photon region code, e.g. asia or jp. Empty uses global Photon settings.")]
    [SerializeField] string defaultRegion = "asia";

    [Header("Scene enabled in Build Profiles > Scene List")]
    [SerializeField] string networkScenePath = "Assets/_Project/Scenes/_flow test/Lobby.unity";

    public NetworkRunner Runner { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsConnected => Runner != null && Runner.IsRunning && Runner.IsClient;
    public string Status { get; private set; } = "Ready to connect.";

    void Awake()
    {
        // Put this component on a dedicated root object, outside LogInMenu.
        // It must survive when Fusion loads the server's gameplay scene.
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Wire the Connect button's On Click () event to this method.</summary>
    [ContextMenu("Connect to server")]
    public async void Connect()
    {
        if (IsBusy || IsConnected) return; // Prevent overlapping connection attempts.

        // Read the latest input only when the player presses Connect.
        string room = (roomNameInput != null ? roomNameInput.text : defaultRoomName)?.Trim();
        string region = (regionInput != null ? regionInput.text : defaultRegion)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(room))
        {
            SetStatus("Please enter a room name.");
            return;
        }
        if (string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdFusion))
        {
            SetStatus("Set App Id Fusion in Fusion > Realtime Settings first.");
            return;
        }
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(networkScenePath);
        if (sceneIndex < 0)
        {
            SetStatus("Enable the configured network scene in the build Scene List.");
            return;
        }

        IsBusy = true;
        SetStatus($"Connecting to {room}...");
        GameObject runnerObject = null;
        try
        {
            // A shut-down Fusion Runner cannot be reused. Create one for each attempt.
            runnerObject = new GameObject("VR Client Runner");
            DontDestroyOnLoad(runnerObject);
            Runner = runnerObject.AddComponent<NetworkRunner>();
            // Future VR input callbacks can submit tracking data through this Runner.
            // ProvideInput alone does not send head/hand poses or create an avatar.
            Runner.ProvideInput = true;
            var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            var events = runnerObject.AddComponent<NetworkEvents>();
            events.OnShutdown ??= new NetworkEvents.ShutdownEvent();
            events.OnShutdown.AddListener((runner, reason) =>
            {
                // During Connect/Disconnect, those methods own the final status message.
                if (!IsBusy) SetStatus("Disconnected: " + reason);
                if (Runner == runner) Runner = null;
                Destroy(runner.gameObject);
            });
            Runner.AddCallbacks(events);

            // Copy per Runner; never change the shared Photon settings asset at runtime.
            var settings = PhotonAppSettings.Global.AppSettings.GetCopy();
            if (!string.IsNullOrEmpty(region)) settings.FixedRegion = region;
            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);

            // Client joins an EXISTING server room. It cannot create a missing room.
            // Server and client must agree on App ID, App Version, region and room name.
            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = room,
                CustomPhotonAppSettings = settings,
                EnableClientSessionCreation = false,
                SceneManager = sceneManager,
                Scene = scene
            });
            if (result.Ok && IsConnected)
                SetStatus($"Connected: {Runner.SessionInfo.Name} | Region: {Runner.SessionInfo.Region}");
            else
            {
                SetStatus("Connection failed: " + result.ShutdownReason);
                Runner = null;
                if (runnerObject != null) Destroy(runnerObject);
            }
        }
        catch (Exception exception)
        {
            SetStatus("Connection failed: " + exception.Message);
            Debug.LogException(exception, this);
            // Clean up even if an exception happens before StartGame returns.
            var failedRunner = Runner;
            Runner = null;
            try
            {
                if (failedRunner != null) await failedRunner.Shutdown();
            }
            catch (Exception cleanupException) { Debug.LogException(cleanupException, this); }
            if (runnerObject != null) Destroy(runnerObject);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Optionally wire a Leave button to this method.</summary>
    public async void Disconnect()
    {
        if (IsBusy || Runner == null) return;
        IsBusy = true;
        try
        {
            await Runner.Shutdown();
            SetStatus("Disconnected. Reopen the login menu to join again.");
        }
        catch (Exception exception)
        {
            SetStatus("Disconnect failed: " + exception.Message);
            Debug.LogException(exception, this);
        }
        finally { IsBusy = false; }
        // Menu navigation belongs to your UI flow; this does not reload the menu scene.
    }

    void Update()
    {
        // UI is optional: unassigned fields use defaults, and no status label is required.
        bool canEdit = !IsBusy && !IsConnected;
        if (connectButton != null) connectButton.interactable = canEdit;
        if (roomNameInput != null) roomNameInput.interactable = canEdit;
        if (regionInput != null) regionInput.interactable = canEdit;
        if (statusText != null) statusText.text = Status;
    }

    void SetStatus(string message)
    {
        Status = message;
        if (statusText != null) statusText.text = message;
        Debug.Log("[ConnectionManager] " + message, this);
    }
}
