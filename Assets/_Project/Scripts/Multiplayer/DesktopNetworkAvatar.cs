using Fusion;
using UnityEngine;

/// <summary>Server-owned replicated penguin. The owner uses its local prefab visual.</summary>
public sealed class DesktopNetworkAvatar : NetworkBehaviour
{
    public override void Spawned()
    {
        if (Object.InputAuthority == Runner.LocalPlayer)
            foreach (var visual in GetComponentsInChildren<Renderer>()) visual.enabled = false;
    }
}
