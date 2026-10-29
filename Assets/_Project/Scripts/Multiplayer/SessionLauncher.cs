using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LiveStock.Multiplayer
{
    public class SessionLauncher : MonoBehaviour
    {
        public enum EntryRole { Development, Server, VRClient }
        public EntryRole Role;
        public string AppIdFusion;
        public string Region = "asia";
        public string AppVersion = "livestock-alpha";
        [Range(1, 16)] public int MaxPlayers = 4;
        public NetworkObject SessionStatePrefab;
        string entryScenePath;
        readonly HashSet<NetworkRunner> gameplayActivated = new();
        public bool IsServerEntry => Role == EntryRole.Server;
        public bool IsVRClientEntry => Role == EntryRole.VRClient;
        public GameSessionState SessionState => GameSessionState.For(ServerRunning ? ServerRunner : Runner);
        public bool GameStarted => SessionState != null && SessionState.Phase == GamePhase.Running;
        public bool MapLoaded => IsGameplayScene(ServerRunning ? ServerRunner : Runner);
        public bool CanEnterMainGame => ServerRunning && !Busy && !ServerRunner.IsSceneManagerBusy && SessionState != null && SessionState.Phase == GamePhase.Lobby;
        public int LobbyPlayerCount => ServerRunning ? ServerRunner.ActivePlayers.Count() : Runner != null && Runner.IsRunning ? Runner.ActivePlayers.Count() : 0;
        public bool CanStartGame => ServerRunning && !Busy && MapLoaded && SessionState != null && SessionState.Phase == GamePhase.AwaitingStart && SessionState.AllReady;

        bool IsGameplayScene(NetworkRunner runner)
        {
            return runner != null && runner.IsRunning && runner.SceneManager is RoleNetworkSceneManager manager && manager.MapLoaded && sceneReady.Contains(runner);
        }

        sealed class LocalRoleState
        {
            public GameObject Role;
            public LiveStock.MotionRecording.VRRecordingTargetPlayer Targets;
            public bool IsVR;
            public int TeleportVersion;
        }

        public NetworkObject PlayerPrefab;
        public bool UseVR;
        public string RoomName = "LIVESTOCK-DEV";
        public string Status = "Menu - start a server or join an existing dedicated server";

        readonly Dictionary<NetworkRunner, LocalRoleState> localRoles = new();
        readonly Dictionary<NetworkRunner, bool> runnerUsesVR = new();
        readonly HashSet<NetworkRunner> sceneReady = new();
        readonly Dictionary<Camera, bool> previewCameras = new();
        readonly Dictionary<AudioListener, bool> previewListeners = new();
        readonly List<NetworkRunner> clients = new();
        readonly Dictionary<PlayerRef, NetworkObject> players = new();

        int activePeerIndex;
        bool peerAtMenu = true;
        GameObject menuVRRole;
        static SessionLauncher instance;

        public NetworkRunner ServerRunner { get; private set; }
        public NetworkRunner Runner => activePeerIndex >= 0 && activePeerIndex < clients.Count ? clients[activePeerIndex] : null;
        public bool Busy { get; private set; }
        public bool ServerRunning => ServerRunner != null && ServerRunner.IsRunning;
        public bool PeerAtMenu => peerAtMenu;
        public bool MenuFocused => peerAtMenu && activePeerIndex == clients.Count;
        public int ConnectedPeerCount => clients.Count;
        public int PeerCount => clients.Count + (peerAtMenu ? 1 : 0);
        public int ActivePeerIndex => activePeerIndex;
        public bool IsMultiPeer => NetworkProjectConfig.Global.PeerMode == NetworkProjectConfig.PeerModes.Multiple;
        public bool CanAddEditorPeer => IsMultiPeer && !Busy && !peerAtMenu && clients.Count < 4;
        public bool HasLocalVRClient => runnerUsesVR.Any(pair => pair.Key != null && pair.Key.IsRunning && pair.Value);
        public bool CanSelectVR => UseVR || !HasLocalVRClient;
        public OVRCameraRig MenuVRRig => menuVRRole != null ? menuVRRole.GetComponentInChildren<OVRCameraRig>(true) : null;
        public OVRCameraRig InteractionVRRig
        {
            get
            {
                if (MenuFocused && UseVR)
                    return MenuVRRig;

                var runner = Runner;
                if (runner == null || !runnerUsesVR.TryGetValue(runner, out bool usesVR) || !usesVR)
                    return null;

                if (localRoles.TryGetValue(runner, out var state) && state.Role != null)
                    return state.Role.GetComponentInChildren<OVRCameraRig>(true);

                // Keep the menu rig interactive while the network avatar is taking ownership of it.
                return MenuVRRig;
            }
        }

        public static void RefreshRunnerVisibility() => instance?.ApplyActiveRunner();

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            entryScenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrWhiteSpace(AppIdFusion)) AppIdFusion = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion;
            if (IsVRClientEntry) UseVR = true;
            Status = IsServerEntry ? "Server offline - configure Fusion, then Start Server." : "Offline VR menu - connect to the server lobby when ready.";
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }

        void Update()
        {
#if UNITY_EDITOR
            HandleEditorShortcuts();
#endif
            if (ServerRunning) UpdateSession(ServerRunner);
            foreach (var client in clients.ToArray())
                if (client != null && client.IsRunning) UpdateSession(client);
            RefreshMenuVRRole();
        }

        void UpdateSession(NetworkRunner runner)
        {
            if (runner.IsSceneManagerBusy || !sceneReady.Contains(runner)) return;
            if (runner.IsServer && GameSessionState.For(runner) == null && SessionStatePrefab != null)
                runner.Spawn(SessionStatePrefab);
            var state = GameSessionState.For(runner);
            if (state == null || !IsGameplayScene(runner)) return;
            state.ReportMapLoaded();
            if (state.Phase != GamePhase.Running || !runner.IsServer) return;
            // 遊戲啟動集中在此，只執行一次；地圖載入與 Start/Awake 都不生成 NPC。
            if (gameplayActivated.Add(runner))
            {
                GDC.Metaverse.NetworkSpawner.SpawnFor(runner);
                RandomNPCSpawner.SpawnFor(runner);
            }
            SpawnPlayers(runner);
        }

        public void ToggleRole()
        {
            if (Role != EntryRole.Development || !MenuFocused || Busy) return;
            if (!UseVR && HasLocalVRClient)
            {
                Status = "This process already has a VR player. Use PC for this peer (one headset per process).";
                return;
            }
            UseVR = !UseVR;
            RefreshMenuVRRole();
            ApplyActiveRunner();
        }

        public void ConnectAsVRClient()
        {
            if (IsServerEntry) { Status = "Use the VRclient / Menu scene to connect a VR client."; return; }
            if (!Application.isPlaying)
            {
                Status = "Enter Play Mode before connecting a VR client.";
                return;
            }
            if (Busy || !MenuFocused)
                return;
            if (!UseVR && HasLocalVRClient)
            {
                Status = "This process already has a VR player. Only one headset is supported per process.";
                return;
            }

            UseVR = true;
            RefreshMenuVRRole();
            ApplyActiveRunner();
            StartSession();
        }

