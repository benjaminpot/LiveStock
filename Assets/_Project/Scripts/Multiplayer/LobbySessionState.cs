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
    [SerializeField, Tooltip("Server-spawned desktop visual, registered as a Fusion prefab.")]
    NetworkObject desktopAvatarPrefab;
    [SerializeField, Tooltip("Server-spawned sheep visual with replicated head/hand IK targets.")]
    NetworkObject vrAvatarPrefab;
    readonly Dictionary<PlayerRef, NetworkObject> playerAvatars = new();
    readonly HashSet<PlayerRef> pendingAvatars = new();

    /// <summary>Receives the sender's PC test pose and updates its server-owned visual.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    public void RPC_DesktopPose(Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        if (!Runner.ActivePlayers.Contains(info.Source) || Runner.IsSceneManagerBusy
            || Phase == SessionPhase.LoadingMainGame || !float.IsFinite(position.sqrMagnitude)
            || position.sqrMagnitude > 100000000f || !float.IsFinite(rotation.x + rotation.y + rotation.z + rotation.w)) return;
        if (playerAvatars.TryGetValue(info.Source, out var avatar) && avatar != null)
        {
            if (avatar.GetComponent<DesktopNetworkAvatar>() != null)
                avatar.transform.SetPositionAndRotation(position, rotation.normalized);
        }
        else if (desktopAvatarPrefab != null && pendingAvatars.Add(info.Source))
            SpawnAvatar(info.Source, desktopAvatarPrefab, position, rotation.normalized);
    }

    /// <summary>Accepts VR target poses only for the authenticated sender's sheep.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    public void RPC_VRPose(VRTrackedPose pose, RpcInfo info = default)
    {
        if (!pose.IsFinite || !Runner.ActivePlayers.Contains(info.Source) || Runner.IsSceneManagerBusy
            || Phase == SessionPhase.LoadingMainGame) return;
        if (playerAvatars.TryGetValue(info.Source, out var avatar) && avatar != null)
        {
            var sheep = avatar.GetComponent<VRNetworkAvatar>();
            if (sheep != null) sheep.SetPose(pose);
        }
        else if (vrAvatarPrefab != null && pendingAvatars.Add(info.Source))
            SpawnAvatar(info.Source, vrAvatarPrefab, pose.RootPosition, pose.RootRotation.normalized);
    }

    async void SpawnAvatar(PlayerRef player, NetworkObject prefab, Vector3 position, Quaternion rotation)
    {
        try
        {
            var avatar = await Runner.SpawnAsync(prefab, position, rotation, player,
                flags: NetworkSpawnFlags.DontDestroyOnLoad);
            if (Runner.IsRunning && Runner.ActivePlayers.Contains(player)) playerAvatars[player] = avatar;
            else if (Runner.IsRunning) Runner.Despawn(avatar);
        }
        catch (System.Exception exception) { if (Runner != null && Runner.IsRunning) Debug.LogException(exception, this); }
        finally { pendingAvatars.Remove(player); }
    }

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
        foreach (var left in playerAvatars.Keys.Where(p => !players.Contains(p)).ToArray())
        {
            if (playerAvatars[left] != null) Runner.Despawn(playerAvatars[left]);
            playerAvatars.Remove(left);
        }
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
        loadedStages[info.Source] = stage;
        // A connected VR player exists even before headset tracking becomes available.
        // Pose packets animate the sheep later; they must not be required to create it.
        if (!vrPlayer) return;
        var spawn = FlowSceneMarker.For(Runner)?.VRSpawnPoint;
        if (spawn == null) return;
        if (playerAvatars.TryGetValue(info.Source, out var avatar) && avatar != null)
        {
            if (avatar.GetComponent<VRNetworkAvatar>() != null)
                avatar.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        }
        else if (vrAvatarPrefab != null && pendingAvatars.Add(info.Source))
            SpawnAvatar(info.Source, vrAvatarPrefab, spawn.position, spawn.rotation);
    }
}
