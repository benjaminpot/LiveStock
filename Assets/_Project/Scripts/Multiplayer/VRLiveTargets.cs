using System;
using UnityEngine;
using LiveStock.MotionRecording;
namespace LiveStock.Multiplayer
{
    [DefaultExecutionOrder(900)]
    public class VRLiveTargets : MonoBehaviour
    {
        Transform[] anchors, targets;
        Vector3[] offsets;
        Quaternion[] rotations;
        public void Initialize(VRRecordingTargetPlayer player)
        {
            var nodes = GetComponentsInChildren<Transform>(true);
            anchors = new[] { Array.Find(nodes, n=>n.name == "CenterEyeAnchor"), Array.Find(nodes, n=>n.name == "LeftHandAnchor"), Array.Find(nodes, n=>n.name == "RightHandAnchor") };
            targets = new[] { player.headTarget, player.leftHandTarget, player.rightHandTarget };
            offsets = new Vector3[3]; rotations = new Quaternion[3];
            for (int i=0;i<3;i++) if (anchors[i] != null) { offsets[i] = targets[i].position - anchors[i].position; rotations[i] = Quaternion.Inverse(anchors[i].rotation) * targets[i].rotation; }
        }
        void LateUpdate()
        {
            if (anchors == null) return;
            for (int i=0;i<3;i++) if (anchors[i] != null) targets[i].SetPositionAndRotation(anchors[i].position + offsets[i], anchors[i].rotation * rotations[i]);
        }
    }
}
