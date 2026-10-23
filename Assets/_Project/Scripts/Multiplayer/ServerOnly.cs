using System.Collections;
using System.Collections.Generic;
using Fusion;
using Unity.RenderStreaming;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    /// <summary>
    /// Root for objects that must exist only in the authoritative server scene.
    /// It owns Render Streaming startup so client scene clones never connect to signaling.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("LiveStock/Multiplayer/Server Only Root")]
    [DisallowMultipleComponent]
    public sealed class ServerOnly : MonoBehaviour
    {
        private static readonly HashSet<ServerOnly> ActiveRoots = new HashSet<ServerOnly>();

        private SignalingManager[] signalingManagers;
        private NetworkRunner runner;
        private bool registered;

        private void Awake()
        {
            PrepareSignalingManagers();
        }

        private void PrepareSignalingManagers()
        {
            if (signalingManagers != null)
                return;

            signalingManagers = GetComponentsInChildren<SignalingManager>(true);
            foreach (var manager in signalingManagers)
            {
                // All scene clones deserialize the same setting. Centralize startup here
                // before SignalingManager.Awake can create a WebSocket connection.
                manager.runOnAwake = false;
            }
        }

        private IEnumerator Start()
        {
            // Multi-Peer assigns the scene-to-runner mapping after object Awake.
            for (int frame = 0; frame < 300 && runner == null; frame++)
            {
                runner = NetworkRunner.GetRunnerForGameObject(gameObject);
                if (runner == null)
                    yield return null;
            }

            ActivateFor(runner);
        }

        public void ActivateFor(NetworkRunner target)
        {
            if (registered)
                return;

            PrepareSignalingManagers();
            runner = target;
            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                gameObject.SetActive(false);
                return;
            }

            registered = true;
            ActiveRoots.Add(this);
            SessionLauncher.RefreshRunnerVisibility();

            var managedSignaling = new HashSet<SignalingManager>();
            foreach (var switcher in GetComponentsInChildren<CCTVViewSwitcher>(true))
            {
                if (switcher.isActiveAndEnabled && switcher.ManagedSignalingManager != null)
                    managedSignaling.Add(switcher.ManagedSignalingManager);
            }

            foreach (var manager in signalingManagers)
            {
                if (manager != null && managedSignaling.Contains(manager) &&
                    manager.isActiveAndEnabled && !manager.Running)
                    manager.Run();
            }

            Debug.Log($"[ServerOnly] Enabled server-only hierarchy for runner '{runner.name}'.", this);
        }

        public static bool IsActiveFor(NetworkRunner target)
        {
            if (target == null)
                return false;
            foreach (var root in ActiveRoots)
                if (root != null && root.registered && root.runner == target && root.isActiveAndEnabled)
                    return true;
            return false;
        }

        private void OnDestroy()
        {
            if (!registered)
                return;

            registered = false;
            ActiveRoots.Remove(this);
            if (signalingManagers != null)
                foreach (var manager in signalingManagers)
                    if (manager != null && manager.Running)
                        manager.Stop();
            SessionLauncher.RefreshRunnerVisibility();
        }
    }
}
