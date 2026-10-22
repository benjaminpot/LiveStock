using UnityEngine;

namespace LiveStock.MotionRecording
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SheepMotionRetargeter))]
    public sealed class SheepVrAnchorDriver : MonoBehaviour
    {
        [Header("Live VR Anchors")]
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private bool calibrateOnEnable = true;

        private SheepMotionRetargeter _retargeter;
        private bool _calibrationPending;

        private void Awake()
        {
            _retargeter = GetComponent<SheepMotionRetargeter>();
        }

        private void OnEnable()
        {
            _calibrationPending = calibrateOnEnable;
        }

        private void Update()
        {
            if (_retargeter == null || !HasRequiredAnchors())
            {
                return;
            }

            GetSourcePose(headAnchor, out Vector3 headPosition, out Quaternion headRotation);
            GetSourcePose(leftHandAnchor, out Vector3 leftPosition, out Quaternion leftRotation);
            GetSourcePose(rightHandAnchor, out Vector3 rightPosition, out Quaternion rightRotation);

            if (_calibrationPending || !_retargeter.IsCalibrated)
            {
                _retargeter.CalibrateSource(
                    headPosition,
                    headRotation,
                    leftPosition,
                    leftRotation,
                    rightPosition,
                    rightRotation);
                _calibrationPending = false;
            }

            _retargeter.ApplySourcePose(
                headPosition,
                headRotation,
                leftPosition,
                leftRotation,
                rightPosition,
                rightRotation);
        }

        [ContextMenu("Recalibrate From Current VR Pose")]
        public void Recalibrate()
        {
            _calibrationPending = true;
        }

        private void GetSourcePose(Transform source, out Vector3 position, out Quaternion rotation)
        {
            if (trackingSpace == null)
            {
                position = source.position;
                rotation = source.rotation;
                return;
            }

            position = trackingSpace.InverseTransformPoint(source.position);
            rotation = Quaternion.Inverse(trackingSpace.rotation) * source.rotation;
        }

        private bool HasRequiredAnchors()
        {
            return headAnchor != null && leftHandAnchor != null && rightHandAnchor != null;
        }

#if UNITY_EDITOR
        public void ConfigureAnchors(
            Transform reference,
            Transform head,
            Transform leftHand,
            Transform rightHand)
        {
            trackingSpace = reference;
            headAnchor = head;
            leftHandAnchor = leftHand;
            rightHandAnchor = rightHand;
        }
#endif
    }
}
