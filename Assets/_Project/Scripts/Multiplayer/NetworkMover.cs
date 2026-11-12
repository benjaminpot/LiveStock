using Fusion;
using UnityEngine;

/// <summary>Learning example of a server-moved root replicated by NetworkTransform.</summary>
public sealed class NetworkMover : NetworkBehaviour
{
    [Networked] Vector3 Origin { get; set; }
    public override void Spawned()
    {
        if (Object.HasStateAuthority) Origin = transform.position;
    }
    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority) transform.position = Origin + Vector3.up * (Mathf.Sin((float)Runner.SimulationTime) * .3f);
    }
}
