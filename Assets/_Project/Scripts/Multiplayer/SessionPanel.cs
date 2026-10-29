using TMPro;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using Oculus.Interaction;

namespace LiveStock.Multiplayer
{
    public class SessionPanel : MonoBehaviour
    {
        static SessionPanel instance;
        TMP_Text status;
        Canvas canvas;
        RectTransform panelRect;
        Transform canvasHome;
        EventSystem inputEventSystem;
        InputSystemUIInputModule desktopInput;
        OVRCameraRig configuredRig;
        VRPokeMenu vrMenu;
        UnityEngine.UI.Button server, start, offline, farm, enterMap, leave;
        UnityEngine.UI.Button role;
        TMP_InputField room, appId, region, version;
        GameObject settingsPanel;
#if UNITY_EDITOR
        UnityEngine.UI.Button addPeer, switchPeer;
#endif
        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Start()
        {
            var canvasPrefab = Resources.Load<GameObject>("Multiplayer/MultiplayerUI");
            if (canvasPrefab == null)
            {
                Debug.LogError("MultiplayerUI prefab is missing from Resources/Multiplayer. Run Tools/LiveStock/Create Multiplayer UI Prefab.");
                enabled = false;
                return;
            }

            var canvasObject = Instantiate(canvasPrefab, transform);
            canvasObject.name = "Multiplayer UI";
            canvasObject.transform.SetParent(transform);
            // The VR interface uses the same 3D PokeInteractable structure as the
            // teaching-assistant example. Keep the legacy PointableCanvas inactive.
            foreach (var pointableCanvas in canvasObject.GetComponentsInChildren<PointableCanvas>(true))
                pointableCanvas.gameObject.SetActive(false);
            canvasHome = transform;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var panel = new GameObject("Panel", typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(canvasObject.transform, false);
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0, 1); panelRect.pivot = new Vector2(0, 1); panelRect.anchoredPosition = new Vector2(20, -20); panelRect.sizeDelta = new Vector2(600, 670);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(.04f, .08f, .12f, .94f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.padding = new RectOffset(16, 16, 12, 12); layout.spacing = 6;
            layout.childForceExpandHeight = false;
            status = Label(panel.transform, "LiveStock Multiplayer");
            status.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 160;
            var launcher = FindFirstObjectByType<SessionLauncher>();
            settingsPanel = new GameObject("Fusion Settings", typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
            settingsPanel.transform.SetParent(panel.transform, false);
            settingsPanel.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 264;
            settingsPanel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 4;
            settingsPanel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childForceExpandHeight = false;
            appId = Input(settingsPanel.transform, "Fusion App ID", launcher.AppIdFusion, v => launcher.AppIdFusion = v);
            room = Input(settingsPanel.transform, "Room / Session Name", launcher.RoomName, v => launcher.RoomName = v);
            region = Input(settingsPanel.transform, "Region (same on both devices)", launcher.Region, v => launcher.Region = v);
            version = Input(settingsPanel.transform, "App Version (same on both devices)", launcher.AppVersion, v => launcher.AppVersion = v);
            server = Button(panel.transform, "Start Server", () => FindFirstObjectByType<SessionLauncher>().StartDedicatedServer());
            role = Button(panel.transform, "Role: PC Observer", () => FindFirstObjectByType<SessionLauncher>().ToggleRole());
            start = Button(panel.transform, "Join Dedicated Server", () => FindFirstObjectByType<SessionLauncher>().StartSession());
            offline = Button(panel.transform, "Offline test", () => FindFirstObjectByType<SessionLauncher>().StartSession(true));
            enterMap = Button(panel.transform, "Enter MainGame", () => FindFirstObjectByType<SessionLauncher>().EnterMainGame());
            farm = Button(panel.transform, "Start Game", () => FindFirstObjectByType<SessionLauncher>().StartGame());
            leave = Button(panel.transform, "Leave", () => FindFirstObjectByType<SessionLauncher>().Leave());
#if UNITY_EDITOR
            addPeer = Button(panel.transform, "Open New Player Menu (F1)", () => FindFirstObjectByType<SessionLauncher>().AddEditorPeer());
            switchPeer = Button(panel.transform, "Switch Peer (F2)", () => FindFirstObjectByType<SessionLauncher>().SwitchActiveRunner());
#endif
            EnsureInputModules();
        }
        static TMP_Text Label(Transform parent, string value)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = 20; text.color = Color.white; text.raycastTarget = false; return text;
        }
        static TMP_InputField Input(Transform parent, string title, string value, UnityEngine.Events.UnityAction<string> changed)
        {
            var caption = Label(parent, title);
            caption.fontSize = 16;
            caption.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 20;
            var go = new GameObject(title, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(TMP_InputField), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(.10f, .18f, .24f);
            go.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 38;
            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10, 3); viewportRect.offsetMax = new Vector2(-10, -3);
            var text = Label(viewport.transform, value);
            text.fontSize = 19; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect; input.textComponent = text;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = value ?? ""; input.onValueChanged.AddListener(changed);
            return input;
        }
        static UnityEngine.UI.Button Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(.12f, .3f, .4f);
            var button = go.GetComponent<UnityEngine.UI.Button>(); button.onClick.AddListener(action);
            go.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 36;
            var label = Label(go.transform, text); var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        void Update()
        {
            var launcher = FindFirstObjectByType<SessionLauncher>(); if (launcher == null || status == null) return;
            EnsureInputModules();
            ConfigurePresentation(launcher);
            var anyNetwork = launcher.ServerRunning || launcher.ConnectedPeerCount > 0;
            bool clientEntry = launcher.IsVRClientEntry;
            bool serverEntry = launcher.IsServerEntry;
            panelRect.sizeDelta = new Vector2(serverEntry ? 560 : 600,
                launcher.Role == SessionLauncher.EntryRole.Development ? 670 : anyNetwork ? 300 : 540);
            bool editing = !anyNetwork && !launcher.Busy;
            appId.interactable = room.interactable = region.interactable = version.interactable = editing;
            settingsPanel.SetActive(!anyNetwork);
            server.gameObject.SetActive(!clientEntry && !launcher.ServerRunning && launcher.ConnectedPeerCount == 0);
            server.interactable = !launcher.Busy;
            role.gameObject.SetActive(!clientEntry && !serverEntry && launcher.MenuFocused); role.interactable = !launcher.Busy && launcher.CanSelectVR;
            role.GetComponentInChildren<TMP_Text>().text = launcher.UseVR ? "Role: VR Sheep" : launcher.CanSelectVR ? "Role: PC Observer" : "Role: PC Observer (VR in use)";
            start.GetComponentInChildren<TMP_Text>().text = launcher.UseVR ? "Join as VR Sheep Client" : "Join as PC Observer Client";
            string phaseMessage = launcher.SessionState == null ? launcher.Status : launcher.SessionState.Phase switch
            {
                GamePhase.Lobby => serverEntry ? "Lobby online. Review players, then Enter MainGame." : "Connected to lobby. Waiting for server to enter map.",
                GamePhase.LoadingMap => "Loading maps. Waiting for every player to be ready.",
                GamePhase.AwaitingStart => "Maps ready. Waiting for server to press Start Game.",
                GamePhase.Running => "Game running.",
                _ => launcher.Status
            };
            status.text = (serverEntry ? "LIVESTOCK | SERVER" : "LIVESTOCK | VR CLIENT")
                + "\n" + phaseMessage
                + "\n" + (launcher.SessionState != null ? launcher.SessionState.Phase.ToString() : anyNetwork ? "CONNECTED / SYNCING" : "OFFLINE")
                + " | players: " + launcher.LobbyPlayerCount + "/" + launcher.MaxPlayers
                + "\nRoom: " + launcher.RoomName + " | Region: " + launcher.Region
                + (launcher.SessionState != null && launcher.SessionState.Phase != GamePhase.Lobby
                    ? "\nMap ready: " + launcher.SessionState.ReadyPlayers + "/" + launcher.SessionState.PlayerCount
                      + " | server: " + (launcher.SessionState.ServerMapReady ? "ready" : "loading") : "")
                + (launcher.LobbyPlayerCount > 0 ? "\nPlayers: " + string.Join(", ", (launcher.ServerRunning ? launcher.ServerRunner : launcher.Runner).ActivePlayers.Select(p => p.ToString())) : "");
            start.gameObject.SetActive(!serverEntry && launcher.MenuFocused);
            offline.gameObject.SetActive(!clientEntry && !serverEntry && launcher.MenuFocused && !anyNetwork);
            start.interactable = offline.interactable = !launcher.Busy;
            enterMap.gameObject.SetActive(launcher.ServerRunning && !launcher.MapLoaded);
            enterMap.interactable = launcher.CanEnterMainGame;
            farm.gameObject.SetActive(launcher.ServerRunning && launcher.MapLoaded);
            farm.GetComponentInChildren<TMP_Text>().text = launcher.GameStarted ? "Game Running" : "Start Game";
            farm.interactable = launcher.CanStartGame;
            leave.gameObject.SetActive(anyNetwork);
#if UNITY_EDITOR
            bool multiPeer = !clientEntry && !serverEntry && launcher.IsMultiPeer && anyNetwork;
            addPeer.gameObject.SetActive(multiPeer && !launcher.PeerAtMenu); addPeer.interactable = launcher.CanAddEditorPeer;
            switchPeer.gameObject.SetActive(multiPeer); switchPeer.interactable = !launcher.Busy && launcher.PeerCount > 1;
#endif
        }

        void ConfigurePresentation(SessionLauncher launcher)
        {
            var rig = launcher.InteractionVRRig;
            bool useVR = rig != null;
            if (useVR && configuredRig != rig)
            {
                configuredRig = rig;
                EnsureVRMenu(launcher);
            }
            else if (!useVR && configuredRig != null)
            {
                configuredRig = null;
            }

            canvas.gameObject.SetActive(!useVR);
            if (vrMenu != null) vrMenu.gameObject.SetActive(useVR);
            if (desktopInput != null) desktopInput.enabled = !useVR;
        }

        void LateUpdate()
        {
            if (configuredRig == null || vrMenu == null || !vrMenu.gameObject.activeInHierarchy) return;
            var eye = configuredRig.centerEyeAnchor;
            vrMenu.transform.SetPositionAndRotation(
                eye.TransformPoint(new Vector3(0, -.15f, 1.15f)), eye.rotation);
        }

        void EnsureVRMenu(SessionLauncher launcher)
        {
            if (vrMenu == null)
            {
                vrMenu = FindFirstObjectByType<VRPokeMenu>(FindObjectsInactive.Include);
                if (vrMenu == null)
                {
                    Debug.LogError("The Menu_VRClient scene is missing its VR Poke Menu hierarchy.");
                    return;
                }
                // The buttons are authored directly in Menu_VRClient. Reparent only
                // at runtime so the menu survives the transition to Lobby/MainGame.
                vrMenu.transform.SetParent(transform, true);
            }
            vrMenu.Bind(launcher);
            vrMenu.gameObject.SetActive(true);
        }

        public static void DetachFromRig()
        {
            // 場景切換或離線會銷毀本機角色，先保住跨場景的選單 Canvas。
            if (instance == null || instance.canvas == null) return;
            instance.configuredRig = null;
            if (instance.vrMenu != null) instance.vrMenu.gameObject.SetActive(false);
            instance.canvas.gameObject.SetActive(true);
            instance.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            instance.canvas.worldCamera = null;
            instance.canvas.transform.localScale = Vector3.one;
        }

        void EnsureInputModules()
        {
            // Desktop uGUI keeps one persistent EventSystem. The VR menu uses Meta
            // PokeInteractables directly and does not depend on this EventSystem.
            if (inputEventSystem == null)
            {
                inputEventSystem = FindFirstObjectByType<EventSystem>();
                if (inputEventSystem == null)
                {
                    var eventSystemObject = new GameObject("Multiplayer EventSystem", typeof(EventSystem));
                    eventSystemObject.transform.SetParent(transform, false);
                    inputEventSystem = eventSystemObject.GetComponent<EventSystem>();
                }
                else
                {
                    inputEventSystem.transform.SetParent(transform, true);
                }
            }

            desktopInput = inputEventSystem.GetComponent<InputSystemUIInputModule>();
            if (desktopInput == null)
                desktopInput = inputEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            // Loaded scenes may contain their own EventSystem. Disable those duplicates;
            // this persistent EventSystem serves both desktop and VR menu interaction.
            foreach (var other in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                if (other == inputEventSystem || !other.enabled) continue;
                foreach (var module in other.GetComponents<BaseInputModule>())
                    module.enabled = false;
                other.enabled = false;
            }
        }
    }
}
