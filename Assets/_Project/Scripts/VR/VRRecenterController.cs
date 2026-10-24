using System.Collections;
using LiveStock.Multiplayer;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LiveStock.VR
{
    [DisallowMultipleComponent]
    public sealed class VRRecenterController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private OVRInput.Button recenterButton = OVRInput.Button.Start;
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.LTouch;
        [SerializeField, Min(0f)] private float holdDuration = 1f;
        [SerializeField] private bool enableKeyboardShortcut = true;

        [Header("Recenter")]
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private bool recenterOnEnable;
        [SerializeField] private bool requireUserPresence = true;
        [SerializeField, Min(0.1f)] private float trackingReadyTimeout = 5f;
        [SerializeField, Min(0f)] private float cooldown = 1.5f;
        [SerializeField] private bool ensureFloorLevel = true;
        [SerializeField] private bool clearHeadPoseRelativeOffset = true;
        [SerializeField] private bool recalibrateLiveTargets = true;
        [SerializeField] private bool logResult = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onRecentered;
        [SerializeField] private UnityEvent onRecenterFailed;

        private Coroutine _recenterRoutine;
        private float _buttonHeldTime;
        private float _nextAllowedTime;
        private bool _buttonTriggered;

        public bool IsRecentering => _recenterRoutine != null;

        private void Awake()
        {
            if (cameraRig == null)
            {
                cameraRig = GetComponentInChildren<OVRCameraRig>(true);
            }
        }

        private void OnEnable()
        {
            ResetButtonState();
            if (recenterOnEnable)
            {
                RequestRecenter();
            }
        }

        private void OnDisable()
        {
            if (_recenterRoutine != null)
            {
                StopCoroutine(_recenterRoutine);
                _recenterRoutine = null;
            }

            ResetButtonState();
        }

        private void Update()
        {
            UpdateControllerButton();

#if ENABLE_INPUT_SYSTEM
            if (enableKeyboardShortcut
                && Keyboard.current != null
                && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RequestRecenter();
            }
#endif
        }

        private void UpdateControllerButton()
        {
            bool isHeld = OVRInput.Get(recenterButton, controller);
            if (!isHeld)
            {
                ResetButtonState();
                return;
            }

            if (_buttonTriggered)
            {
                return;
            }

            _buttonHeldTime += Time.unscaledDeltaTime;
            if (_buttonHeldTime >= holdDuration)
            {
                _buttonTriggered = true;
                RequestRecenter();
            }
        }

        /// <summary>
        /// Can be connected directly to a Unity UI Button OnClick event.
        /// </summary>
        public void RequestRecenter()
        {
            if (!isActiveAndEnabled || IsRecentering || Time.unscaledTime < _nextAllowedTime)
            {
                return;
            }

            _recenterRoutine = StartCoroutine(RecenterWhenTrackingIsReady());
        }

        private IEnumerator RecenterWhenTrackingIsReady()
        {
            float deadline = Time.unscaledTime + trackingReadyTimeout;
            while (!IsTrackingReady() && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (!IsTrackingReady())
            {
                FinishRecenter(false, "VR tracking did not become ready before the timeout.");
                yield break;
            }

            OVRManager manager = OVRManager.instance;
            if (ensureFloorLevel)
            {
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            }

            if (clearHeadPoseRelativeOffset)
            {
                manager.headPoseRelativeOffsetTranslation = Vector3.zero;
                manager.headPoseRelativeOffsetRotation = Vector3.zero;
            }

            OVRManager.display.RecenterPose();

            // Recenter is applied by the XR runtime. Wait for anchors to receive
            // the new pose before rebuilding avatar target offsets.
            yield return null;
            yield return new WaitForEndOfFrame();

            if (recalibrateLiveTargets)
            {
                foreach (VRLiveTargets liveTargets in GetComponentsInChildren<VRLiveTargets>(true))
                {
                    liveTargets.RecalibrateOffsets();
                }
            }

            FinishRecenter(true, "VR headset and controller poses were recentered.");
        }

        private bool IsTrackingReady()
        {
            if (OVRManager.instance == null
                || OVRManager.display == null
                || cameraRig == null
                || cameraRig.centerEyeAnchor == null
                || !OVRManager.isHmdPresent)
            {
                return false;
            }

            return !requireUserPresence || OVRManager.instance.isUserPresent;
        }

        private void FinishRecenter(bool succeeded, string message)
        {
            _recenterRoutine = null;
            _nextAllowedTime = Time.unscaledTime + cooldown;

            if (logResult)
            {
                if (succeeded)
                {
                    Debug.Log($"[{nameof(VRRecenterController)}] {message}", this);
                }
                else
                {
                    Debug.LogWarning($"[{nameof(VRRecenterController)}] {message}", this);
                }
            }

            if (succeeded)
            {
                onRecentered?.Invoke();
            }
            else
            {
                onRecenterFailed?.Invoke();
            }
        }

        private void ResetButtonState()
        {
            _buttonHeldTime = 0f;
            _buttonTriggered = false;
        }
    }
}
