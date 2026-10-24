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
        const string DefaultNetworkScenePath = "Assets/_Project/Scenes/_ALPHA_DEMO/MainGame.unity";

        sealed class LocalRoleState
        {
            public GameObject Role;
            public LiveStock.MotionRecording.VRRecordingTargetPlayer Targets;
            public bool IsVR;
        }

        public NetworkObject PlayerPrefab;
        public bool UseVR;
        public string RoomName = "LIVESTOCK-DEV";
        [Tooltip("多人遊戲場景的完整 Assets/.../*.unity 路徑；必須啟用於 Build Settings。")]
        public string NetworkScenePath = DefaultNetworkScenePath;
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
        string resolvedNetworkScenePath;
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

        public static void RefreshRunnerVisibility() => instance?.ApplyActiveRunner();

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }

        void Start()
        {
#if UNITY_SERVER
            StartDedicatedServer();
#endif
        }

        void Update()
        {
#if UNITY_EDITOR
            HandleEditorShortcuts();
#endif
            if (ServerRunning) SpawnPlayers(ServerRunner);
            foreach (var client in clients)
                if (client != null && client.IsRunning && client.IsServer) SpawnPlayers(client);
            RefreshMenuVRRole();
        }

        public void ToggleRole()
        {
            if (!MenuFocused || Busy) return;
            if (!UseVR && HasLocalVRClient)
            {
                Status = "This process already has a VR player. Use PC for this peer (one headset per process).";
                return;
            }
            UseVR = !UseVR;
            RefreshMenuVRRole();
            ApplyActiveRunner();
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
            if (Busy || ServerRunning || !HasFusionAppId()) return;
            Busy = true;
            Status = $"Starting dedicated server: {RoomName}...";
            try
            {
                ServerRunner = await StartRunner(GameMode.Server, true, false);
                if (ServerRunning) Status = $"Dedicated server ready: {RoomName}. This player is still at Menu.";
                ApplyActiveRunner();
            }
            catch (Exception ex) { Status = ex.Message; Debug.LogException(ex); }
            finally { Busy = false; }
        }

        public async void StartSession(bool offline = false)
        {
            if (Busy || !MenuFocused || (!offline && !HasFusionAppId())) return;
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
                    Status = offline ? "Offline player ready." :
                        $"Player {activePeerIndex + 1} connected as {(UseVR ? "VR Sheep" : "PC Observer")}. F1 opens a fresh player Menu; F2 switches.";
                }
            }
            catch (Exception ex) { Status = ex.Message; Debug.LogException(ex); }
            finally { Busy = false; }
        }

        bool HasFusionAppId()
        {
            if (!string.IsNullOrWhiteSpace(Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion)) return true;
            Status = "Set App Id Fusion in Fusion > Realtime Settings first.";
            return false;
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
                SpawnPlayers(r);
                GDC.Metaverse.NetworkSpawner.SpawnFor(r);
                RandomNPCSpawner.SpawnFor(r);
                ApplyActiveRunner();
            });
            events.OnSceneLoadStart.AddListener(r =>
            {
                sceneReady.Remove(r);
                ClearLocalRole(r);
                GDC.Metaverse.NetworkSpawner.DespawnFor(r);
                RandomNPCSpawner.DespawnFor(r);
                if (!r.IsServer) return;
                foreach (var obj in players.Values) if (obj != null) r.Despawn(obj);
                players.Clear();
            });
            events.OnShutdown.AddListener((r, reason) => OnRunnerShutdown(r, reason));
            runner.AddCallbacks(events);

            var networkScenePath = ResolveNetworkScenePath();
            int networkSceneIndex = SceneUtility.GetBuildIndexByScenePath(networkScenePath);
            if (networkSceneIndex < 0)
            {
                if (dedicatedServer) ServerRunner = null;
                else clients.Remove(runner);
                runnerUsesVR.Remove(runner);
                Debug.LogError(
                    $"{nameof(SessionLauncher)}: network scene is not enabled in Build Settings: {networkScenePath}",
                    this);
                Status = $"Network scene is not enabled in Build Settings: {networkScenePath}";
                Destroy(go);
                return null;
            }

            var scene = new NetworkSceneInfo();
            // Fusion Multi-Peer 要求每個 Runner（包含 Client）都指定自己的場景。
            // 直接使用 Build Settings index，避免錯誤/停用的 path 找不到時落入 Addressables 分支。
            scene.AddSceneRef(SceneRef.FromIndex(networkSceneIndex), LoadSceneMode.Single);
            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = RoomName.Trim(),
                PlayerCount = 4,
                SceneManager = go.AddComponent<NetworkSceneManagerDefault>(),
                Scene = scene,
                EnableClientSessionCreation = mode != GameMode.Client,
                IsOpen = true,
                IsVisible = true
            };
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

        string ResolveNetworkScenePath()
        {
            // Multi-Peer 載入後 Active Scene 會變成沒有 asset path 的 Runner 場景。
            // 第一個 Runner 決定路徑後必須快取，後續 Client 才會載入同一個場景。
            if (!string.IsNullOrWhiteSpace(resolvedNetworkScenePath))
                return resolvedNetworkScenePath;

            if (!string.IsNullOrWhiteSpace(NetworkScenePath))
                return resolvedNetworkScenePath = NetworkScenePath.Trim();

            var activeScenePath = SceneManager.GetActiveScene().path;
            var activeSceneName = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrWhiteSpace(activeScenePath) && activeSceneName != "Menu" && activeSceneName != "Lobby")
                return resolvedNetworkScenePath = activeScenePath;

            Debug.LogWarning(
                $"{nameof(SessionLauncher)}: no gameplay scene was configured; using {DefaultNetworkScenePath}.",
                this);
            return resolvedNetworkScenePath = DefaultNetworkScenePath;
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

        MoveInput ReadPose(NetworkRunner runner)
        {
            bool isVR = runnerUsesVR.TryGetValue(runner, out var value) && value;
            var data = new MoveInput { IsVR = isVR, RootRotation = Quaternion.identity, HeadRotation = Quaternion.identity, LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity };
            if (!localRoles.TryGetValue(runner, out var state) || state.Role == null) return data;
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
            bool shouldExist = MenuFocused && UseVR && !HasLocalVRClient && PlayerPrefab != null;
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
            else if (!shouldExist && menuVRRole != null)
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
            }
            ApplyActiveRunner();
        }

        void SpawnPlayers(NetworkRunner runner)
        {
            if (!runner.IsServer || runner.IsSceneManagerBusy || !sceneReady.Contains(runner)) return;
            foreach (var player in runner.ActivePlayers)
            {
                if (players.ContainsKey(player)) continue;
                var obj = runner.Spawn(PlayerPrefab, new Vector3((player.PlayerId % 4) * 3 - 4.5f, 0, 0), Quaternion.identity, player);
                players[player] = obj;
                runner.SetPlayerObject(player, obj);
            }
        }

        public void StartFarm()
        {
            var authority = ServerRunning ? ServerRunner : clients.FirstOrDefault(r => r != null && r.IsRunning && r.IsServer);
            if (authority == null || authority.IsSceneManagerBusy)
                return;

            const string farmScenePath = "Assets/_Project/Scenes/MainGame.unity";
            int farmSceneIndex = SceneUtility.GetBuildIndexByScenePath(farmScenePath);
            if (farmSceneIndex < 0)
            {
                Status = $"Farm scene is not enabled in Build Settings: {farmScenePath}";
                Debug.LogError(Status, this);
                return;
            }

            authority.LoadScene(SceneRef.FromIndex(farmSceneIndex));
        }

        public async void Leave()
        {
            if (Busy) return;
            Busy = true;
            foreach (var client in clients.ToArray()) if (client != null && client.IsRunning) await client.Shutdown();
            if (ServerRunning) await ServerRunner.Shutdown();
            ClearLocalRoles();
            peerAtMenu = true;
            activePeerIndex = 0;
            Busy = false;
            Status = "Menu - start a server or join an existing dedicated server";
            if (!IsMultiPeer && SceneManager.GetActiveScene().name != "Menu") SceneManager.LoadScene("Menu");
        }

        void ClearLocalRole(NetworkRunner runner)
        {
            if (!localRoles.Remove(runner, out var state)) return;
            if (state.Role != null) Destroy(state.Role);
        }

        void ClearLocalRoles()
        {
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
