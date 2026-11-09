using Fusion;
using UnityEngine;
using System.Collections.Generic;

/// <summary>Identifies the stage and local VR standing point inside one Runner's scene.</summary>
public sealed class FlowSceneMarker : MonoBehaviour
{
    [Tooltip("1 = Lobby, 2 = MainGame. This is a local scene marker, not network authority.")]
    public int Stage = 1;
    public Transform VRSpawnPoint;
    static readonly List<FlowSceneMarker> instances = new();
    void Awake() => instances.Add(this);
    void OnDestroy() => instances.Remove(this);

    /// <summary>Finds only the marker belonging to this Runner, including Multi-Peer scene copies.</summary>
    public static FlowSceneMarker For(NetworkRunner runner)
    {
        if (runner == null || runner.SceneManager is not NetworkSceneManagerDefault manager) return null;
        var scene = manager.MainRunnerScene;
        if (!scene.IsValid() || !scene.isLoaded) return null;
        // Input and room checks run every frame/tick. Search registered markers rather than the whole farm hierarchy.
        foreach (var marker in instances)
            if (marker != null && marker.gameObject.scene == scene
                && (runner.Config.PeerMode != NetworkProjectConfig.PeerModes.Multiple
                    || NetworkRunner.GetRunnerForGameObject(marker.gameObject) == runner)) return marker;
        return null;
    }
}
