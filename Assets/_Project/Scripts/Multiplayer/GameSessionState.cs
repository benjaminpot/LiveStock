using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public enum GamePhase { Lobby, LoadingMap, AwaitingStart, Running }

    /// <summary>跨場景保留的房間狀態。只有 server 可以切換階段；client 只回報自己的載入結果。</summary>
    public sealed class GameSessionState : NetworkBehaviour
    {
        static readonly Dictionary<NetworkRunner, GameSessionState> sessions = new();
        readonly HashSet<PlayerRef> ready = new();
        [Networked] public GamePhase Phase { get; private set; }
        [Networked] public int LoadVersion { get; private set; }
        [Networked] public int ReadyPlayers { get; private set; }
        [Networked] public int PlayerCount { get; private set; }
        [Networked] public NetworkBool ServerMapReady { get; private set; }
        public bool AllReady => ServerMapReady && ReadyPlayers == PlayerCount;
        int reportedVersion = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => sessions.Clear();
        public static GameSessionState For(NetworkRunner runner) => runner != null && sessions.TryGetValue(runner, out var state) && state != null && state.Object != null && state.Object.IsValid ? state : null;

        public override void Spawned()
        {
            sessions[Runner] = this;
            transform.SetParent(null, true);
            Runner.SceneManager.MakeDontDestroyOnLoad(gameObject);
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => sessions.Remove(runner);

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            ready.RemoveWhere(player => !Runner.ActivePlayers.Contains(player));
            PlayerCount = Runner.ActivePlayers.Count();
            ReadyPlayers = ready.Count;
            if (Phase == GamePhase.LoadingMap && AllReady) Phase = GamePhase.AwaitingStart;
        }

        public bool BeginMapLoad()
        {
            if (!HasStateAuthority || Phase != GamePhase.Lobby) return false;
            ready.Clear(); ReadyPlayers = 0; ServerMapReady = false;
            LoadVersion++;
            Phase = GamePhase.LoadingMap;
            // 準備地圖後鎖定參與名單，避免載入過程中新增玩家造成等待條件改變。
            Runner.SessionInfo.IsOpen = false;
            return true;
        }

        public void ReportMapLoaded()
        {
            if (Phase != GamePhase.LoadingMap && Phase != GamePhase.AwaitingStart) return;
            if (HasStateAuthority) { ServerMapReady = true; return; }
            if (reportedVersion == LoadVersion) return;
            reportedVersion = LoadVersion;
            RPC_MapReady(LoadVersion);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_MapReady(int version, RpcInfo info = default)
        {
            if (Phase != GamePhase.LoadingMap || version != LoadVersion || !Runner.ActivePlayers.Contains(info.Source)) return;
            ready.Add(info.Source);
        }

        public bool BeginGame()
        {
            if (!HasStateAuthority || Phase != GamePhase.AwaitingStart || !AllReady) return false;
            Phase = GamePhase.Running;
            return true;
        }
    }
}
