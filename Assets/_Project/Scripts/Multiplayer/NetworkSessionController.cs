using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns one Runner's startup, shutdown and scene callbacks for either local role.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkRunner), typeof(NetworkSceneManagerDefault), typeof(NetworkEvents))]
public sealed class NetworkSessionController : MonoBehaviour
{
    public NetworkRunner Runner { get; private set; }
    public event Action<NetworkSessionController, ShutdownReason> Stopped;
    Task shutdown;
    NetworkInputSource inputSource;

    /// <summary>Attaches this Client's local input cache before starting its Runner.</summary>
    public void BindClient(ConnectionManager client)
    {
        inputSource = gameObject.AddComponent<NetworkInputSource>();
        inputSource.Initialize(client);
    }

    /// <summary>Creates a persistent peer. Each Client and Server gets its own controller.</summary>
    public static NetworkSessionController Create(string label)
    {
        var controller = new GameObject(label).AddComponent<NetworkSessionController>();
        DontDestroyOnLoad(controller.gameObject);
        return controller;
    }

    void Awake()
    {
        Runner = GetComponent<NetworkRunner>();
        // Fusion removes this helper in Single mode, so it cannot be a required component.
        if (NetworkProjectConfig.Global.PeerMode == NetworkProjectConfig.PeerModes.Multiple)
            gameObject.AddComponent<RunnerEnableVisibility>();
        var manager = GetComponent<NetworkSceneManagerDefault>();
        manager.IsSceneTakeOverEnabled = false;
        var events = GetComponent<NetworkEvents>();
        events.OnInput ??= new NetworkEvents.InputEvent();
        events.OnInput.AddListener((runner, input) => input.Set(inputSource != null ? inputSource.Current : default));
        events.OnDisconnectedFromServer ??= new NetworkEvents.DisconnectFromServerEvent();
        events.OnDisconnectedFromServer.AddListener((runner, reason) => { _ = ShutdownAsync(); });
        events.OnShutdown ??= new NetworkEvents.ShutdownEvent();
        events.OnShutdown.AddListener((runner, reason) =>
        {
            Stopped?.Invoke(this, reason);
            Destroy(gameObject);
        });
        events.PlayerLeft ??= new NetworkEvents.PlayerEvent();
        events.PlayerLeft.AddListener((runner, player) =>
        {
            if (runner.IsServer) NetworkWorld.For(runner)?.RemovePlayer(player);
        });
        events.OnSceneLoadStart ??= new NetworkEvents.RunnerEvent();
        events.OnSceneLoadStart.AddListener(runner =>
        {
            if (inputSource != null) inputSource.Clear();
            if (runner.IsServer) NetworkWorld.For(runner)?.BeginSceneLoad();
        });
        Runner.AddCallbacks(events);
    }

    /// <summary>Starts Fusion with the same room, region and scene contract for both entry points.</summary>
    public Task<StartGameResult> StartAsync(GameMode mode, string room, string region, int sceneIndex, int playerLimit = 4)
    {
        var settings = PhotonAppSettings.Global.AppSettings.GetCopy();
        if (!string.IsNullOrWhiteSpace(region)) settings.FixedRegion = region.Trim().ToLowerInvariant();
        var scene = new NetworkSceneInfo();
        scene.AddSceneRef(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
        Runner.ProvideInput = mode == GameMode.Client;
        return Runner.StartGame(new StartGameArgs
        {
            GameMode = mode, SessionName = room.Trim(), PlayerCount = playerLimit,
            CustomPhotonAppSettings = settings, SceneManager = GetComponent<NetworkSceneManagerDefault>(),
            Scene = scene, EnableClientSessionCreation = mode == GameMode.Server,
            IsOpen = true, IsVisible = true
        });
    }

    /// <summary>Shares a single shutdown operation between disconnect callbacks and UI requests.</summary>
    public Task ShutdownAsync()
    {
        if (shutdown == null) shutdown = Runner.Shutdown();
        return shutdown;
    }
}