#if UNITY_EDITOR
        void HandleEditorShortcuts()
        {
            if (!IsMultiPeer) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) AddEditorPeer();
            else if (keyboard.f2Key.wasPressedThisFrame) SwitchActiveRunner();
        }
#endif

        public async void StartDedicatedServer()
        {
            if (IsVRClientEntry || Busy || ServerRunning || !HasFusionAppId()) return;
            Busy = true;
            Status = $"Starting dedicated server: {RoomName}...";
            try
            {
                ServerRunner = await StartRunner(GameMode.Server, true, false);
                if (ServerRunning) Status = $"Lobby online: {RoomName}. Check players, then Enter MainGame.";
                ApplyActiveRunner();
            }
            catch (Exception ex) { Status = ex.Message; Debug.LogException(ex); }
            finally { Busy = false; }
        }

        public async void StartSession(bool offline = false)
        {
            if (IsServerEntry || Busy || !MenuFocused || (!offline && !HasFusionAppId())) return;
            Busy = true;
            int newIndex = clients.Count;
            Status = offline ? "Starting offline player..." : $"Player {newIndex + 1} joining {RoomName} as {(UseVR ? "VR" : "PC")}...";
            try
            {
                var runner = await StartRunner(offline ? GameMode.Single : GameMode.Client, false, UseVR);
                if (runner != null)
                {
                    peerAtMenu = false;
                    activePeerIndex = clients.IndexOf(runner);
                    ApplyActiveRunner();
                    Status = offline ? "Offline player ready." : "Connected to lobby - waiting for server to start the game.";
                }
            }
            catch (Exception ex) { Status = ex.Message; Debug.LogException(ex); }
            finally { Busy = false; }
        }

        bool HasFusionAppId()
        {
            if (!Guid.TryParse(AppIdFusion?.Trim(), out _)) { Status = "Enter a valid Fusion App ID."; return false; }
            if (string.IsNullOrWhiteSpace(RoomName)) { Status = "Enter a room name."; return false; }
            if (string.IsNullOrWhiteSpace(Region)) { Status = "Enter the same fixed region on server and client (for example asia)."; return false; }
            return true;
        }

        async System.Threading.Tasks.Task<NetworkRunner> StartRunner(GameMode mode, bool dedicatedServer, bool useVR)
        {
            var go = new GameObject(dedicatedServer ? "LiveStock Dedicated Server" : $"LiveStock Player Peer {clients.Count + 1}");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<NetworkRunner>();
            go.AddComponent<RunnerEnableVisibility>();
            runner.ProvideInput = false;
            if (dedicatedServer) ServerRunner = runner;
            else { clients.Add(runner); runnerUsesVR[runner] = useVR; }

            var events = go.AddComponent<NetworkEvents>();
            events.OnInput ??= new NetworkEvents.InputEvent();
            events.PlayerJoined ??= new NetworkEvents.PlayerEvent();
            events.PlayerLeft ??= new NetworkEvents.PlayerEvent();
            events.OnSceneLoadDone ??= new NetworkEvents.RunnerEvent();
            events.OnSceneLoadStart ??= new NetworkEvents.RunnerEvent();
            events.OnShutdown ??= new NetworkEvents.ShutdownEvent();
            if (!dedicatedServer) events.OnInput.AddListener((r, input) => input.Set(ReadPose(r)));
            events.PlayerJoined.AddListener((r, p) => SpawnPlayers(r));
            events.PlayerLeft.AddListener((r, p) => { if (r.IsServer && players.Remove(p, out var obj) && obj != null) r.Despawn(obj); });
            events.OnSceneLoadDone.AddListener(r =>
            {
                sceneReady.Add(r);
                ConfigureServerOnly(r);
                DisableScenePreviewCameras(r);
                if (IsGameplayScene(r))
                {
                    Status = "Map loaded. Waiting for all players and server Start Game.";
                }
                ApplyActiveRunner();
            });
            events.OnSceneLoadStart.AddListener(r =>
            {
                sceneReady.Remove(r);
                gameplayActivated.Remove(r);
                ClearLocalRole(r);
                GDC.Metaverse.NetworkSpawner.DespawnFor(r);
                RandomNPCSpawner.DespawnFor(r);
                if (!r.IsServer) return;
                foreach (var obj in players.Values) if (obj != null) r.Despawn(obj);
                players.Clear();
            });
            events.OnShutdown.AddListener((r, reason) => OnRunnerShutdown(r, reason));
            runner.AddCallbacks(events);

            string lobbyPath = RoleNetworkSceneManager.SceneFolder + (dedicatedServer ? "Lobby_server.unity" : "Lobby_VRClient.unity");
            if (SceneUtility.GetBuildIndexByScenePath(lobbyPath) < 0 || SessionStatePrefab == null)
            {
                if (dedicatedServer) ServerRunner = null;
                else clients.Remove(runner);
                runnerUsesVR.Remove(runner);
                Debug.LogError(
                    $"{nameof(SessionLauncher)}: missing role lobby or SessionStatePrefab: {lobbyPath}",
                    this);
                Status = $"Missing role lobby or GameSessionState prefab: {lobbyPath}";
                Destroy(go);
                return null;
            }

            var scene = new NetworkSceneInfo();
            // 兩份 Build 使用同一個邏輯 SceneRef，實際場景由角色管理器解析。
            scene.AddSceneRef(RoleNetworkSceneManager.LobbyRef, LoadSceneMode.Single);
            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = RoomName.Trim(),
                PlayerCount = Mathf.Clamp(MaxPlayers, 1, 16),
                SceneManager = go.AddComponent<RoleNetworkSceneManager>(),
                Scene = scene,
                EnableClientSessionCreation = mode != GameMode.Client,
                IsOpen = true,
                IsVisible = true
            };
            var settings = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.GetCopy();
            settings.AppIdFusion = AppIdFusion.Trim();
            settings.FixedRegion = Region.Trim().ToLowerInvariant();
            settings.AppVersion = AppVersion.Trim();
            args.CustomPhotonAppSettings = settings;
            var result = await runner.StartGame(args);
            if (result.Ok)
            {
                // Dedicated Server owns CCTV rendering and NPC simulation in this process.
                // Do not hide it here after OnSceneLoadDone has activated ServerOnly;
                // the following visibility pass keeps the authoritative Runner visible.
                ApplyActiveRunner();
                return runner;
            }

            if (dedicatedServer) ServerRunner = null;
            else clients.Remove(runner);
            runnerUsesVR.Remove(runner);
            sceneReady.Remove(runner);
            ClearLocalRole(runner);
            Destroy(go);
            Status = "Connection failed: " + result.ShutdownReason;
            return null;
        }

        static void ConfigureServerOnly(NetworkRunner runner)
        {
            if (runner.SceneManager is not NetworkSceneManagerDefault manager)
                return;

            foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                foreach (var serverOnly in root.GetComponentsInChildren<ServerOnly>(true))
                    serverOnly.ActivateFor(runner);
        }

