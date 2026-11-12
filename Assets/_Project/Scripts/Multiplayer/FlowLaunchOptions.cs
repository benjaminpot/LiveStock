using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

/// <summary>Chooses desktop/server startup without changing the shared scene build indexes.</summary>
public static class FlowLaunchOptions
{
    public static bool ServerOnly => Environment.GetCommandLineArgs().Contains("-flow-server");
    public static bool VRPreview => Environment.GetCommandLineArgs().Contains("-flow-vr-preview")
#if UNITY_EDITOR
        || UnityEditor.SessionState.GetBool("LiveStock.FlowVRPreview", false)
#endif
        ;
    public static bool DesktopClient => Environment.GetCommandLineArgs().Contains("-flow-desktop")
#if UNITY_EDITOR
        || UnityEditor.SessionState.GetBool("LiveStock.FlowDesktopTest", false)
#endif
        ;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ConfigureXR()
    {
        // A separate server/desktop process must not try to claim the headset.
        // In the Editor, only the explicit desktop test option suppresses XR for all local peers.
        if ((ServerOnly || DesktopClient || VRPreview) && XRGeneralSettings.Instance != null)
        {
#if UNITY_EDITOR
            XRGeneralSettings.Instance.InitManagerOnStart = false;
#else
            // InitManagerOnStart is read-only in Players. This process needs no XR loader.
            XRGeneralSettings.Instance.Manager = null;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void PrepareLocalRig()
    {
        if (!ServerOnly && !DesktopClient && !VRPreview)
        {
#if !UNITY_EDITOR
            // Test builds defer automatic XR startup so a server process never opens the headset.
            // A normal VR client starts the same configured loader explicitly before its rig wakes.
            var settings = XRGeneralSettings.Instance;
            if (settings != null && !settings.InitManagerOnStart && settings.Manager != null)
            {
                settings.Manager.InitializeLoaderSync();
                if (settings.Manager.activeLoader != null) settings.Manager.StartSubsystems();
            }
#endif
            return;
        }
        foreach (var rig in UnityEngine.Object.FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            rig.gameObject.SetActive(false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SelectEntry()
    {
        if (ServerOnly || DesktopClient || VRPreview) Application.targetFrameRate = 60;
        if (ServerOnly && SceneManager.GetActiveScene().path != NetworkSceneSettings.Current.ServerScene)
        {
            foreach (var client in UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(client.gameObject);
            SceneManager.LoadScene(NetworkSceneSettings.Current.ServerScene);
        }
    }
}
