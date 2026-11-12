using Fusion;
using UnityEngine;

/// <summary>Learning example of a persistent state changed through the room's interaction gateway.</summary>
public sealed class NetworkSwitch : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(ApplyLook))] public NetworkBool IsOn { get; private set; }
    Renderer visual;
    MaterialPropertyBlock color;
    public override void Spawned()
    {
        visual = GetComponentInChildren<Renderer>();
        color = new MaterialPropertyBlock();
        ApplyLook();
    }

    [ContextMenu("Network/Request toggle")]
    public void RequestToggle() => NetworkWorld.For(Runner)?.RequestToggle(Object);

    /// <summary>Changes persistent state after NetworkWorld validates the request.</summary>
    public void ToggleAsServer()
    {
        if (Object.HasStateAuthority) IsOn = !IsOn;
    }

    void ApplyLook()
    {
        if (visual == null) return;
        color.SetColor("_BaseColor", IsOn ? Color.green : Color.gray);
        color.SetColor("_Color", IsOn ? Color.green : Color.gray);
        visual.SetPropertyBlock(color);
    }
}
