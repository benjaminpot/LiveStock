using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>Exercises role registration, scene readiness and peer shutdown through the P1 entry points.</summary>
public static class NetworkWorldAcceptance
{
    static readonly List<object> checks = new();

    static void Check(string test, bool pass, object evidence = null)
    {
        checks.Add(new { test, pass, evidence });
        System.IO.File.WriteAllText("Docs/Fusion_Map_World_Report.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(checks, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new InvalidOperationException(test);
    }

    static async Task Wait(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(label);
            await Task.Delay(100);
        }
    }

    public static async Task<object> Run()
    {
        checks.Clear();
        var client = ConnectionManager.Clients.Single();
        var actor = client.GetComponentInChildren<DesktopTestPlayer>();
        actor.enabled = false;
        LocalConnectionTestTools.StartServer();
        ServerConnectionManager server = null;
        await Wait(() =>
        {
            server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
            return server != null && server.State != null;
        }, "Server startup");
        Check("Client connects through the shared session controller", await client.ConnectAsync(server.RoomName, server.Region));
        var world = server.State.World;
        await Wait(() => server.State.CanLoadMap && world.AvatarCount == 1, "Desktop avatar without any pose packets");
        Check("Desktop avatar exists before its pose sender runs", world.RegisteredPlayerCount == 1 && !actor.enabled);
        Check("Each peer owns one controller and one Runner", client.Runner.GetComponent<NetworkSessionController>() != null
            && server.Runner.GetComponent<NetworkSessionController>() != null
            && UnityEngine.Object.FindObjectsByType<NetworkSessionController>(FindObjectsSortMode.None).Length == 2);
        Check("World lookup is scoped to its Runner", NetworkWorld.For(server.Runner) == world
            && NetworkWorld.For(client.Runner) == client.State.World && client.State.World != world);
        var player = client.Runner.LocalPlayer;
        Check("Client cannot register authoritative avatars", !client.State.World.RegisterPlayer(player, true, 1));
        world.TryGetPlayer(player, out var avatar);
        Check("A registered player cannot change role", !world.RegisterPlayer(player, true, 1)
            && avatar.GetComponent<DesktopNetworkAvatar>() != null && world.AvatarCount == 1);
        Vector3 position = avatar.transform.position;
        var forged = new PlayerNetworkInput { IsVR = true, HasPose = true, Stage = 1, Pose = new VRTrackedPose
        {
            RootPosition = position + Vector3.right * 20,
            RootRotation = Quaternion.identity, HeadRotation = Quaternion.identity,
            LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity
        } };
        UnityEngine.Events.UnityAction<NetworkRunner, NetworkInput> inject = (runner, input) => input.Set(forged);
        var events = client.Runner.GetComponent<NetworkEvents>();
        events.OnInput.AddListener(inject);
        await Task.Delay(400);
        events.OnInput.RemoveListener(inject);
        Check("A Desktop sender's VR pose cannot move its avatar", Vector3.Distance(position, avatar.transform.position) < .001f);
        Check("Repeated Connect does not create another Runner", !await client.ConnectAsync(server.RoomName, server.Region)
            && UnityEngine.Object.FindObjectsByType<NetworkSessionController>(FindObjectsSortMode.None).Length == 2);

        var delayed = await LocalConnectionTestTools.AddClientAsync(30);
        await Wait(() => server.State.PlayerCount == 2 && server.State.LoadedPlayers == 1, "Delayed readiness gate");
        Check("Unready players block Start Game and have no registered avatar", !server.State.CanLoadMap
            && world.RegisteredPlayerCount == 1 && world.AvatarCount == 1);
        var controller = delayed.Runner.GetComponent<NetworkSessionController>();
        Task first = controller.ShutdownAsync();
        Task second = controller.ShutdownAsync();
        Check("Repeated shutdown shares the same operation", ReferenceEquals(first, second));
        await first;
        await Wait(() => server.State.CanLoadMap && server.State.PlayerCount == 1, "Departed loader removed");
        UnityEngine.Object.Destroy(delayed.gameObject);

        var oldRunner = client.Runner;
        await client.DisconnectAsync();
        await Wait(() => world.RegisteredPlayerCount == 0 && world.AvatarCount == 0, "Avatar cleanup");
        Check("Disconnect removes the registration and network avatar", !world.TryGetPlayer(player, out _));
        Check("Disconnected Client can reconnect with a fresh Runner", await client.ConnectAsync(server.RoomName, server.Region)
            && client.Runner != oldRunner);
        await Wait(() => server.State.CanLoadMap && world.AvatarCount == 1, "Reconnected avatar");
        server.LoadMainGame();
        await Wait(() => server.State.Phase == LobbySessionState.SessionPhase.AwaitingStart
            && client.State.Phase == LobbySessionState.SessionPhase.AwaitingStart, "MainGame readiness");
        Check("Scene loading keeps exactly one registered avatar", world.AvatarCount == 1 && world.RegisteredPlayerCount == 1);
        actor.enabled = true;
        await Task.Delay(500);
        world.TryGetPlayer(client.Runner.LocalPlayer, out var current);
        Check("Client-owned root updates resume after MainGame", Vector3.Distance(current.transform.position, actor.transform.position) < .01f);
        await server.StopServerAsync();
        await Wait(() => !client.IsConnected && client.Runner == null, "Server stop");
        await Task.Delay(200);
        Check("Shutdown leaves no Runner controllers", UnityEngine.Object.FindObjectsByType<NetworkSessionController>(FindObjectsSortMode.None).Length == 0);
        return new { passed = checks.Count };
    }
}
