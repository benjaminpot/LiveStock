using LiveStock.MotionRecording;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    /// <summary>
    /// Remote-only IK targets driven by the networked player pose.
    /// This deliberately does not use VRRecordingTargetPlayer: recorded playback
    /// and live network poses must never compete for the same transforms.
    /// </summary>
    public sealed class RemoteVRPoseTargets : MonoBehaviour
    {
        [SerializeField] Transform headTarget;
        [SerializeField] Transform leftHandTarget;
        [SerializeField] Transform rightHandTarget;

        public bool IsConfigured => headTarget != null && leftHandTarget != null && rightHandTarget != null;

        void Awake()
        {
            // A remote avatar is visual-only. Its source prefab contains a local
            // Meta camera rig, which must not create extra cameras, audio or tracking.
            foreach (var camera in GetComponentsInChildren<Camera>(true))
                camera.enabled = false;
            foreach (var listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
            foreach (var rig in GetComponentsInChildren<OVRCameraRig>(true))
                rig.enabled = false;
            foreach (var manager in GetComponentsInChildren<OVRManager>(true))
                manager.enabled = false;
            foreach (var playback in GetComponentsInChildren<VRRecordingTargetPlayer>(true))
            {
                playback.playOnStart = false;
                playback.enabled = false;
            }
            foreach (var liveTargets in GetComponentsInChildren<VRLiveTargets>(true))
                liveTargets.enabled = false;
        }

        public void ApplyPose(Transform playerRoot, Vector3 head, Quaternion headRotation,
            Vector3 leftHand, Quaternion leftHandRotation, Vector3 rightHand, Quaternion rightHandRotation)
        {
            if (!IsConfigured || playerRoot == null)
                return;

            headTarget.SetPositionAndRotation(playerRoot.TransformPoint(head), playerRoot.rotation * headRotation);
            leftHandTarget.SetPositionAndRotation(playerRoot.TransformPoint(leftHand), playerRoot.rotation * leftHandRotation);
            rightHandTarget.SetPositionAndRotation(playerRoot.TransformPoint(rightHand), playerRoot.rotation * rightHandRotation);
        }

        public void Configure(Transform head, Transform leftHand, Transform rightHand)
        {
            headTarget = head;
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
        }
    }
}
