using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>Server-owned player registry, avatar lifetime and pose routing for one room.</summary>
[DisallowMultipleComponent]
public sealed class NetworkWorld : NetworkBehaviour
{
    [Header("Player prefabs")]
    [SerializeField, Tooltip("Desktop avatar registered in Fusion's prefab table.")]
    NetworkObject desktopAvatarPrefab;
    [SerializeField, Tooltip("VR sheep registered in Fusion's prefab table. Created even without tracking.")]
    NetworkObject vrAvatarPrefab;
    [System.Serializable]
    public sealed class SceneSpawn
    {
        public NetworkObject Prefab;
        [Tooltip("Offset relative to the MainGame player spawn point.")]
        public Vector3 Offset;
    }
    readonly List<NetworkObject> spawnedSceneObjects = new();

    sealed class PlayerEntry
    {
        public bool IsVR;
        public int Stage;
        public bool Spawning;
        public NetworkObject Avatar;
        public PlayerBodyPose Body;
        public TickTimer InputExpiry;
    }

    readonly Dictionary<PlayerRef, PlayerEntry> players = new();
    readonly List<PlayerRef> departed = new();
    LobbySessionState state;
    int sceneGeneration;
    bool npcsStarted;
    [Networked] public int CollectedFruitCount { get; private set; }
    [Networked] public int RequiredFruitCount { get; private set; }
    public bool MissionComplete => RequiredFruitCount > 0 && CollectedFruitCount >= RequiredFruitCount;

    public override void Spawned()
    {
        if (Object.HasStateAuthority) RequiredFruitCount = 3;
    }
    [Header("Runtime diagnostics")]
    [SerializeField, Tooltip("Number of accepted input samples on this Server. Client replicas keep their own zero counter.")]
    int acceptedInputCount;
    public int AcceptedInputCount => acceptedInputCount;

    public int RegisteredPlayerCount => players.Count;
    public int AvatarCount => players.Values.Count(p => p.Avatar != null && p.Avatar.IsValid);

    /// <summary>Finds the world through that Runner's replicated room object.</summary>
    public static NetworkWorld For(NetworkRunner runner) => LobbySessionState.For(runner)?.World;

    void Awake() => state = GetComponent<LobbySessionState>();

    /// <summary>Looks up the server-owned avatar belonging to an authenticated player.</summary>
    public bool TryGetPlayer(PlayerRef player, out NetworkObject avatar)
    {
        avatar = players.TryGetValue(player, out var entry) ? entry.Avatar : null;
        return avatar != null && avatar.IsValid;
    }

    /// <summary>Registers a fixed role and moves or creates the avatar after a stage acknowledgement.</summary>
    public bool RegisterPlayer(PlayerRef player, bool isVR, int stage)
    {
        if (!Object.HasStateAuthority || !Runner.ActivePlayers.Contains(player)) return false;
        if (!players.TryGetValue(player, out var entry))
        {
            entry = new PlayerEntry { IsVR = isVR };
            players.Add(player, entry);
        }
        if (entry.IsVR != isVR) return false;
        bool changedStage = entry.Stage != stage;
        entry.Stage = stage;
        var spawn = FlowSceneMarker.For(Runner)?.VRSpawnPoint;
        if (changedStage && entry.Avatar != null && spawn != null)
        {
            entry.Avatar.GetComponent<NetworkTransform>().Teleport(spawn.position, spawn.rotation);
            entry.Avatar.GetComponent<NetworkTargetPose>()?.ResetPose();
        }
        EnsureAvatar(player, entry);
        return true;
    }

    /// <summary>Invalidates pending spawns while keeping each connected player's role across scenes.</summary>
    public void BeginSceneLoad()
    {
        if (!Object.HasStateAuthority) return;
        sceneGeneration++;
        LiveStock.Multiplayer.RandomNPCSpawner.DespawnFor(Runner);
        foreach (var obj in spawnedSceneObjects) if (obj != null && obj.IsValid) Runner.Despawn(obj);
        spawnedSceneObjects.Clear();
        npcsStarted = false;
        CollectedFruitCount = 0;
        RequiredFruitCount = 3;
        foreach (var entry in players.Values) { entry.Stage = 0; InvalidateInput(entry); }
    }

