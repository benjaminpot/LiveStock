using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Exercises PC input and two real Fusion clients through the existing local test entry.</summary>
public static class FlowDesktopControls
{
    static readonly List<object> results = new List<object>();

    static void Check(string test, bool pass, object evidence)
    {
        results.Add(new { test, pass, evidence });
        System.IO.File.WriteAllText("Docs/VR_Lobby_PC_Controls_Report.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new Exception(test);
    }

    static async Task Wait(Func<bool> predicate, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(message);
            await Task.Delay(100);
        }
    }

    static async Task<float> Move(DesktopTestPlayer player, params Key[] keys)
    {
        var start = player.transform.position;
        try
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
            await Task.Delay(400);
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            await Task.Delay(100);
        }
        return Vector3.Distance(start, player.transform.position);
    }

    public static async Task<object> Run()
    {
        results.Clear();
        var server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        var client = UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
        var player = client.GetComponentInChildren<DesktopTestPlayer>(true);
        await Wait(() => server.State != null, "Server Lobby did not open");
        Check("PC view created with one listener and no active XR rig", player != null
            && UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled) == 1
            && !UnityEngine.Object.FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None).Any(r => r.isActiveAndEnabled), player != null);
        float menuDistance = await Move(player, Key.W);
        Check("Menu ignores movement", !player.CanControl && menuDistance < 0.001f, menuDistance);
        client.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == "ConnectButton").onClick.Invoke();
        await Wait(() => server.State.CanStart && player.CanControl, "PC Client did not enter Lobby");

        float walk = await Move(player, Key.W);
        Check("W moves the loaded PC view", walk > 0.3f, walk);
        float fast = await Move(player, Key.W, Key.LeftShift);
        Check("Shift increases movement speed", fast > walk * 1.4f, new { walk, fast });
        float height = player.transform.position.y;
        await Move(player, Key.E);
        Check("E raises the PC view", player.transform.position.y > height + 0.3f, player.transform.position.y - height);
        height = player.transform.position.y;
        await Move(player, Key.Q);
        Check("Q lowers the PC view", player.transform.position.y < height - 0.3f, player.transform.position.y - height);

        Quaternion before = player.transform.rotation;
        try
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { buttons = 2, delta = new Vector2(80, -20) });
            await Task.Delay(100);
        }
        finally
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            await Task.Delay(100);
        }
        float angle = Quaternion.Angle(before, player.transform.rotation);
        Check("Right mouse rotates the PC view", angle > 1f, angle);

        var second = await LocalConnectionTestTools.AddClientAsync(0);
        await Wait(() => server.State.PlayerCount == 2 && server.State.LoadedPlayers == 2 && second.State != null,
            "Second real client did not reach Lobby");
        Check("Two clients share one room and authoritative state", second.IsConnected
            && client.State.Object.Id == second.State.Object.Id && !second.State.Object.HasStateAuthority,
            new { server.State.PlayerCount, server.State.LoadedPlayers, room = client.Runner.SessionInfo.Name });
        Check("Extra test client does not create a controllable camera",
            second.GetComponentInChildren<DesktopTestPlayer>(true) == null, second.name);
        var runner = client.Runner;
        var stateId = client.State.Object.Id;
        server.transform.root.GetComponentsInChildren<UnityEngine.UI.Button>(true)
            .Single(b => b.name == "StartGameButton").onClick.Invoke();
        await Wait(() => server.State.Phase == LobbySessionState.SessionPhase.InMainGame
            && client.State.Phase == LobbySessionState.SessionPhase.InMainGame
            && second.State.Phase == LobbySessionState.SessionPhase.InMainGame && player.CanControl,
            "Both clients did not reach MainGame");
        float gameDistance = await Move(player, Key.D);
        Check("PC control survives the shared MainGame transition", gameDistance > 0.3f
            && client.Runner == runner && client.State.Object.Id == stateId,
            new { distance = gameDistance, stage = FlowSceneMarker.For(client.Runner).Stage, server.State.PlayerCount });
        await second.DisconnectAsync();
        UnityEngine.Object.Destroy(second.gameObject);
        await Wait(() => server.State.PlayerCount == 1, "Leaving PC did not update the player count");
        await client.DisconnectAsync();
        float disconnectedDistance = await Move(player, Key.W);
        Check("Disconnected PC view ignores input", !player.CanControl && disconnectedDistance < 0.001f, disconnectedDistance);
        client.ReturnToMenu();
        await Task.Delay(200);
        Check("Return to Menu restores ready status", client.Status == "Ready to connect." && !player.CanControl, client.Status);
        await server.StopServerAsync();
        return new { passed = results.Count, total = results.Count };
    }
}
