using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Starts a dedicated server and owns the desktop-only Start Game operation.</summary>
public sealed class ServerConnectionManager : MonoBehaviour
{
    public const string SceneFolder = "Assets/_Project/Scenes/_flow test/";
    [SerializeField] string roomName = "LIVESTOCK-DEV";
    [SerializeField] string region = "asia";
    [SerializeField, Range(1, 16)] int playerLimit = 4;
    [SerializeField] NetworkObject sessionStatePrefab;
    [SerializeField] TMP_Text roomText, statusText, playerCountText;
    [SerializeField] UnityEngine.UI.Button startGameButton;
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
        int lobbyIndex = SceneUtility.GetBuildIndexByScenePath(SceneFolder + "Lobby.unity");
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
            if (Runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple) Runner.SetVisible(false);
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
    public void StartGame()
    {
        var state = State;
        if (IsBusy || Runner == null || !Runner.IsServer || state == null || !state.CanStart) return;
        int index = SceneUtility.GetBuildIndexByScenePath(SceneFolder + "MainGame.unity");
        if (index < 0) { Status = "Enable MainGame in the build scene list."; return; }
        state.Phase = LobbySessionState.SessionPhase.LoadingMainGame;
        Runner.SessionInfo.IsOpen = false;
        Status = "Loading MainGame...";
        Runner.LoadScene(SceneRef.FromIndex(index), LoadSceneMode.Single);
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
            Status = state.Phase == LobbySessionState.SessionPhase.Lobby ? "Lobby - waiting for Start Game."
                : state.Phase == LobbySessionState.SessionPhase.LoadingMainGame ? "Loading MainGame..." : "All connected players reached MainGame.";
        if (roomText != null) roomText.text = $"Room: {roomName} | Region: {region}";
        if (statusText != null) statusText.text = Status;
        if (playerCountText != null) playerCountText.text = state == null ? "Connected players: 0"
            : $"Connected players: {state.PlayerCount} | Loaded: {state.LoadedPlayers}/{state.PlayerCount}";
        if (startGameButton != null) startGameButton.interactable = !IsBusy && state != null && state.CanStart;
    }
}