#if UNITY_EDITOR
        public void AddEditorPeer()
        {
            if (!IsMultiPeer)
            {
                Status = "F1 requires Fusion Peer Mode = Multiple.";
                return;
            }
            if (Busy)
            {
                Status = "Wait for the current connection operation to finish before adding another player.";
                return;
            }
            if (peerAtMenu)
            {
                Status = "This menu is already the next player. Join first, then press F1 to open another player menu.";
                return;
            }
            if (clients.Count >= 4)
            {
                Status = "Player limit reached (4/4).";
                return;
            }
            peerAtMenu = true;
            activePeerIndex = clients.Count;
            UseVR = false;
            Status = $"Player {clients.Count + 1} is at Menu. Select PC/VR, then Join.";
            ApplyActiveRunner();
        }

        public void SwitchActiveRunner()
        {
            clients.RemoveAll(r => r == null || !r.IsRunning);
            int slots = clients.Count + (peerAtMenu ? 1 : 0);
            if (slots < 2) return;
            activePeerIndex = (activePeerIndex + 1) % slots;
            ApplyActiveRunner();
            Status = activePeerIndex == clients.Count
                ? $"Player {clients.Count + 1} Menu selected. Choose a role and Join."
                : $"Active player {activePeerIndex + 1}/{clients.Count}. F1 opens a new Menu; F2 switches.";
        }
