using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Starts a dedicated server and exposes separate map-loading and gameplay-start operations.</summary>
public sealed class ServerConnectionManager : MonoBehaviour
{
    public const string SceneFolder = "Assets/_Project/Scenes/_flow test/";
    [SerializeField] string roomName = "LIVESTOCK-DEV";
    [SerializeField] string region = "asia";
    [SerializeField, Range(1, 16)] int playerLimit = 4;
    [SerializeField] NetworkObject sessionStatePrefab;
    [SerializeField] TMP_Text roomText, statusText, playerCountText;
    [SerializeField] UnityEngine.UI.Button startGameButton;
    [SerializeField, Tooltip("Loads and positions players without starting NPC simulation.")]
    UnityEngine.UI.Button loadMapButton;
    [SerializeField] bool startOnPlay = true;

    NetworkSessionController session;
    public NetworkRunner Runner => session != null ? session.Runner : null;
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Server not started.";
    public LobbySessionState State => LobbySessionState.For(Runner);
    public string RoomName { get => roomName; set => roomName = value; }
    public string Region { get => region; set => region = value; }

    void Awake()
    {
        DontDestroyOnLoad(transform.root.gameObject);
        Application.runInBackground = true;
    }
    async void Start() { if (startOnPlay) await StartServerAsync(); }

    /// <summary>Creates a fresh Server Runner; the room exists only after StartGame succeeds.</summary>
    public async Task<bool> StartServerAsync()
    {
        if (!Application.isPlaying) { Status = "Enter Play Mode before starting the server."; return false; }
        if (IsBusy || (Runner != null && Runner.IsRunning)) return false;
        int lobbyIndex = SceneUtility.GetBuildIndexByScenePath(NetworkSceneSettings.Current.LobbyScene);
        if (lobbyIndex < 0 || sessionStatePrefab == null || string.IsNullOrWhiteSpace(roomName)
            || string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdFusion))
        { Status = "Check Fusion App ID, room name, Lobby scene and session-state prefab."; return false; }
        IsBusy = true;
        NetworkSessionController starting = null;
        try
        {
            Status = "Opening Lobby...";
            starting = NetworkSessionController.Create("Lobby Server Runner");
            session = starting;
            starting.Stopped += (stopped, reason) =>
            {
                if (session == stopped)
                {
                    session = null;
                    Status = "Server stopped: " + reason;
                }
            };
            var result = await starting.StartAsync(GameMode.Server, roomName, region, lobbyIndex, playerLimit);
            if (!result.Ok) throw new InvalidOperationException(result.ShutdownReason.ToString());
            // Fusion's persistence flag applies on all replicas, not just the server instance.
            await Runner.SpawnAsync(sessionStatePrefab, flags: NetworkSpawnFlags.DontDestroyOnLoad);
            // Only the local peer selector may hide this world; standalone CCTV needs its renderers enabled.
            if (Runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple)
                Runner.SetVisible(ConnectionTestViews.Instance == null);
            Status = "Lobby open. Waiting for players.";
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            if (starting != null) await starting.ShutdownAsync();
            if (session == starting) session = null;
            if (starting != null) Destroy(starting.gameObject);
            Status = "Server failed: " + ex.Message;
            return false;
        }
        finally { IsBusy = false; }
    }

    /// <summary>Only the authoritative server may close the room and load the game scene.</summary>
    public void LoadMainGame()
    {
        var state = State;
        if (IsBusy || Runner == null || !Runner.IsServer || state == null || !state.CanLoadMap) return;
        int index = SceneUtility.GetBuildIndexByScenePath(NetworkSceneSettings.Current.MainGameScene);
        if (index < 0) { Status = "Enable MainGame in the build scene list."; return; }
        state.Phase = LobbySessionState.SessionPhase.LoadingMainGame;
        Runner.SessionInfo.IsOpen = false;
        Status = "Loading MainGame...";
        Runner.LoadScene(SceneRef.FromIndex(index), LoadSceneMode.Single);
    }

    /// <summary>Starts gameplay only after all connected players finish map loading.</summary>
    public void StartGame()
    {
        var state = State;
        if (IsBusy || state == null || !state.CanStart) return;
        state.Phase = LobbySessionState.SessionPhase.Running;
    }

    public async void StopServer() => await StopServerAsync();
    public async Task StopServerAsync()
    {
        if (IsBusy || Runner == null) return;
        IsBusy = true;
        try { await session.ShutdownAsync(); }
        finally { IsBusy = false; }
    }

    void Update()
    {
        var state = State;
        if (state != null)
            Status = state.Phase == LobbySessionState.SessionPhase.Lobby ? "Lobby - ready to load map."
                : state.Phase == LobbySessionState.SessionPhase.LoadingMainGame ? "Loading MainGame..." : state.Phase == LobbySessionState.SessionPhase.AwaitingStart ? "Map ready. Waiting for Start Game." : "Game running.";
        if (roomText != null) roomText.text = $"Room: {roomName} | Region: {region}";
        if (statusText != null) statusText.text = Status;
        if (playerCountText != null) playerCountText.text = state == null ? "Connected players: 0"
            : $"Connected players: {state.PlayerCount} | Loaded: {state.LoadedPlayers}/{state.PlayerCount}";
        if (loadMapButton != null) loadMapButton.interactable = !IsBusy && state != null && state.CanLoadMap;
        if (startGameButton != null) startGameButton.interactable = !IsBusy && state != null && state.CanStart;
    }
}
