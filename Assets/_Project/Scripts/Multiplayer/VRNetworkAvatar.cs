using Fusion;
using LiveStock.Multiplayer;
using UnityEngine;

/// <summary>Server-owned sheep root and replicated head/hand IK targets for the new connection flow.</summary>
public sealed class VRNetworkAvatar : NetworkBehaviour
{
    [SerializeField] RemoteVRPoseTargets visualTargets;
    [Networked] public VRTrackedPose Pose { get; set; }
    [Networked] public NetworkBool HasPose { get; set; }
    public RemoteVRPoseTargets VisualTargets => visualTargets;

    public override void Spawned()
    {
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
        Pose = pose;
        HasPose = true;
    }

    public override void Render()
    {
        if (!HasPose || visualTargets == null) return;
        var pose = Pose;
        visualTargets.ApplyPose(transform, pose.Head, pose.HeadRotation, pose.LeftHand, pose.LeftRotation,
            pose.RightHand, pose.RightRotation);
    }
}