#endif

        public void CreateLocalRole(NetworkRunner runner, Vector3 position)
        {
            ClearLocalRole(runner);
            SuppressPreviewCameras();
            var state = new LocalRoleState { IsVR = runnerUsesVR.TryGetValue(runner, out var isVR) && isVR };
            if (state.IsVR)
            {
                if (menuVRRole != null)
                {
                    state.Role = menuVRRole;
                    menuVRRole = null;
                    state.Role.transform.SetPositionAndRotation(position, Quaternion.identity);
                }
                else state.Role = Instantiate(PlayerPrefab.GetComponent<NetworkPlayer>().LocalVRPrefab, position, Quaternion.identity);
                state.Targets = Array.Find(state.Role.GetComponentsInChildren<LiveStock.MotionRecording.VRRecordingTargetPlayer>(true),
                    t => t.headTarget != null && t.leftHandTarget != null && t.rightHandTarget != null);
                foreach (var playback in state.Role.GetComponentsInChildren<LiveStock.MotionRecording.VRRecordingTargetPlayer>(true))
                { playback.playOnStart = false; playback.enabled = false; }
                if (state.Targets != null) state.Role.AddComponent<VRLiveTargets>().Initialize(state.Targets);
            }
            else
            {
                state.Role = new GameObject($"Local PC Observer ({runner.name})");
                state.Role.transform.SetPositionAndRotation(new Vector3(0, 8, -12), Quaternion.Euler(20, 0, 0));
                var camera = state.Role.AddComponent<Camera>(); camera.farClipPlane = 2000; camera.depth = 100;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                state.Role.AddComponent<AudioListener>();
                var observer = state.Role.AddComponent<ObserverCamera>();
                observer.Initialize(runner);
            }
            localRoles[runner] = state;
            ApplyActiveRunner();
        }

        /// <summary>
        /// Applies a State Authority teleport to the input owner's local tracking rig.
        /// The version is echoed in MoveInput so the server never accepts a stale
        /// pre-teleport world pose on the following network tick.
        /// </summary>
        public void ApplyServerTeleport(NetworkRunner runner, Vector3 position, Quaternion rotation, int version)
        {
            if (!localRoles.TryGetValue(runner, out var state) || state.Role == null ||
                version <= state.TeleportVersion)
                return;

            state.Role.transform.SetPositionAndRotation(position, rotation);
            state.TeleportVersion = version;
        }

        MoveInput ReadPose(NetworkRunner runner)
        {
            bool isVR = runnerUsesVR.TryGetValue(runner, out var value) && value;
            var data = new MoveInput { IsVR = isVR, RootRotation = Quaternion.identity, HeadRotation = Quaternion.identity, LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity };
            if (!localRoles.TryGetValue(runner, out var state) || state.Role == null) return data;
            data.TeleportVersion = state.TeleportVersion;
            data.Root = state.Role.transform.position; data.RootRotation = state.Role.transform.rotation;
            if (state.Targets != null)
            {
                data.Head = state.Targets.headTarget.position; data.HeadRotation = state.Targets.headTarget.rotation;
                data.Left = state.Targets.leftHandTarget.position; data.LeftRotation = state.Targets.leftHandTarget.rotation;
                data.Right = state.Targets.rightHandTarget.position; data.RightRotation = state.Targets.rightHandTarget.rotation;
            }
            return data;
        }

        void ApplyActiveRunner()
        {
            clients.RemoveAll(r => r == null);
            bool menuFocused = peerAtMenu && activePeerIndex == clients.Count;
            if (!menuFocused && clients.Count > 0) activePeerIndex = Mathf.Clamp(activePeerIndex, 0, clients.Count - 1);
            if (ServerRunning)
            {
                ServerRunner.ProvideInput = false;
                // Keep the authoritative scene ticking so ServerOnly.Start can
                // resolve its runner and start Render Streaming. Hiding this
                // scene until registration creates a visibility/startup cycle.
                if (IsMultiPeer) ServerRunner.SetVisible(true);
            }
            bool serverOwnsRendering = ServerRunning;
            for (int i = 0; i < clients.Count; i++)
            {
                var runner = clients[i];
                bool active = !menuFocused && i == activePeerIndex;
                bool isVR = runnerUsesVR.TryGetValue(runner, out var usesVR) && usesVR;
                bool keepAlive = active || isVR;
                runner.ProvideInput = keepAlive;
                // A process that is rendering CCTV must expose exactly the
                // authoritative server scene, never a client replica.
                if (runner.IsRunning && IsMultiPeer) runner.SetVisible(!serverOwnsRendering && keepAlive);
                if (localRoles.TryGetValue(runner, out var state) && state.Role != null)
                {
                    state.Role.SetActive(keepAlive);
                    foreach (var listener in state.Role.GetComponentsInChildren<AudioListener>(true))
                        listener.enabled = keepAlive && (isVR || !HasLocalVRClient);
                }
            }
            SetPreviewVisible(menuFocused);
        }

        void RefreshMenuVRRole()
        {
            bool waitingForVRRole = runnerUsesVR.Any(pair =>
                pair.Key != null && pair.Key.IsRunning && pair.Value && !localRoles.ContainsKey(pair.Key));
            bool shouldExist = ((MenuFocused && UseVR && !HasLocalVRClient) || waitingForVRRole) && PlayerPrefab != null;
            if (shouldExist && menuVRRole == null)
            {
                var localVRPrefab = PlayerPrefab.GetComponent<NetworkPlayer>()?.LocalVRPrefab;
                if (localVRPrefab == null) return;
                SuppressPreviewCameras();
                menuVRRole = Instantiate(localVRPrefab, Vector3.zero, Quaternion.identity);
                menuVRRole.name = "VR Menu Rig";
                DontDestroyOnLoad(menuVRRole);
                foreach (var playback in menuVRRole.GetComponentsInChildren<LiveStock.MotionRecording.VRRecordingTargetPlayer>(true))
                { playback.playOnStart = false; playback.enabled = false; }
            }
            else if (!shouldExist && !waitingForVRRole && menuVRRole != null)
            {
                Destroy(menuVRRole);
                menuVRRole = null;
            }
        }

        void FixedUpdate()
        {
            if (!IsMultiPeer) return;
            if (ServerRunning) SimulatePhysics(ServerRunner);
            foreach (var client in clients) SimulatePhysics(client);
        }

        static void SimulatePhysics(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning) return;
            var physics = runner.GetPhysicsScene();
            if (physics.IsValid()) physics.Simulate(Time.fixedDeltaTime);
        }

        void OnRunnerShutdown(NetworkRunner runner, ShutdownReason reason)
        {
            bool wasServer = runner == ServerRunner;
            ClearLocalRole(runner);
            clients.Remove(runner);
            runnerUsesVR.Remove(runner);
            sceneReady.Remove(runner);
            gameplayActivated.Remove(runner);
            Destroy(runner.gameObject);
            if (wasServer)
            {
                ServerRunner = null;
                players.Clear();
                Status = "Dedicated server stopped: " + reason;
            }
            else
            {
                peerAtMenu = true;
                activePeerIndex = clients.Count;
                Status = "Player returned to Menu: " + reason;
                if (IsVRClientEntry && !Busy && !IsMultiPeer && Application.isPlaying)
                    SceneManager.LoadScene(RoleNetworkSceneManager.SceneFolder + "Menu_VRClient.unity");
            }
            ApplyActiveRunner();
        }

        void SpawnPlayers(NetworkRunner runner)
        {
            if (!runner.IsServer || !IsGameplayScene(runner) || runner.IsSceneManagerBusy || GameSessionState.For(runner)?.Phase != GamePhase.Running) return;
            var spawnPoints = FindPlayerSpawnPoints(runner);
            int playerIndex = 0;
            foreach (var player in runner.ActivePlayers.OrderBy(player => player.PlayerId))
            {
                if (players.ContainsKey(player)) { playerIndex++; continue; }
                Vector3 position = spawnPoints.Count > 0
                    ? spawnPoints[playerIndex % spawnPoints.Count].position
                    : new Vector3((player.PlayerId % 4) * 3 - 4.5f, 0, 0);
                Quaternion rotation = spawnPoints.Count > 0
                    ? spawnPoints[playerIndex % spawnPoints.Count].rotation
                    : Quaternion.identity;
                var obj = runner.Spawn(PlayerPrefab, position, rotation, player);
                players[player] = obj;
                runner.SetPlayerObject(player, obj);
                obj.GetComponent<NetworkPlayer>()?.ServerTeleport(position, rotation);
                playerIndex++;
            }
        }

        static List<Transform> FindPlayerSpawnPoints(NetworkRunner runner)
        {
            var result = new List<Transform>();
            if (runner.SceneManager is not NetworkSceneManagerDefault manager)
                return result;

            foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.name.StartsWith("Anchor_PlayerSpawn_", StringComparison.Ordinal))
                        result.Add(candidate);

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        public void StartGame()
        {
            if (!CanStartGame) return;
            if (!SessionState.BeginGame()) return;
            // The dedicated server chooses the scene-authored spawn anchors and
            // creates every player object with State Authority before gameplay.
            SpawnPlayers(ServerRunner);
            Status = "Game running.";
        }

        public void EnterMainGame()
        {
            if (!CanEnterMainGame) return;
            var manager = (RoleNetworkSceneManager)ServerRunner.SceneManager;
            if (SceneUtility.GetBuildIndexByScenePath(manager.LocalPath(RoleNetworkSceneManager.MainGameRef)) < 0)
            { Status = "MainGame_server is missing from this build."; return; }
            if (!SessionState.BeginMapLoad()) return;
            Status = "Loading maps. Game mechanisms are paused.";
            ServerRunner.LoadScene(RoleNetworkSceneManager.MainGameRef, LoadSceneMode.Single);
        }

        public async void Leave()
        {
            if (Busy) return;
            Busy = true;
            try
            {
                foreach (var client in clients.ToArray()) if (client != null && client.IsRunning) await client.Shutdown();
                if (ServerRunning) await ServerRunner.Shutdown();
                ClearLocalRoles();
                peerAtMenu = true;
                activePeerIndex = 0;
                Status = IsServerEntry ? "Server stopped. Ready to create a lobby." : "Offline VR menu - disconnected.";
                if (!IsMultiPeer && !string.IsNullOrEmpty(entryScenePath)) SceneManager.LoadScene(entryScenePath);
            }
            finally { Busy = false; }
        }

        void ClearLocalRole(NetworkRunner runner)
        {
            if (!localRoles.Remove(runner, out var state)) return;
            if (state.IsVR) SessionPanel.DetachFromRig();
            if (state.Role != null) Destroy(state.Role);
        }

        void ClearLocalRoles()
        {
            SessionPanel.DetachFromRig();
            foreach (var state in localRoles.Values) if (state.Role != null) Destroy(state.Role);
            localRoles.Clear();
            foreach (var item in previewCameras) if (item.Key != null) item.Key.enabled = item.Value;
            foreach (var item in previewListeners) if (item.Key != null) item.Key.enabled = item.Value;
            previewCameras.Clear();
            previewListeners.Clear();
            if (menuVRRole != null) Destroy(menuVRRole);
            menuVRRole = null;
        }

        void SuppressPreviewCameras()
        {
            if (previewCameras.Count > 0 || previewListeners.Count > 0) return;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { previewCameras[camera] = camera.enabled; camera.enabled = false; }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { previewListeners[listener] = listener.enabled; listener.enabled = false; }
        }

        void SetPreviewVisible(bool visible)
        {
            foreach (var item in previewCameras) if (item.Key != null) item.Key.enabled = visible && item.Value;
            foreach (var item in previewListeners) if (item.Key != null) item.Key.enabled = visible && item.Value;
        }

        static void DisableScenePreviewCameras(NetworkRunner runner)
        {
            if (runner.SceneManager is not NetworkSceneManagerDefault manager) return;
            // server 的 Lobby 仍需要桌面相機，讓房間面板與等待空間正常顯示。
            if (runner.IsServer && manager is RoleNetworkSceneManager roleManager && !roleManager.MapLoaded)
            {
                foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                    foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                        camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                return;
            }

            var serverPreviewCameras = new HashSet<Camera>();
            if (runner.IsServer)
                foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
                    foreach (var switcher in root.GetComponentsInChildren<CCTVViewSwitcher>(true))
                        switcher.CollectPreviewCameras(serverPreviewCameras);

            foreach (var root in manager.MainRunnerScene.GetRootGameObjects())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    if (!serverPreviewCameras.Contains(camera))
                        camera.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                    listener.enabled = false;
            }
        }
    }
}
