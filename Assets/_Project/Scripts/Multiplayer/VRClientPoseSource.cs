using Fusion;
using UnityEngine;

/// <summary>Root-relative IK target poses captured from the local VR sheep rig.</summary>
[System.Serializable]
public struct VRTrackedPose : INetworkStruct
{
    public Vector3 RootPosition, Head, LeftHand, RightHand;
    public Quaternion RootRotation, HeadRotation, LeftRotation, RightRotation;
    public bool IsFinite => Finite(RootPosition) && Finite(Head) && Finite(LeftHand) && Finite(RightHand)
        && Finite(RootRotation) && Finite(HeadRotation) && Finite(LeftRotation) && Finite(RightRotation)
        && RootPosition.sqrMagnitude < 100000000f && Head.sqrMagnitude < 100f
        && LeftHand.sqrMagnitude < 100f && RightHand.sqrMagnitude < 100f;
    static bool Finite(Vector3 value) => float.IsFinite(value.x + value.y + value.z);
    static bool Finite(Quaternion value) => float.IsFinite(value.x + value.y + value.z + value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .000001f;
}

/// <summary>Provides this Client's calibrated VR IK targets to the Runner's input source.</summary>
[DefaultExecutionOrder(1000)]
public sealed class VRClientPoseSource : MonoBehaviour
{
    [SerializeField] Transform rigRoot;
    [SerializeField] OVRCameraRig trackingRig;
    [SerializeField] Transform headAnchor, leftHandAnchor, rightHandAnchor;
    [SerializeField] Transform headTarget, leftHandTarget, rightHandTarget;
    ConnectionManager owner;
    public Transform RigRoot => rigRoot;
    public OVRCameraRig TrackingRig => trackingRig;
    public Transform HeadAnchor => headAnchor;
    public Transform LeftHandAnchor => leftHandAnchor;
    public Transform RightHandAnchor => rightHandAnchor;
    public Transform HeadTarget => headTarget;
    public Transform LeftHandTarget => leftHandTarget;
    public Transform RightHandTarget => rightHandTarget;
    public bool IsConfigured => rigRoot != null && trackingRig != null && headTarget != null
        && leftHandTarget != null && rightHandTarget != null && headAnchor != null
        && leftHandAnchor != null && rightHandAnchor != null;
    /// <summary>Requires a tracked head and both controllers for a complete pose; explicit preview stays device-free.</summary>
    public bool TrackingAvailable => owner != null && (owner.IsVRPreview || (OVRManager.isHmdPresent
        && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)
        && OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.EyeCenter)
        && ControllerTracked(OVRInput.Controller.LTouch) && ControllerTracked(OVRInput.Controller.RTouch)));

    static bool ControllerTracked(OVRInput.Controller controller) => OVRInput.GetControllerPositionTracked(controller)
        && OVRInput.GetControllerOrientationTracked(controller);

    /// <summary>Binds this source to one Client and seeds anchors for device-free preview.</summary>
    public void Initialize(ConnectionManager client)
    {
        owner = client;
        if (!owner.IsVRPreview || !IsConfigured) return;
        // The preview uses the same anchor hierarchy while native tracking is inactive.
        headAnchor.localPosition = new Vector3(0, 1.65f, 0);
        leftHandAnchor.localPosition = new Vector3(-.3f, 1.2f, .3f);
        rightHandAnchor.localPosition = new Vector3(.3f, 1.2f, .3f);
    }

    /// <summary>Samples calibrated IK targets relative to the same root used by the remote sheep.</summary>
    public VRTrackedPose ReadPose() => new VRTrackedPose
    {
        RootPosition = rigRoot.position, RootRotation = rigRoot.rotation,
        Head = rigRoot.InverseTransformPoint(headTarget.position),
        LeftHand = rigRoot.InverseTransformPoint(leftHandTarget.position),
        RightHand = rigRoot.InverseTransformPoint(rightHandTarget.position),
        HeadRotation = Quaternion.Inverse(rigRoot.rotation) * headTarget.rotation,
        LeftRotation = Quaternion.Inverse(rigRoot.rotation) * leftHandTarget.rotation,
        RightRotation = Quaternion.Inverse(rigRoot.rotation) * rightHandTarget.rotation
    };

}
