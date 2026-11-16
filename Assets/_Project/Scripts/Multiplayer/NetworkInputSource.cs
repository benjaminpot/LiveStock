using Fusion;
using UnityEngine;

/// <summary>One tick input for the owning Client's root and root-relative sheep targets.</summary>
[System.Serializable]
public struct PlayerNetworkInput : INetworkInput
{
    public NetworkBool IsVR, HasPose;
    /// <summary>Device tracking validity is independent of whether Transform poses can be replicated.</summary>
    public NetworkBool TrackingValid;
    public int Stage;
    public VRTrackedPose Pose;
    public PlayerBodyPose Body;
}

/// <summary>Caches one Client's completed render-frame pose for its Runner's OnInput callback.</summary>
[DefaultExecutionOrder(2000)]
public sealed class NetworkInputSource : MonoBehaviour
{
    ConnectionManager owner;
    VRClientPoseSource vr;
    DesktopTestPlayer desktop;
    [SerializeField, Tooltip("Latest complete local sample submitted by this Runner's OnInput callback.")]
    PlayerNetworkInput cached;
    public PlayerNetworkInput Current => cached;

    /// <summary>Binds explicit local sources so Multi-Peer never samples another Client's rig.</summary>
    public void Initialize(ConnectionManager client)
    {
        owner = client;
        vr = client.GetComponent<VRClientPoseSource>();
        desktop = client.GetComponentInChildren<DesktopTestPlayer>(true);
    }

    /// <summary>Revokes cached pose and body input during scene changes or when this source is disabled.</summary>
    public void Clear() => cached = default;

    void OnDisable() => Clear();

    void LateUpdate()
    {
        Clear();
        if (owner == null || !owner.IsConnected || owner.Runner.IsSceneManagerBusy || owner.State == null
            || owner.State.Phase == LobbySessionState.SessionPhase.LoadingMainGame) return;
        var marker = FlowSceneMarker.For(owner.Runner);
        if (marker == null) return;
        cached.Stage = marker.Stage;
        cached.IsVR = !owner.IsDesktop;
        var gameplay = owner.GetComponent<PlayerGameplay>();
        if (gameplay != null) cached.Body = gameplay.ReadBody();
        if (owner.IsDesktop && desktop != null && desktop.isActiveAndEnabled)
        {
            cached.Pose = new VRTrackedPose
            {
                RootPosition = desktop.transform.position, RootRotation = desktop.transform.rotation,
                HeadRotation = Quaternion.identity, LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity
            };
            cached.HasPose = true;
        }
        else if (!owner.IsDesktop && vr != null && vr.isActiveAndEnabled && vr.IsConfigured)
        {
            cached.Pose = vr.ReadPose();
            cached.HasPose = cached.Pose.IsFinite;
            cached.TrackingValid = vr.TrackingAvailable;
        }
    }
}
