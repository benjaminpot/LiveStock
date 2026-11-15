using Fusion;
using UnityEngine;

/// <summary>World-space body capsule used for the original fruit-trigger proximity rule.</summary>
public struct PlayerBodyPose : INetworkStruct
{
    public Vector3 Center, Axis;
    public float Radius, Height;
    public bool IsValid => Finite(Center) && Finite(Axis) && float.IsFinite(Radius) && float.IsFinite(Height)
        && Radius >= .05f && Radius <= 1f && Height >= Radius * 2f && Height <= 3.5f
        && Mathf.Abs(Axis.sqrMagnitude - 1f) < .01f;

    /// <summary>Reads the authored capsule, including crouching and world scale.</summary>
    public static PlayerBodyPose From(CapsuleCollider capsule)
    {
        var scale = capsule.transform.lossyScale;
        int direction = capsule.direction;
        float lengthScale = Mathf.Abs(scale[direction]);
        float radiusScale = Mathf.Max(Mathf.Abs(scale[(direction + 1) % 3]), Mathf.Abs(scale[(direction + 2) % 3]));
        var axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
        float radius = capsule.radius * radiusScale;
        return new PlayerBodyPose { Center = capsule.transform.TransformPoint(capsule.center),
            Axis = capsule.transform.TransformDirection(axis).normalized, Radius = radius,
            Height = Mathf.Max(capsule.height * lengthScale, radius * 2f) };
    }

    /// <summary>Checks the original sphere trigger without requiring cross-scene trigger callbacks.</summary>
    public bool Overlaps(SphereCollider trigger)
    {
        if (!IsValid || trigger == null || !trigger.enabled) return false;
        var center = trigger.transform.TransformPoint(trigger.center);
        var scale = trigger.transform.lossyScale;
        float radius = trigger.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        float halfSegment = Height * .5f - Radius;
        float offset = Mathf.Clamp(Vector3.Dot(center - Center, Axis), -halfSegment, halfSegment);
        return (center - (Center + Axis * offset)).sqrMagnitude <= (radius + Radius) * (radius + Radius);
    }

    public static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
