using Fusion;
using LiveStock.Multiplayer;
using UnityEngine;

/// <summary>Server-owned sheep root and replicated head/hand IK targets for the new connection flow.</summary>
public sealed class VRNetworkAvatar : NetworkBehaviour
{
    [SerializeField] RemoteVRPoseTargets visualTargets;
    NetworkTargetPose targets;
    public bool HasPose => targets != null && targets.HasPose;
    [Networked] public NetworkBool TrackingValid { get; private set; }
    public VRTrackedPose Pose => new VRTrackedPose
    {
        RootPosition = transform.position, RootRotation = transform.rotation,
        Head = targets.State.Head, LeftHand = targets.State.Left, RightHand = targets.State.Right,
        HeadRotation = targets.State.HeadRotation, LeftRotation = targets.State.LeftRotation,
        RightRotation = targets.State.RightRotation
    };
    public RemoteVRPoseTargets VisualTargets => visualTargets;

    public void SetTrackingValid(bool valid)
    {
        if (Object.HasStateAuthority) TrackingValid = valid;
    }

    public override void Spawned()
    {
        targets = GetComponent<NetworkTargetPose>();
        foreach (var animator in GetComponentsInChildren<Animator>(true))
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (Object.InputAuthority == Runner.LocalPlayer)
            foreach (var visual in GetComponentsInChildren<Renderer>(true)) visual.enabled = false;
    }

    /// <summary>Called only by the session's state authority after validating the sender.</summary>
    public void SetPose(VRTrackedPose pose)
    {
        if (!Object.HasStateAuthority) return;
        transform.SetPositionAndRotation(pose.RootPosition, pose.RootRotation.normalized);
        targets.SetPose(ThreeTargetPose.FromTracked(pose));
    }
}