    /// <summary>Removes a departed player's avatar and its registration on the server.</summary>
    public void RemovePlayer(PlayerRef player)
    {
        if (!Object.HasStateAuthority || !players.Remove(player, out var entry)) return;
        if (entry.Avatar != null && entry.Avatar.IsValid) Runner.Despawn(entry.Avatar);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (!npcsStarted && state.Phase == LobbySessionState.SessionPhase.Running && !Runner.IsSceneManagerBusy)
        {
            npcsStarted = true;
            FlowSceneMarker.For(Runner)?.GetComponent<MapNetworkSetup>()?.StartGameplay(this);
        }
        departed.Clear();
        foreach (var pair in players)
        {
            if (!Runner.ActivePlayers.Contains(pair.Key)) departed.Add(pair.Key);
            else
            {
                EnsureAvatar(pair.Key, pair.Value);
                if (Runner.TryGetInputForPlayer(pair.Key, out PlayerNetworkInput input)) ApplyInput(pair.Value, input);
                else if (pair.Value.InputExpiry.ExpiredOrNotRunning(Runner)) InvalidateInput(pair.Value);
            }
        }
        foreach (var player in departed) RemovePlayer(player);
    }

    void EnsureAvatar(PlayerRef player, PlayerEntry entry)
    {
        if (entry.Avatar != null || entry.Spawning || entry.Stage == 0 || Runner.IsSceneManagerBusy) return;
        var marker = FlowSceneMarker.For(Runner);
        if (marker == null || marker.Stage != entry.Stage || marker.VRSpawnPoint == null) return;
        var prefab = entry.IsVR ? vrAvatarPrefab : desktopAvatarPrefab;
        if (prefab != null) SpawnAvatar(player, entry, prefab, marker.VRSpawnPoint);
    }

    async void SpawnAvatar(PlayerRef player, PlayerEntry entry, NetworkObject prefab, Transform spawn)
    {
        var runner = Runner;
        int generation = sceneGeneration;
        entry.Spawning = true;
        try
        {
            var avatar = await runner.SpawnAsync(prefab, spawn.position, spawn.rotation, player,
                flags: NetworkSpawnFlags.DontDestroyOnLoad);
            // A completed spawn may belong to a departed player, an earlier stage or a stopped Runner.
            bool current = this != null && Object != null && Object.IsValid && runner != null && runner.IsRunning
                && generation == sceneGeneration && players.TryGetValue(player, out var registered)
                && ReferenceEquals(registered, entry) && runner.ActivePlayers.Contains(player);
            if (current) entry.Avatar = avatar;
            else if (runner != null && runner.IsRunning && avatar != null && avatar.IsValid) runner.Despawn(avatar);
        }
        catch (System.Exception exception)
        {
            if (this != null && runner != null && runner.IsRunning) Debug.LogException(exception, this);
        }
        finally { entry.Spawning = false; }
    }

    /// <summary>Consumes tick input identified by Fusion and accepts the owner's movement result.</summary>
    void ApplyInput(PlayerEntry entry, PlayerNetworkInput input)
    {
        if (input.IsVR != entry.IsVR || entry.Stage == 0
            || input.Stage != entry.Stage || Runner.IsSceneManagerBusy || entry.Avatar == null
            || state.Phase == LobbySessionState.SessionPhase.LoadingMainGame
            || !input.HasPose || !input.Pose.IsFinite)
        {
            InvalidateInput(entry);
            return;
        }
        if (entry.IsVR) entry.Avatar.GetComponent<VRNetworkAvatar>().SetTrackingValid(true);
        // Tolerate brief packet loss, but never keep an interactive body indefinitely without input.
        entry.InputExpiry = TickTimer.CreateFromSeconds(Runner, .5f);
        entry.Body = input.Body.IsValid && Vector3.Distance(input.Body.Center, input.Pose.RootPosition) < 4f
            ? input.Body : default;
        if (entry.IsVR) entry.Avatar.GetComponent<VRNetworkAvatar>().SetPose(input.Pose);
        else entry.Avatar.transform.SetPositionAndRotation(input.Pose.RootPosition, input.Pose.RootRotation.normalized);
        acceptedInputCount++;
    }

