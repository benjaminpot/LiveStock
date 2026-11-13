using Fusion;
using UnityEngine;

/// <summary>Calibrated head and front-limb targets in their common root's local space.</summary>
public struct ThreeTargetPose : INetworkStruct
{
    public Vector3 Head, Left, Right;
    public Quaternion HeadRotation, LeftRotation, RightRotation;

    public static ThreeTargetPose FromTracked(VRTrackedPose pose) => new ThreeTargetPose
    {
        Head = pose.Head, Left = pose.LeftHand, Right = pose.RightHand,
        HeadRotation = pose.HeadRotation, LeftRotation = pose.LeftRotation, RightRotation = pose.RightRotation
    };

    public static ThreeTargetPose Interpolate(ThreeTargetPose from, ThreeTargetPose to, float alpha) => new ThreeTargetPose
    {
        Head = Vector3.Lerp(from.Head, to.Head, alpha), Left = Vector3.Lerp(from.Left, to.Left, alpha),
        Right = Vector3.Lerp(from.Right, to.Right, alpha),
        HeadRotation = Quaternion.Slerp(from.HeadRotation, to.HeadRotation, alpha),
        LeftRotation = Quaternion.Slerp(from.LeftRotation, to.LeftRotation, alpha),
        RightRotation = Quaternion.Slerp(from.RightRotation, to.RightRotation, alpha)
    };
}

/// <summary>Replicates three IK targets for either a tracked player or a server-animated NPC.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(2000)]
public sealed class NetworkTargetPose : NetworkBehaviour
{
    [SerializeField, Tooltip("Head, left front limb and right front limb, mapped relative to this object's root.")]
    Transform headTarget, leftTarget, rightTarget;
    [SerializeField, Tooltip("Enable for NPCs whose Server recording or animator generates the targets.")]
    bool captureOnServer;
    [Networked] public ThreeTargetPose State { get; private set; }
    [Networked] public NetworkBool HasPose { get; private set; }
    [Networked] int PoseEpoch { get; set; }
    ThreeTargetPose captured;
    bool hasCapture;
    public Transform HeadTarget => headTarget;
    public Transform LeftTarget => leftTarget;
    public Transform RightTarget => rightTarget;
    public bool IsConfigured => headTarget != null && leftTarget != null && rightTarget != null;
    public bool CaptureOnServer => captureOnServer;

    /// <summary>Publishes validated player targets from the Server's tick input consumer.</summary>
    public void SetPose(ThreeTargetPose pose)
    {
        if (!Object.HasStateAuthority) return;
        State = pose;
        HasPose = true;
    }

    /// <summary>Starts a new interpolation epoch when the object's root changes stages or teleports.</summary>
    public void ResetPose()
    {
        if (!Object.HasStateAuthority) return;
        PoseEpoch++;
        HasPose = false;
        hasCapture = false;
    }

    void LateUpdate()
    {
        if (!captureOnServer || Object == null || !Object.IsValid || !Object.HasStateAuthority || !IsConfigured) return;
        captured = new ThreeTargetPose
        {
            Head = transform.InverseTransformPoint(headTarget.position),
            Left = transform.InverseTransformPoint(leftTarget.position), Right = transform.InverseTransformPoint(rightTarget.position),
            HeadRotation = Quaternion.Inverse(transform.rotation) * headTarget.rotation,
            LeftRotation = Quaternion.Inverse(transform.rotation) * leftTarget.rotation,
            RightRotation = Quaternion.Inverse(transform.rotation) * rightTarget.rotation
        };
        hasCapture = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (captureOnServer && Object.HasStateAuthority && hasCapture) SetPose(captured);
    }

    public override void Render()
    {
        if (!HasPose || !IsConfigured || (captureOnServer && Object.HasStateAuthority)) return;
        var pose = State;
        if (!Object.HasStateAuthority && TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
        {
            var states = GetPropertyReader<ThreeTargetPose>(nameof(State));
            var epochs = GetPropertyReader<int>(nameof(PoseEpoch));
            var valid = GetPropertyReader<NetworkBool>(nameof(HasPose));
            if (!valid.Read(to)) return;
            pose = epochs.Read(from) == epochs.Read(to) && valid.Read(from)
                ? ThreeTargetPose.Interpolate(states.Read(from), states.Read(to), alpha) : states.Read(to);
        }
        headTarget.SetPositionAndRotation(transform.TransformPoint(pose.Head), transform.rotation * pose.HeadRotation);
        leftTarget.SetPositionAndRotation(transform.TransformPoint(pose.Left), transform.rotation * pose.LeftRotation);
        rightTarget.SetPositionAndRotation(transform.TransformPoint(pose.Right), transform.rotation * pose.RightRotation);
    }
}
