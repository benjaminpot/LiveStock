using UnityEngine;

namespace LiveStock.MotionRecording
{
    public enum SheepRetargetPositionMode
    {
        PreserveRecordedTargetOffsets,
        DirectAnchorDelta
    }

    [DisallowMultipleComponent]
    public sealed class SheepMotionRetargeter : MonoBehaviour
    {
        [Header("Rig References")]
        [SerializeField] private Transform sheepRoot;
        [SerializeField] private Transform headPivot;
        [SerializeField] private Transform headTarget;
        [SerializeField] private Transform leftFrontFootTarget;
        [SerializeField] private Transform rightFrontFootTarget;

        [Header("Source To Sheep Mapping")]
        [Tooltip("Rotates TrackingSpace axes into sheep-root local axes.")]
        [SerializeField] private Vector3 sourceToSheepEuler;
        [Tooltip("Per-axis multiplier for source position differences, in sheep local units per metre.")]
        [SerializeField] private Vector3 positionScale = Vector3.one;
        [SerializeField, Range(0f, 1f)] private float headTranslationWeight = 1f;
        [SerializeField] private bool applyFrontFootRotation = true;

        [Header("Recorded Target Compatibility")]
        [Tooltip("PreserveRecordedTargetOffsets reproduces the old setup by applying each offset in its VR Anchor space before converting the result into sheep-root space.")]
        [SerializeField] private SheepRetargetPositionMode positionMode =
            SheepRetargetPositionMode.PreserveRecordedTargetOffsets;
        [Tooltip("The old Target offset below CenterEyeAnchor.")]
        [SerializeField] private Vector3 headAnchorTargetOffset = new Vector3(0f, -0.62f, 0.21f);
        [Tooltip("The old Target L offset below LeftHandAnchor.")]
        [SerializeField] private Vector3 leftHandTargetOffset = new Vector3(0f, 0f, 0.3f);
        [Tooltip("The old TargetR offset below RightHandAnchor.")]
        [SerializeField] private Vector3 rightHandTargetOffset = new Vector3(0f, 0f, 0.3f);

        private RigRestPose _rigRest;
        private SourcePose _sourceOrigin;
        private bool _hasRigRest;
        private bool _hasSourceOrigin;

        public bool IsCalibrated => _hasRigRest && _hasSourceOrigin;

        private struct RigRestPose
        {
            public Vector3 headPositionInRoot;
            public Vector3 headDirectionInRoot;
            public float headAimDistance;
            public Quaternion headTargetRotationInRoot;
            public Vector3 leftPositionInRoot;
            public Quaternion leftRotationInRoot;
            public Vector3 rightPositionInRoot;
            public Quaternion rightRotationInRoot;
        }

        private struct SourcePose
        {
            public Vector3 headPosition;
            public Quaternion headRotation;
            public Vector3 leftPosition;
            public Quaternion leftRotation;
            public Vector3 rightPosition;
            public Quaternion rightRotation;
        }

        private void Awake()
        {
            CaptureRigRestPose();
        }

        public void CaptureRigRestPose()
        {
            if (!HasRequiredRigReferences())
            {
                _hasRigRest = false;
                return;
            }

            Vector3 headOffset = headTarget.position - headPivot.position;
            float distance = headOffset.magnitude;
            if (distance < 0.0001f)
            {
                headOffset = sheepRoot.forward;
                distance = 1f;
            }

            _rigRest = new RigRestPose
            {
                headPositionInRoot = sheepRoot.InverseTransformPoint(headTarget.position),
                headDirectionInRoot = sheepRoot.InverseTransformDirection(headOffset.normalized),
                headAimDistance = distance,
                headTargetRotationInRoot = Quaternion.Inverse(sheepRoot.rotation) * headTarget.rotation,
                leftPositionInRoot = sheepRoot.InverseTransformPoint(leftFrontFootTarget.position),
                leftRotationInRoot = Quaternion.Inverse(sheepRoot.rotation) * leftFrontFootTarget.rotation,
                rightPositionInRoot = sheepRoot.InverseTransformPoint(rightFrontFootTarget.position),
                rightRotationInRoot = Quaternion.Inverse(sheepRoot.rotation) * rightFrontFootTarget.rotation
            };
            _hasRigRest = true;
        }

        public void CalibrateSource(
            Vector3 headPosition,
            Quaternion headRotation,
            Vector3 leftPosition,
            Quaternion leftRotation,
            Vector3 rightPosition,
            Quaternion rightRotation)
        {
            if (!_hasRigRest)
            {
                CaptureRigRestPose();
            }

            _sourceOrigin = new SourcePose
            {
                headPosition = headPosition,
                headRotation = headRotation,
                leftPosition = leftPosition,
                leftRotation = leftRotation,
                rightPosition = rightPosition,
                rightRotation = rightRotation
            };
            _hasSourceOrigin = true;
        }

