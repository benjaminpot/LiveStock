using TMPro;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public enum VRPokeMenuAction
    {
        Connect,
        Leave
    }

    public sealed class VRPokeMenu : MonoBehaviour
    {
        [SerializeField] TMP_Text statusText;

        SessionLauncher launcher;
        VRPokeMenuButton[] buttons;

        void Awake()
        {
            buttons = GetComponentsInChildren<VRPokeMenuButton>(true);
            foreach (var button in buttons)
                button.Bind(this);

            if (statusText == null)
            {
                var statusObject = transform.Find("StatusText");
                if (statusObject != null)
                    statusText = statusObject.GetComponentInChildren<TMP_Text>(true);
            }
        }

        public void Bind(SessionLauncher sessionLauncher)
        {
            launcher = sessionLauncher;
            Refresh();
        }

        void Update() => Refresh();

        void Refresh()
        {
            if (launcher == null)
                launcher = FindFirstObjectByType<SessionLauncher>();
            if (launcher == null)
                return;

            bool connected = launcher.ServerRunning || launcher.ConnectedPeerCount > 0;
            foreach (var button in buttons)
            {
                bool show = IsAvailable(button, connected);
                button.SetAvailable(show);
            }

            if (statusText != null)
            {
                statusText.text = "LIVESTOCK VR\n"
                    + launcher.Status + "\n"
                    + "Room: " + launcher.RoomName + " | Region: " + launcher.Region + "\n"
                    + (connected ? "CONNECTED" : launcher.Busy ? "CONNECTING..." : "NOT CONNECTED");
            }
        }

        bool IsAvailable(VRPokeMenuButton button, bool connected)
        {
            return button.Action switch
            {
                VRPokeMenuAction.Connect => !connected && launcher.MenuFocused && !launcher.Busy && !launcher.IsServerEntry,
                VRPokeMenuAction.Leave => connected && !launcher.Busy,
                _ => false
            };
        }

        public void Invoke(VRPokeMenuAction action)
        {
            if (launcher == null || launcher.Busy)
                return;

            switch (action)
            {
                case VRPokeMenuAction.Connect:
                    launcher.ConnectAsVRClient();
                    break;
                case VRPokeMenuAction.Leave:
                    launcher.Leave();
                    break;
            }
        }

        public void Configure(TMP_Text status) => statusText = status;
    }
}
