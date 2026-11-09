using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>Server-owned room state that survives Lobby-to-MainGame scene changes.</summary>
public sealed class LobbySessionState : NetworkBehaviour
{
    public enum SessionPhase { Lobby = 1, LoadingMainGame = 2, InMainGame = 3 }
    [Networked] public SessionPhase Phase { get; set; }
    [Networked] public int PlayerCount { get; set; }
    [Networked] public int LoadedPlayers { get; set; }

    static readonly List<LobbySessionState> instances = new();
    readonly Dictionary<PlayerRef, int> loadedStages = new();
    int sentStage;
    int pendingStage;
    float readyAt;
    public NetworkWorld World { get; private set; }

    void Awake() => World = GetComponent<NetworkWorld>();

    public bool CanStart => Object != null && Object.IsValid && Object.HasStateAuthority
        && Phase == SessionPhase.Lobby && PlayerCount > 0 && LoadedPlayers == PlayerCount
        && PlayerCount == Runner.ActivePlayers.Count() && !Runner.IsSceneManagerBusy;

    /// <summary>Returns the replicated state for a specific peer, never another local Runner.</summary>
    public static LobbySessionState For(NetworkRunner runner) =>
        instances.FirstOrDefault(s => s != null && s.Object != null && s.Object.IsValid && s.Runner == runner);

    public override void Spawned()
    {
        instances.Add(this);
        if (Object.HasStateAuthority) Phase = SessionPhase.Lobby;
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => instances.Remove(this);
    void OnDestroy() => instances.Remove(this);

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        var players = Runner.ActivePlayers.ToArray();
        foreach (var left in loadedStages.Keys.Where(p => !players.Contains(p)).ToArray()) loadedStages.Remove(left);
        int expectedStage = Phase == SessionPhase.Lobby ? 1 : 2;
        PlayerCount = players.Length; // A dedicated server has no local player to count.
        LoadedPlayers = players.Count(p => loadedStages.TryGetValue(p, out int stage) && stage == expectedStage);
        var marker = FlowSceneMarker.For(Runner);
        if (Phase == SessionPhase.LoadingMainGame && !Runner.IsSceneManagerBusy
            && marker != null && marker.Stage == 2 && LoadedPlayers == PlayerCount)
            Phase = SessionPhase.InMainGame;
    }

    void Update()
    {
        if (Object == null || !Object.IsValid || !Runner.IsRunning || !Runner.IsClient || Runner.IsSceneManagerBusy) return;
        var marker = FlowSceneMarker.For(Runner);
        if (marker == null || marker.Stage == sentStage) return;
        if (pendingStage != marker.Stage)
        {
            pendingStage = marker.Stage;
            var owner = ConnectionManager.For(Runner);
            readyAt = Time.unscaledTime + (owner != null ? owner.ReadyReportDelaySeconds : 0f);
        }
        if (Time.unscaledTime < readyAt) return;
        var client = ConnectionManager.For(Runner);
        RPC_ReportLoaded(marker.Stage, client != null && !client.IsDesktop);
        sentStage = marker.Stage;
    }

    // The sender is identified by Fusion. Never trust a player ID supplied by a client.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_ReportLoaded(int stage, bool vrPlayer, RpcInfo info = default)
    {
        int expectedStage = Phase == SessionPhase.Lobby ? 1 : 2;
        if (stage != expectedStage || !Runner.ActivePlayers.Contains(info.Source)) return;
        if (!World.RegisterPlayer(info.Source, vrPlayer, stage)) return;
        loadedStages[info.Source] = stage;
    }
}
