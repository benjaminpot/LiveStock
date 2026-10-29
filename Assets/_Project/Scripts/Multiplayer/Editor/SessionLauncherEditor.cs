using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LiveStock.Multiplayer.Editor
{
    [CustomEditor(typeof(SessionLauncher))]
    public sealed class SessionLauncherEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            root.Add(new HelpBox(
                "Enter Play Mode, then use this button to select the VR role and connect to the configured room.",
                HelpBoxMessageType.Info));

            var connectButton = new Button(() =>
            {
                serializedObject.ApplyModifiedProperties();
                ((SessionLauncher)target).ConnectAsVRClient();
            })
            {
                name = "connectAsVRClient",
                text = "Connect as VR Client"
            };
            root.Add(connectButton);

            void RefreshState()
            {
                if (target == null)
                    return;

                var launcher = (SessionLauncher)target;
                connectButton.SetEnabled(Application.isPlaying && !launcher.IsServerEntry && launcher.MenuFocused && !launcher.Busy && launcher.CanSelectVR);
            }

            RefreshState();
            root.schedule.Execute(RefreshState).Every(100);
            return root;
        }
    }
}