    /// <summary>Revokes interaction and VR tracking when a sample is invalid or its grace period expires.</summary>
    void InvalidateInput(PlayerEntry entry)
    {
        entry.Body = default;
        entry.InputExpiry = default;
        if (entry.IsVR && entry.Avatar != null && entry.Avatar.IsValid)
            entry.Avatar.GetComponent<VRNetworkAvatar>().SetTrackingValid(false);
    }

    /// <summary>Creates a registered Fusion prefab on this Server and tracks its scene lifetime.</summary>
    public NetworkObject SpawnObject(NetworkObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!Object.HasStateAuthority || state.Phase != LobbySessionState.SessionPhase.Running || Runner.IsSceneManagerBusy) return null;
        var obj = Runner.Spawn(prefab, position, rotation);
        if (obj != null) spawnedSceneObjects.Add(obj);
        return obj;
    }

    /// <summary>Routes an interaction from this Client's replicated world to its Server.</summary>
    public void RequestToggle(NetworkObject target)
    {
        if (Object.IsValid && target != null && target.IsValid && target.Runner == Runner) RPC_RequestToggle(target.Id);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_RequestToggle(NetworkId targetId, RpcInfo info = default)
    {
        if (state.Phase != LobbySessionState.SessionPhase.Running || !players.TryGetValue(info.Source, out var player)
            || player.Stage != 2 || player.Avatar == null || !Runner.TryFindObject(targetId, out var target)
            || Vector3.Distance(player.Avatar.transform.position, target.transform.position) > 3f) return;
        target.GetComponent<NetworkSwitch>()?.ToggleAsServer();
    }

    /// <summary>Sends a ray-selected apple request through this player's own room object.</summary>
    public void RequestCollect(FruitPickup fruit, Ray ray)
    {
        if (Object.IsValid && fruit != null && fruit.Object != null && fruit.Object.IsValid && fruit.Runner == Runner)
            RPC_RequestCollect(fruit.Object.Id, ray.origin, ray.direction);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_RequestCollect(NetworkId fruitId, Vector3 origin, Vector3 direction, RpcInfo info = default)
    {
        string feedback = "玩家尚未準備完成";
        if (state.Phase == LobbySessionState.SessionPhase.Running && !Runner.IsSceneManagerBusy
            && players.TryGetValue(info.Source, out var player) && player.Stage == 2 && player.Avatar != null
            && Runner.TryFindObject(fruitId, out var target) && spawnedSceneObjects.Contains(target)
            && target.TryGetComponent<FruitPickup>(out var fruit))
        {
            bool tracking = !player.IsVR || player.Avatar.GetComponent<VRNetworkAvatar>().TrackingValid;
            if (!tracking || player.InputExpiry.ExpiredOrNotRunning(Runner) || !player.Body.IsValid) feedback = "等待玩家追蹤資料";
            else if (fruit.IsCollected) feedback = "這顆蘋果已被收集";
            else if (!fruit.IsNearby(player.Body)) feedback = "請靠近蘋果";
            else if (!PlayerBodyPose.Finite(origin) || Vector3.Distance(origin, player.Body.Center) > (player.IsVR ? 3f : 5f)
                || !fruit.HasClearRay(new Ray(origin, direction), player.Avatar.transform)) feedback = "請重新瞄準蘋果";
            else if (fruit.CollectAsServer())
            {
                CollectedFruitCount = spawnedSceneObjects.Count(o => o != null && o.IsValid
                    && o.TryGetComponent<FruitPickup>(out var apple) && apple.IsCollected);
                feedback = MissionComplete ? "蘋果已收集完成！" : $"已收集蘋果 {CollectedFruitCount}/{RequiredFruitCount}";
            }
        }
        RPC_CollectFeedback(info.Source, feedback);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_CollectFeedback(PlayerRef player, string feedback)
    {
        if (Runner.LocalPlayer == player)
            ConnectionManager.For(Runner)?.GetComponent<PlayerGameplay>()?.ShowFeedback(feedback);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        sceneGeneration++;
        players.Clear();
    }
}
