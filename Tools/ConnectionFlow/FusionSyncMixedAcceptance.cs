using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>Checks the original VR menu scene and mixed peers without requiring a headset.</summary>
public static class FusionSyncMixedAcceptance
{
    static readonly List<object> results = new();
    static string report;
    static void Check(string test, bool pass, object evidence = null)
    {
        results.Add(new { test, pass, evidence });
        System.IO.File.WriteAllText(report, Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new Exception(test);
    }
    static async Task Wait(Func<bool> condition, string label)
    {
        var end = DateTime.UtcNow.AddSeconds(45);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new Exception(label);
            await Task.Delay(100);
        }
    }
    static Canvas Menu(ConnectionManager client) => (Canvas)new SerializedObject(client).FindProperty("menuCanvas").objectReferenceValue;
    static VRNetworkAvatar[] Sheep() => UnityEngine.Object.FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None);
    static DesktopNetworkAvatar[] Penguins() => UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None);
    static async Task KeyF2()
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.F2));
        await Task.Delay(150);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        await Task.Delay(150);
    }
    static async Task<object> Run(bool vrFirst)
    {
        results.Clear();
        report = "Docs/Fusion_Map_" + (vrFirst ? "VRFirst" : "DesktopFirst") + "_Report.json";
        await Wait(() => ConnectionManager.Clients.Count == 1, "Initial disconnected Menu");
        ServerConnectionManager server = null;
        ConnectionManager pc = null;
        if (!vrFirst)
        {
            pc = ConnectionManager.Clients.Single();
            LocalConnectionTestTools.StartServer();
            await Wait(() => { server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>(); return server != null && server.State != null; }, "Server");
            pc.Connect();
            await Wait(() => pc.IsConnected && server.State.LoadedPlayers == 1, "PC connected before VR creation");
            LocalConnectionTestTools.StartVRPreview();
        }
        await Wait(() => ConnectionManager.Clients.Any(c => !c.IsDesktop), "VR scene loaded");
        var vr = ConnectionManager.Clients.Single(c => !c.IsDesktop);
        var source = vr.GetComponent<VRClientPoseSource>();
        var menu = Menu(vr);
        Check("VR entry loads the original Menu_VRClient scene", SceneManager.GetSceneByPath(ServerConnectionManager.SceneFolder + "Menu_VRClient.unity").isLoaded);
        Check("VR starts disconnected with the original world-space menu", !vr.IsConnected && vr.Runner == null && menu.gameObject.activeSelf && menu.renderMode == RenderMode.WorldSpace,
            new { scale = menu.transform.localScale.ToString("F4"), position = menu.transform.position.ToString() });
        Check("VR has no desktop player or desktop input module", !vr.IsDesktop && vr.GetComponentInChildren<DesktopTestPlayer>(true) == null && vr.transform.Find("Desktop Client Input") == null);
        var camera = vr.GetComponentsInChildren<Camera>(true).Single(c => c.gameObject.activeInHierarchy && c.enabled);
        Check("Device-free camera looks toward the 3D VR menu", Vector3.Dot(camera.transform.forward, (menu.transform.position - camera.transform.position).normalized) > .99f && camera.cullingMask != 0);
        int count = ConnectionManager.Clients.Count;
        LocalConnectionTestTools.StartVRPreview();
        await Task.Delay(200);
        Check("Opening VR again selects the existing client", ConnectionManager.Clients.Count == count && ConnectionTestViews.Instance.Selected == vr);
        source.enabled = false; // Simulate tracking being unavailable during connection.
        if (vrFirst)
        {
            vr.Connect();
            await Wait(() => !vr.IsBusy, "Missing server response");
            Check("ContextMenu Connect fails clearly without a server", !vr.IsConnected && vr.Status.Contains("GameNotFound"), vr.Status);
            LocalConnectionTestTools.StartServer();
            await Wait(() => { server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>(); return server != null && server.State != null; }, "Server");
        }
        else
            Check("Creating VR preserves the already connected PC", pc.IsConnected && pc.Runner.IsConnectedToServer && server.State.PlayerCount == 1);
        vr.Connect();
        await Wait(() => vr.IsConnected && server.State.LoadedPlayers == (vrFirst ? 1 : 2), "VR joins");
        if (vrFirst)
        {
            LocalConnectionTestTools.StartDesktopTest();
            await Wait(() => ConnectionManager.Clients.Count == 2, "PC offline menu");
            pc = ConnectionManager.Clients.Single(c => c.IsDesktop);
            Check("Opening Desktop leaves VR connected and PC disconnected", vr.IsConnected && !pc.IsConnected);
            pc.Connect();
        }
        await Wait(() => server.State.PlayerCount == 2 && server.State.LoadedPlayers == 2 && Sheep().Length == 3 && Penguins().Length == 3, "Mixed avatars");
        Check("VR sheep spawns without any tracking packets", Sheep().All(s => !s.HasPose) && vr.IsConnected && pc.IsConnected);
        Check("All three runners have one sheep and one penguin", Sheep().Select(s => s.Runner).Distinct().Count() == 3 && Penguins().Select(s => s.Runner).Distinct().Count() == 3);
        var inactiveCamera = vr.GetComponentsInChildren<Camera>(true).First(c => !c.gameObject.activeInHierarchy);
        inactiveCamera.enabled = false;
        ConnectionTestViews.Instance.Select(vr);
        await KeyF2(); await KeyF2(); await KeyF2();
        Check("F2 preserves deliberately disabled VR cameras", ConnectionTestViews.Instance.Selected == vr && !inactiveCamera.enabled && camera.enabled);
        source.enabled = true;
        await Wait(() => Sheep().All(s => s.HasPose), "Tracking resumes");
        source.HeadAnchor.localPosition += new Vector3(.06f, .04f, .03f);
        source.LeftHandAnchor.localPosition += new Vector3(-.1f, .04f, .02f);
        source.RightHandAnchor.localPosition += new Vector3(.1f, .03f, .04f);
        await Task.Delay(1200);
        var expected = source.ReadPose();
        float poseError = Sheep().Max(s => Mathf.Max(Vector3.Distance(s.Pose.Head, expected.Head), Vector3.Distance(s.Pose.LeftHand, expected.LeftHand), Vector3.Distance(s.Pose.RightHand, expected.RightHand)));
        Check("Head and both hands still replicate through Fusion", poseError < .01f, poseError);
        var actor = pc.GetComponentInChildren<DesktopTestPlayer>();
        actor.transform.position += Vector3.right * .6f;
        await Task.Delay(1000);
        float pcError = Penguins().Max(p => Vector3.Distance(p.transform.position, actor.transform.position));
        Check("PC penguin keeps syncing after VR joins", pcError < .1f && pc.Runner.IsConnectedToServer && vr.Runner.IsConnectedToServer, pcError);
        server.LoadMainGame();
        await Wait(() => server.State.Phase == LobbySessionState.SessionPhase.AwaitingStart && vr.State.Phase == LobbySessionState.SessionPhase.AwaitingStart && pc.State.Phase == LobbySessionState.SessionPhase.AwaitingStart, "Shared MainGame");
        Check("Mixed clients stay connected across MainGame", vr.IsConnected && pc.IsConnected && server.State.LoadedPlayers == 2 && Sheep().Length == 3 && Penguins().Length == 3);
        ConnectionTestViews.Instance.Select(vr);
        vr.ReturnToMenu();
        await Wait(() => vr.Status == "Ready to connect." && server.State.PlayerCount == 1 && Sheep().Length == 0, "Return to VR menu");
        Check("VR returns to its original 3D menu and environment", menu.gameObject.activeSelf && menu.renderMode == RenderMode.WorldSpace && camera.cullingMask != 0 && pc.IsConnected);
        Check("Returning restores the offline menu camera", Vector3.Dot(camera.transform.forward, (menu.transform.position - camera.transform.position).normalized) > .99f);
        await server.StopServerAsync();
        await Wait(() => !pc.IsConnected && pc.Runner == null, "Shutdown");
        Check("Server shutdown cleans remaining client", !pc.IsConnected && !vr.IsConnected);
        return new { passed = results.Count, vrFirst };
    }
    public static Task<object> VRFirst() => Run(true);
    public static Task<object> DesktopFirst() => Run(false);

}

