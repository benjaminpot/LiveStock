using Fusion;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public struct MoveInput : INetworkInput
    {
        public NetworkBool IsVR;
        public Vector3 Root, Head, Left, Right;
        public Quaternion RootRotation, HeadRotation, LeftRotation, RightRotation;
    }

    public class NetworkPlayer : NetworkBehaviour
    {
        public GameObject LocalVRPrefab, RemoteVRPrefab;
        [Networked] public NetworkBool IsVR { get; set; }
        [Networked] Vector3 Head { get; set; }
        [Networked] Vector3 Left { get; set; }
        [Networked] Vector3 Right { get; set; }
        [Networked] Quaternion HeadRotation { get; set; }
        [Networked] Quaternion LeftRotation { get; set; }
        [Networked] Quaternion RightRotation { get; set; }
        GameObject visual;
        LiveStock.MotionRecording.VRRecordingTargetPlayer targets;
        bool visualRole;
        public override void Spawned()
        {
            if (Object.HasInputAuthority) FindFirstObjectByType<SessionLauncher>().CreateLocalRole(Runner, transform.position);
        }
        public override void FixedUpdateNetwork()
        {
            if (!GetInput(out MoveInput data)) return;
            IsVR = data.IsVR;
            transform.SetPositionAndRotation(data.Root, data.RootRotation);
            Head = data.Head; Left = data.Left; Right = data.Right;
            HeadRotation = data.HeadRotation; LeftRotation = data.LeftRotation; RightRotation = data.RightRotation;
        }
        public override void Render()
        {
            if (Object.HasInputAuthority) return;
            if (visualRole != (bool)IsVR)
            {
                visualRole = IsVR;
                if (visual != null) Destroy(visual);
                if (IsVR && RemoteVRPrefab != null)
                {
                    visual = Instantiate(RemoteVRPrefab, transform.position, transform.rotation, transform);
                    targets = System.Array.Find(visual.GetComponentsInChildren<LiveStock.MotionRecording.VRRecordingTargetPlayer>(true), t => t.headTarget != null && t.leftHandTarget != null && t.rightHandTarget != null);
                }
            }
            if (targets == null) return;
            targets.headTarget.SetPositionAndRotation(Head, HeadRotation);
            targets.leftHandTarget.SetPositionAndRotation(Left, LeftRotation);
            targets.rightHandTarget.SetPositionAndRotation(Right, RightRotation);
        }
    }
}
