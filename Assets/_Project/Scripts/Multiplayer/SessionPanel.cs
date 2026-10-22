using TMPro;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;

namespace LiveStock.Multiplayer
{
    public class SessionPanel : MonoBehaviour
    {
        static SessionPanel instance;
        TMP_Text status;
        Canvas canvas;
        RectTransform panelRect;
        Transform canvasHome;
        UnityEngine.UI.GraphicRaycaster desktopRaycaster;
        OVRRaycaster vrRaycaster;
        InputSystemUIInputModule desktopInput;
        OVRInputModule vrInput;
        OVRCameraRig configuredRig;
        UnityEngine.UI.Button server, start, offline, farm, leave;
        UnityEngine.UI.Button role;
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
            var canvasObject = new GameObject("Multiplayer UI", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(OVRRaycaster));
            canvasObject.transform.SetParent(transform);
            canvasHome = transform;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            desktopRaycaster = canvasObject.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            vrRaycaster = canvasObject.GetComponent<OVRRaycaster>();
            vrRaycaster.enabled = false;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var panel = new GameObject("Panel", typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(canvasObject.transform, false);
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0, 1); panelRect.pivot = new Vector2(0, 1); panelRect.anchoredPosition = new Vector2(20, -20); panelRect.sizeDelta = new Vector2(520, 440);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(.04f, .08f, .12f, .94f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.padding = new RectOffset(16, 16, 12, 12); layout.spacing = 6;
            status = Label(panel.transform, "LiveStock Multiplayer");
            server = Button(panel.transform, "Start Local Dedicated Server", () => FindFirstObjectByType<SessionLauncher>().StartDedicatedServer());
            role = Button(panel.transform, "Role: PC Observer", () => FindFirstObjectByType<SessionLauncher>().ToggleRole());
            start = Button(panel.transform, "Join Dedicated Server", () => FindFirstObjectByType<SessionLauncher>().StartSession());
            offline = Button(panel.transform, "Offline test", () => FindFirstObjectByType<SessionLauncher>().StartSession(true));
            farm = Button(panel.transform, "Start Farm (Host)", () => FindFirstObjectByType<SessionLauncher>().StartFarm());
            leave = Button(panel.transform, "Leave", () => FindFirstObjectByType<SessionLauncher>().Leave());
#if UNITY_EDITOR
            addPeer = Button(panel.transform, "Open New Player Menu (F1)", () => FindFirstObjectByType<SessionLauncher>().AddEditorPeer());
            switchPeer = Button(panel.transform, "Switch Peer (F2)", () => FindFirstObjectByType<SessionLauncher>().SwitchActiveRunner());
#endif
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            desktopInput = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (desktopInput == null) desktopInput = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            vrInput = eventSystem.GetComponent<OVRInputModule>();
            if (vrInput == null) vrInput = eventSystem.gameObject.AddComponent<OVRInputModule>();
            vrInput.joyPadClickButton = OVRInput.Button.PrimaryIndexTrigger;
            vrInput.enabled = false;
        }
        static TMP_Text Label(Transform parent, string value)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = 20; text.color = Color.white; text.raycastTarget = false; return text;
        }
        static UnityEngine.UI.Button Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(.12f, .3f, .4f);
            var button = go.GetComponent<UnityEngine.UI.Button>(); button.onClick.AddListener(action);
            var label = Label(go.transform, text); var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        void Update()
        {
            var launcher = FindFirstObjectByType<SessionLauncher>(); if (launcher == null || status == null) return;
            ConfigurePresentation(launcher);
            var anyNetwork = launcher.ServerRunning || launcher.ConnectedPeerCount > 0;
            server.gameObject.SetActive(!launcher.ServerRunning && launcher.ConnectedPeerCount == 0);
            server.interactable = !launcher.Busy;
            role.gameObject.SetActive(launcher.MenuFocused); role.interactable = !launcher.Busy && launcher.CanSelectVR;
            role.GetComponentInChildren<TMP_Text>().text = launcher.UseVR ? "Role: VR Sheep" : launcher.CanSelectVR ? "Role: PC Observer" : "Role: PC Observer (VR in use)";
            start.GetComponentInChildren<TMP_Text>().text = launcher.UseVR ? "Join as VR Sheep Client" : "Join as PC Observer Client";
            status.text = launcher.Status
                + "\nServer: " + (launcher.ServerRunning ? "LOCAL DEDICATED" : "external / not started")
                + " | connected clients: " + launcher.ConnectedPeerCount + "/4"
                + (launcher.MenuFocused ? " | active: MENU" : " | active client: " + (launcher.ActivePeerIndex + 1));
            start.gameObject.SetActive(launcher.MenuFocused);
            offline.gameObject.SetActive(launcher.MenuFocused && !anyNetwork);
            start.interactable = offline.interactable = !launcher.Busy;
            farm.gameObject.SetActive(launcher.ServerRunning);
            farm.GetComponentInChildren<TMP_Text>().text = "Start Farm (Dedicated Server)";
            farm.interactable = !launcher.Busy;
            leave.gameObject.SetActive(anyNetwork);
#if UNITY_EDITOR
            bool multiPeer = launcher.IsMultiPeer && anyNetwork;
            addPeer.gameObject.SetActive(multiPeer && !launcher.PeerAtMenu); addPeer.interactable = launcher.CanAddEditorPeer;
            switchPeer.gameObject.SetActive(multiPeer); switchPeer.interactable = !launcher.Busy && launcher.PeerCount > 1;
#endif
        }

        void ConfigurePresentation(SessionLauncher launcher)
        {
            var rig = launcher.MenuFocused && launcher.UseVR ? launcher.MenuVRRig : null;
            bool useVR = rig != null;
            if (useVR && configuredRig != rig)
            {
                configuredRig = rig;
                canvas.transform.SetParent(rig.centerEyeAnchor, false);
                canvas.transform.localPosition = new Vector3(0, 0, 1.5f);
                canvas.transform.localRotation = Quaternion.identity;
                canvas.transform.localScale = Vector3.one * .0015f;
                canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 720);
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = rig.centerEyeAnchor.GetComponent<Camera>();
                vrInput.rayTransform = rig.rightControllerAnchor;
                panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(.5f, .5f);
                panelRect.anchoredPosition = Vector2.zero;
            }
            else if (!useVR && configuredRig != null)
            {
                configuredRig = null;
                canvas.transform.SetParent(canvasHome, false);
                canvas.transform.localScale = Vector3.one;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0, 1);
                panelRect.pivot = new Vector2(0, 1);
                panelRect.anchoredPosition = new Vector2(20, -20);
            }
            desktopRaycaster.enabled = !useVR;
            desktopInput.enabled = !useVR;
            vrRaycaster.enabled = useVR;
            vrInput.enabled = useVR;
        }
    }
}
