using Fusion;
using UnityEngine;

/// <summary>Portable map entry point. The marker supplies the spawn point and this component owns map spawns.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FlowSceneMarker))]
public sealed class MapNetworkSetup : MonoBehaviour
{
    [SerializeField, Tooltip("Fusion prefabs generated relative to the player spawn when gameplay starts.")]
    NetworkWorld.SceneSpawn[] sceneObjects = System.Array.Empty<NetworkWorld.SceneSpawn>();

    /// <summary>Starts this map's NPCs and configured objects after the Server enters Running.</summary>
    public void StartGameplay(NetworkWorld world)
    {
        if (!world.Object.HasStateAuthority || world.Runner.IsSceneManagerBusy
            || LobbySessionState.For(world.Runner).Phase != LobbySessionState.SessionPhase.Running) return;
        LiveStock.Multiplayer.RandomNPCSpawner.SpawnFor(world.Runner);
        var spawn = GetComponent<FlowSceneMarker>().VRSpawnPoint;
        if (spawn == null) return;
        foreach (var item in sceneObjects)
            if (item.Prefab != null)
                world.SpawnObject(item.Prefab, spawn.position + spawn.rotation * item.Offset, spawn.rotation);
    }
}
