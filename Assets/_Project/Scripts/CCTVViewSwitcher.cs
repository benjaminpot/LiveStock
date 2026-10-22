using System;
using Unity.RenderStreaming;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class CCTVViewSwitcher : MonoBehaviour
{
    [Serializable]
    public sealed class CCTVView
    {
        public Camera streamCamera;
        public Camera previewCamera;
    }

    [Tooltip("Use the single Video Stream Sender already registered in Broadcast.")]
    [SerializeField] private VideoStreamSender videoSender;

    [Tooltip("For each CCTV group, assign its CCTV camera and local preview camera.")]
    [SerializeField] private CCTVView[] views = Array.Empty<CCTVView>();

    private int currentViewIndex = -1;

    private void Awake()
    {
        if (videoSender == null || views.Length == 0)
        {
            Debug.LogWarning("[CCTVViewSwitcher] Assign Video Sender and at least one view.", this);
            enabled = false;
            return;
        }

        if (videoSender.source != VideoStreamSource.Camera)
        {
            Debug.LogWarning("[CCTVViewSwitcher] Set Video Stream Sender Source to Camera.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < views.Length; i++)
        {
            var view = views[i];
            if (view == null || view.streamCamera == null || view.previewCamera == null ||
                view.streamCamera == view.previewCamera)
            {
                Debug.LogWarning($"[CCTVViewSwitcher] View {i + 1} needs separate stream and preview cameras.", this);
                enabled = false;
                return;
            }

            for (int j = 0; j < i; j++)
            {
                var previous = views[j];
                if (view.streamCamera == previous.streamCamera ||
                    view.streamCamera == previous.previewCamera ||
                    view.previewCamera == previous.streamCamera ||
                    view.previewCamera == previous.previewCamera)
                {
                    Debug.LogWarning("[CCTVViewSwitcher] Each view must use its own cameras.", this);
                    enabled = false;
                    return;
                }
            }
        }

        SwitchToView(0);
    }

    // Select this under Dynamic CallbackContext in the CCTV Switch input event.
    public void OnSwitch(InputAction.CallbackContext context)
    {
        if (context.performed)
            SwitchNext();
    }

    public void SwitchNext()
    {
        if (!isActiveAndEnabled)
            return;

        SwitchToView((currentViewIndex + 1) % views.Length);
    }

    public void SwitchToView(int index)
    {
        if (!isActiveAndEnabled || index < 0 || index >= views.Length || index == currentViewIndex)
            return;

        // Changing the source uses Render Streaming's track replacement API.
        // Keep the connection, input receiver and GameObjects running.
        videoSender.sourceCamera = views[index].streamCamera;

        for (int i = 0; i < views.Length; i++)
        {
            bool selected = i == index;
            views[i].streamCamera.enabled = selected;
            views[i].previewCamera.enabled = selected;
        }

        currentViewIndex = index;
        Debug.Log($"[CCTVViewSwitcher] Viewing CCTV {index + 1}.", this);
    }
}