        public void ApplySourcePose(
            Vector3 headPosition,
            Quaternion headRotation,
            Vector3 leftPosition,
            Quaternion leftRotation,
            Vector3 rightPosition,
            Quaternion rightRotation)
        {
            if (!IsCalibrated || !HasRequiredRigReferences())
            {
                return;
            }

            Quaternion basis = Quaternion.Euler(sourceToSheepEuler);
            Vector3 headDelta = MapPosition(
                GetSourcePositionDelta(
                    headPosition,
                    headRotation,
                    _sourceOrigin.headPosition,
                    _sourceOrigin.headRotation,
                    headAnchorTargetOffset),
                basis);
            Quaternion headRotationDelta = MapRotation(
                headRotation * Quaternion.Inverse(_sourceOrigin.headRotation),
                basis);

            if (positionMode == SheepRetargetPositionMode.PreserveRecordedTargetOffsets)
            {
                headTarget.position = sheepRoot.TransformPoint(
                    _rigRest.headPositionInRoot + headDelta * headTranslationWeight);
            }
            else
            {
                Vector3 lookDirectionInRoot = headRotationDelta * _rigRest.headDirectionInRoot;
                Vector3 headTranslationWorld = sheepRoot.TransformVector(headDelta * headTranslationWeight);
                headTarget.position = headPivot.position
                    + sheepRoot.TransformDirection(lookDirectionInRoot).normalized * _rigRest.headAimDistance
                    + headTranslationWorld;
            }

            headTarget.rotation = sheepRoot.rotation * _rigRest.headTargetRotationInRoot;

            ApplyFoot(
                leftFrontFootTarget,
                _rigRest.leftPositionInRoot,
                _rigRest.leftRotationInRoot,
                GetSourcePositionDelta(
                    leftPosition,
                    leftRotation,
                    _sourceOrigin.leftPosition,
                    _sourceOrigin.leftRotation,
                    leftHandTargetOffset),
                leftRotation * Quaternion.Inverse(_sourceOrigin.leftRotation),
                basis);
            ApplyFoot(
                rightFrontFootTarget,
                _rigRest.rightPositionInRoot,
                _rigRest.rightRotationInRoot,
                GetSourcePositionDelta(
                    rightPosition,
                    rightRotation,
                    _sourceOrigin.rightPosition,
                    _sourceOrigin.rightRotation,
                    rightHandTargetOffset),
                rightRotation * Quaternion.Inverse(_sourceOrigin.rightRotation),
                basis);
        }

        public void RestoreRigRestPose()
        {
            if (!_hasRigRest || !HasRequiredRigReferences())
            {
                return;
            }

            headTarget.position = headPivot.position
                + sheepRoot.TransformDirection(_rigRest.headDirectionInRoot).normalized * _rigRest.headAimDistance;
            headTarget.rotation = sheepRoot.rotation * _rigRest.headTargetRotationInRoot;
            leftFrontFootTarget.position = sheepRoot.TransformPoint(_rigRest.leftPositionInRoot);
            leftFrontFootTarget.rotation = sheepRoot.rotation * _rigRest.leftRotationInRoot;
            rightFrontFootTarget.position = sheepRoot.TransformPoint(_rigRest.rightPositionInRoot);
            rightFrontFootTarget.rotation = sheepRoot.rotation * _rigRest.rightRotationInRoot;
        }

        public void ClearSourceCalibration()
        {
            _hasSourceOrigin = false;
        }

        private void ApplyFoot(
            Transform target,
            Vector3 restPositionInRoot,
            Quaternion restRotationInRoot,
            Vector3 sourcePositionDelta,
            Quaternion sourceRotationDelta,
            Quaternion basis)
        {
            Vector3 targetPositionInRoot = restPositionInRoot + MapPosition(sourcePositionDelta, basis);
            target.position = sheepRoot.TransformPoint(targetPositionInRoot);

            Quaternion rotationInRoot = applyFrontFootRotation
                ? MapRotation(sourceRotationDelta, basis) * restRotationInRoot
                : restRotationInRoot;
            target.rotation = sheepRoot.rotation * rotationInRoot;
        }

        private Vector3 MapPosition(Vector3 sourceDelta, Quaternion basis)
        {
            return Vector3.Scale(basis * sourceDelta, positionScale);
        }

        private Vector3 GetSourcePositionDelta(
            Vector3 currentPosition,
            Quaternion currentRotation,
            Vector3 originPosition,
            Quaternion originRotation,
            Vector3 targetOffset)
        {
            if (positionMode == SheepRetargetPositionMode.DirectAnchorDelta)
            {
                return currentPosition - originPosition;
            }

            Vector3 currentTargetPosition = currentPosition + currentRotation * targetOffset;
            Vector3 originTargetPosition = originPosition + originRotation * targetOffset;
            return currentTargetPosition - originTargetPosition;
        }

        private static Quaternion MapRotation(Quaternion sourceDelta, Quaternion basis)
        {
            return basis * sourceDelta * Quaternion.Inverse(basis);
        }

        private bool HasRequiredRigReferences()
        {
            return sheepRoot != null
                && headPivot != null
                && headTarget != null
                && leftFrontFootTarget != null
                && rightFrontFootTarget != null;
        }

#if UNITY_EDITOR
        public void ConfigureForRig(
            Transform root,
            Transform pivot,
            Transform targetHead,
            Transform targetLeftFoot,
            Transform targetRightFoot)
        {
            sheepRoot = root;
            headPivot = pivot;
            headTarget = targetHead;
            leftFrontFootTarget = targetLeftFoot;
            rightFrontFootTarget = targetRightFoot;
            CaptureRigRestPose();
        }
#endif
    }
}
