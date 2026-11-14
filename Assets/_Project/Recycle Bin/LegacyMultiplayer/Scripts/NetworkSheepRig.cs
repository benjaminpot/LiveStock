using System.Collections.Generic;
using Fusion;
using LiveStock.MotionRecording;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LiveStock.Multiplayer
{
    public struct RigTargetPose : INetworkStruct
    {
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }

    /// <summary>同步 IK 完成後的骨架外觀；只有 Server 執行 Rigging。</summary>
    [DefaultExecutionOrder(30000)] // LateUpdate 在 Final IK 之後取樣／套用。
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkSheepRig : NetworkBehaviour
    {
        public VRRecordingTargetPlayer Playback;
        [Networked, Capacity(64)] public NetworkArray<RigTargetPose> BonePoses => default;
        [Networked] public NetworkBool PoseReady { get; set; }
        Transform[] bones;
        RigTargetPose[] captured;
        bool spawned, hasCapture;

        void Awake()
        {
            // 收集蒙皮骨頭和中間父節點，依階層排序；兩端必須使用同一 Prefab。
            var needed = new HashSet<Transform>();
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var bone in skin.bones)
                    for (var t = bone; t != null && t != transform && t.IsChildOf(transform); t = t.parent)
                        needed.Add(t);
            var ordered = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (needed.Contains(t)) ordered.Add(t);
            bones = ordered.ToArray();
            captured = new RigTargetPose[bones.Length];
            foreach (var ik in GetComponentsInChildren<BipedIK>(true))
            {
                AnchorFoot(ik.solvers.leftFoot, ik.references.leftFoot, "Rear Foot L Target");
                AnchorFoot(ik.solvers.rightFoot, ik.references.rightFoot, "Rear Foot R Target");
            }
        }

        void AnchorFoot(IKSolverLimb solver, Transform foot, string label)
        {
            if (solver.target != null || foot == null) return;
            var target = new GameObject(label).transform;
            target.SetParent(transform, false);
            target.SetPositionAndRotation(foot.position, foot.rotation);
            solver.target = target; // 後腳使用本隻羊的相對目標，不沿用舊世界座標。
            solver.IKPosition = target.position;
            solver.IKRotation = target.rotation;
        }

        public override void Spawned()
        {
            if (bones.Length == 0 || bones.Length > 64)
                throw new System.InvalidOperationException("NetworkSheepRig 支援 1～64 個骨架節點。");
            if (Playback == null) Playback = GetComponentInChildren<VRRecordingTargetPlayer>(true);
            bool server = Object.HasStateAuthority;
            if (Playback != null) Playback.enabled = server;
            foreach (var rig in GetComponentsInChildren<RigBuilder>(true)) rig.enabled = server;
            foreach (var ik in GetComponentsInChildren<RootMotion.SolverManager>(true)) ik.enabled = server;
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = server; // Client 不再讓動畫覆寫已同步的骨架。
            }
            spawned = true;
            hasCapture = false;
        }

        void LateUpdate()
        {
            if (!spawned || Object == null || !Object.IsValid) return;
            if (Object.HasStateAuthority)
            {
                for (int i = 0; i < bones.Length; i++)
                    captured[i] = new RigTargetPose { Position = bones[i].localPosition,
                        Rotation = bones[i].localRotation, Scale = bones[i].localScale };
                hasCapture = true;
            }
            else if (PoseReady)
                for (int i = 0; i < bones.Length; i++)
                {
                    var pose = BonePoses[i];
                    bones[i].SetLocalPositionAndRotation(pose.Position, pose.Rotation);
                    bones[i].localScale = pose.Scale;
                }
        }

        public override void FixedUpdateNetwork()
        {
            // 發布上一個畫面在 IK 完成後取樣的姿態。
            if (!Object.HasStateAuthority || !hasCapture) return;
            for (int i = 0; i < bones.Length; i++) BonePoses.Set(i, captured[i]);
            PoseReady = true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;
        public void PlayMotion(int index)
        {
            if (Object != null && Object.IsValid && Object.HasStateAuthority && Playback != null)
                Playback.Play(index);
        }
        [ContextMenu("Motion/Idle (Server)")] void Idle() => PlayMotion(0);
        [ContextMenu("Motion/Walk (Server)")] void Walk() => PlayMotion(1);
        [ContextMenu("Motion/Eat (Server)")] void Eat() => PlayMotion(2);
    }
}
