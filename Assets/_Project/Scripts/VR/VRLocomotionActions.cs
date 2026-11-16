using Oculus.Interaction.Locomotion;
using UnityEngine;

/// <summary>Keeps the existing locomotor's run/crouch actions held, with crouching taking priority.</summary>
public sealed class VRLocomotionActions : MonoBehaviour
{
    [SerializeField] private FirstPersonLocomotor locomotor;
    private bool running, crouching;
    public void SetRunning(bool held) { running = held; Apply(); }
    public void SetCrouching(bool held) { crouching = held; Apply(); }
    void Apply() { locomotor.Crouch(crouching); locomotor.Run(running && !crouching); }
    void OnDisable() { running = crouching = false; if (locomotor != null) Apply(); }
}
