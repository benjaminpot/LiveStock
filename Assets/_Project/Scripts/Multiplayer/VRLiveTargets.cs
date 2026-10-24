using System;
using UnityEngine;
using LiveStock.MotionRecording;
namespace LiveStock.Multiplayer
{
    [DefaultExecutionOrder(900)]
    public class VRLiveTargets : MonoBehaviour
    {
        Transform[] anchors;
        Transform[] targets;
        Vector3[] localOffsets;
        Quaternion[] localRotations;

        public void Initialize(VRRecordingTargetPlayer player)
        {
            var nodes = GetComponentsInChildren<Transform>(true);
            anchors = new[]
            {
                Array.Find(nodes, node => node.name == "CenterEyeAnchor"),
                Array.Find(nodes, node => node.name == "LeftHandAnchor"),
                Array.Find(nodes, node => node.name == "RightHandAnchor")
            };
            targets = new[] { player.headTarget, player.leftHandTarget, player.rightHandTarget };
            localOffsets = new Vector3[anchors.Length];
            localRotations = new Quaternion[anchors.Length];
            RecalibrateOffsets();
        }

        public void RecalibrateOffsets()
        {
            if (anchors == null || targets == null)
            {
                return;
            }

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] == null || targets[i] == null)
                {
                    continue;
                }

                localOffsets[i] = anchors[i].InverseTransformPoint(targets[i].position);
                localRotations[i] = Quaternion.Inverse(anchors[i].rotation) * targets[i].rotation;
            }
        }

        void LateUpdate()
        {
            if (anchors == null || targets == null)
            {
                return;
            }

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] == null || targets[i] == null)
                {
                    continue;
                }

                targets[i].SetPositionAndRotation(
                    anchors[i].TransformPoint(localOffsets[i]),
                    anchors[i].rotation * localRotations[i]);
            }
        }
    }
}
