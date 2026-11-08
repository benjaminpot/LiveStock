using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>Runs real online Server/Client acceptance checks inside the Editor's current local test.</summary>
public static class FlowAcceptance
{
    static readonly List<object> results = new List<object>();
    static void Check(string test, bool pass, object evidence)
    {
        results.Add(new { test, pass, evidence, utc = DateTime.UtcNow.ToString("O") });
        System.IO.File.WriteAllText("Docs/VR_Lobby_Acceptance_Report.json", Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new Exception("Acceptance failed: " + test);
    }
    static async Task Wait(Func<bool> predicate, string label, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException(label); await Task.Delay(100); }
    }
    static object Snapshot(ServerConnectionManager server, ConnectionManager client) => new
    {
        frame = Time.frameCount,
        serverRunning = server.Runner != null && server.Runner.IsRunning,
        phase = server.State != null ? server.State.Phase.ToString() : "none",
        players = server.State != null ? server.State.PlayerCount : -1,
        loaded = server.State != null ? server.State.LoadedPlayers : -1,
        canStart = server.State != null && server.State.CanStart,
        clientConnected = client.IsConnected,
        clientStatus = client.Status,
        clientStage = FlowSceneMarker.For(client.Runner)?.Stage ?? 0
    };

    public static async Task<object> Run()
    {
        results.Clear();
        if (!Application.isPlaying) throw new Exception("Start a desktop local test first.");
        var server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        var client = UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None).Single(c => !c.name.StartsWith("Test Client"));
        await Wait(() => server != null && server.State != null, "Server did not open Lobby");
        int frame = Time.frameCount; await Task.Delay(250);
        Check("Player loop advances", Time.frameCount > frame, new { before=frame, after=Time.frameCount });
        Check("T4 Server alone cannot start", server.State.PlayerCount == 0 && !server.State.CanStart, Snapshot(server,client));
        bool empty = await client.ConnectAsync("  ", server.Region);
        Check("T3 Empty room is rejected locally", !empty && client.Runner == null, client.Status);
        var button = client.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="ConnectButton");
        button.onClick.Invoke(); button.onClick.Invoke();
        await Wait(() => client.IsConnected && server.State.PlayerCount == 1 && server.State.LoadedPlayers == 1, "First client did not reach Lobby");
        Check("T1 Menu button joins real online Lobby", client.Runner.GameMode == GameMode.Client && FlowSceneMarker.For(client.Runner)?.Stage == 1, Snapshot(server,client));
        Check("T3 Repeated Connect creates one client Runner", UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Count(r=>r.IsRunning)==2, Snapshot(server,client));
        var delayed = await LocalConnectionTestTools.AddClientAsync(5);
        await Wait(() => server.State.PlayerCount == 2, "Second client did not join");
        Check("T10 Loading client blocks Start Game", server.State.LoadedPlayers < 2 && !server.State.CanStart, Snapshot(server,client));
        await Wait(() => server.State.CanStart && delayed.State != null && delayed.State.PlayerCount == 2, "Delayed client never became ready");
        Check("T9 Two real players ready", server.State.PlayerCount==2 && server.State.LoadedPlayers==2, Snapshot(server,client));
        await delayed.DisconnectAsync();
        await Wait(() => server.State.PlayerCount==1 && server.State.CanStart,"Leaving player did not update counts");
        Check("T5 Player departure updates count", server.State.LoadedPlayers==1,Snapshot(server,client));
        await delayed.ConnectAsync(server.RoomName,server.Region);
        await Wait(()=>server.State.PlayerCount==2 && server.State.CanStart,"Second player could not reconnect");
        var serverId = server.State.Object.Id;
        var start = server.GetComponentsInParent<Transform>().Last().GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="StartGameButton");
        start.onClick.Invoke(); start.onClick.Invoke();
        await Wait(()=>!server.Runner.SessionInfo.IsOpen,"Room did not close",10);
        Check("T7 Start closes room and ignores double click", server.State.Phase!=LobbySessionState.SessionPhase.Lobby,Snapshot(server,client));
        await Wait(()=>server.State!=null && server.State.Phase==LobbySessionState.SessionPhase.InMainGame && client.State?.Phase==LobbySessionState.SessionPhase.InMainGame && delayed.State?.Phase==LobbySessionState.SessionPhase.InMainGame,"All peers did not finish MainGame",90);
        Check("T6 All peers reach MainGame in same session", FlowSceneMarker.For(server.Runner)?.Stage==2 && FlowSceneMarker.For(client.Runner)?.Stage==2 && FlowSceneMarker.For(delayed.Runner)?.Stage==2 && server.State.Object.Id==serverId && client.State.Object.Id==serverId && delayed.State.Object.Id==serverId,Snapshot(server,client));
        Check("T7 Exactly one state per peer", UnityEngine.Object.FindObjectsByType<LobbySessionState>(FindObjectsSortMode.None).Length==3,Snapshot(server,client));
        var late = new GameObject("Test Client Late").AddComponent<ConnectionManager>();
        bool joinedLate = await late.ConnectAsync(server.RoomName,server.Region);
        Check("T12 Late join is rejected",!joinedLate && !late.IsConnected,late.Status);
        UnityEngine.Object.Destroy(late.gameObject);
        await delayed.DisconnectAsync();UnityEngine.Object.Destroy(delayed.gameObject);
        await server.StopServerAsync();
        await Wait(()=>!client.IsConnected,"Client did not notice server shutdown");
        Check("T8 Server shutdown disconnects client",client.Status.StartsWith("Disconnected:"),Snapshot(server,client));
        client.ReturnToMenu();
        await Wait(()=>client.GetComponentsInChildren<Canvas>(true).Any(c=>c.name=="LogInMenu" && c.gameObject.activeInHierarchy),"Return to Menu failed");
        Check("Return to Menu restores current menu",client.Runner==null,client.Status);
        bool missing = await client.ConnectAsync("LIVESTOCK-NO-ROOM-"+Guid.NewGuid().ToString("N").Substring(0,8),server.Region);
        Check("T2 Missing room fails without creating a room",!missing && client.Runner==null,client.Status);
        return new {passed=results.Count,results};
    }
}
