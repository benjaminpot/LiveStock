using UnityEngine;

/// <summary>Shared scene paths used by Server and Client builds.</summary>
[CreateAssetMenu(menuName = "LiveStock/Network Scene Settings")]
public sealed class NetworkSceneSettings : ScriptableObject
{
    [Tooltip("Enabled Lobby scene path shared by Server and Clients.")]
    public string LobbyScene;
    [Tooltip("Enabled gameplay scene path loaded before the Server starts gameplay.")]
    public string MainGameScene;
    [Tooltip("Local VR menu scene used when returning from a standalone session.")]
    public string MenuScene;
    [Tooltip("Dedicated Server entry scene.")]
    public string ServerScene;
    public static NetworkSceneSettings Current => Resources.Load<NetworkSceneSettings>("NetworkSceneSettings");
}
