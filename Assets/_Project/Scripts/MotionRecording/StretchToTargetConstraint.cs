using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace LiveStock.MotionRecording
{
    [Serializable]
    public struct StretchToTargetData : IAnimationJobData
    {
        public Transform root, mid, tip;
        [SyncSceneToStream] public Transform target;
        public Vector3 midRestPosition, tipRestPosition;

        public bool IsValid() => root != null && mid != null && tip != null && target != null &&
            mid.parent == root && tip.parent == mid &&
            midRestPosition.sqrMagnitude > 0.00000001f && tipRestPosition.sqrMagnitude > 0.00000001f;

        public void SetDefaultValues()
        {
            root = mid = tip = target = null;
            midRestPosition = tipRestPosition = Vector3.zero;
        }
    }

    public struct StretchToTargetJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle root, mid, tip;
        public ReadOnlyTransformHandle target;
        public Vector3 midRestPosition, tipRestPosition;
        public float upperLength, lowerLength;
        public FloatProperty jobWeight { get; set; }

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float weight = jobWeight.Get(stream);
            if (weight <= 0)
            {
                AnimationRuntimeUtils.PassThrough(stream, mid);
                AnimationRuntimeUtils.PassThrough(stream, tip);
                return;
            }
            // Read the current animation stream, after any body/head constraints, with no frame lag.
            float distance = Vector3.Distance(root.GetPosition(stream), target.GetPosition(stream));
            float upper = upperLength, lower = lowerLength;
            if (distance > upper + lower)
            {
                float ratio = (distance * 1.001f + 0.00001f) / (upper + lower);
                upper *= ratio;
                lower *= ratio;
            }
            // Very close targets also need a valid triangle. Extend only the shorter segment.
            if (Mathf.Abs(upper - lower) > distance)
                upper = lower = Mathf.Max(upper, lower);
            mid.SetLocalPosition(stream, midRestPosition * Mathf.Lerp(1, upper / upperLength, weight));
            tip.SetLocalPosition(stream, tipRestPosition * Mathf.Lerp(1, lower / lowerLength, weight));
        }
    }

    public sealed class StretchToTargetBinder : AnimationJobBinder<StretchToTargetJob, StretchToTargetData>
    {
        public override StretchToTargetJob Create(Animator animator, ref StretchToTargetData data, Component component) =>
            new StretchToTargetJob
            {
                root = ReadWriteTransformHandle.Bind(animator, data.root),
                mid = ReadWriteTransformHandle.Bind(animator, data.mid),
                tip = ReadWriteTransformHandle.Bind(animator, data.tip),
                target = ReadOnlyTransformHandle.Bind(animator, data.target),
                midRestPosition = data.midRestPosition,
                tipRestPosition = data.tipRestPosition,
                upperLength = data.root.TransformVector(data.midRestPosition).magnitude,
                lowerLength = data.mid.TransformVector(data.tipRestPosition).magnitude
            };

        public override void Destroy(StretchToTargetJob job) { }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Animation Rigging/Stretch To Target Constraint")]
    public sealed class StretchToTargetConstraint :
        RigConstraint<StretchToTargetJob, StretchToTargetData, StretchToTargetBinder> { }
}
