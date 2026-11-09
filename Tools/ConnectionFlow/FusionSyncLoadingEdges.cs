using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Exercises disconnects during loading so no departed player can hold the room indefinitely.</summary>
public static class FusionSyncLoadingEdges
{
    static async Task Wait(Func<bool> predicate, string label, int seconds=60)
    {
        var end=DateTime.UtcNow.AddSeconds(seconds);
        while(!predicate()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(100);}
    }
    public static async Task<object> Run()
    {
        var server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        var client=UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None).Single(c=>!c.name.StartsWith("Test Client"));
        if(server.State==null)await server.StartServerAsync();
        if(!client.IsConnected)await client.ConnectAsync(server.RoomName,server.Region);
        await Wait(()=>server.State?.CanLoadMap==true,"Primary player never ready");
        var delayed=await LocalConnectionTestTools.AddClientAsync(30);
        await Wait(()=>server.State.PlayerCount==2 && server.State.LoadedPlayers==1,"Delayed Lobby stage not observed");
        bool lobbyBlocked=!server.State.CanLoadMap;
        await delayed.DisconnectAsync();
        await Wait(()=>server.State.PlayerCount==1 && server.State.CanLoadMap,"Leaving during Lobby load blocked start");
        delayed.ReadyReportDelaySeconds=0;
        await delayed.ConnectAsync(server.RoomName,server.Region);
        await Wait(()=>server.State.PlayerCount==2 && server.State.CanLoadMap,"Second player rejoin failed");
        bool clientAuthorityBlocked=!client.State.CanLoadMap && !delayed.State.CanLoadMap;
        delayed.ReadyReportDelaySeconds=20;
        server.LoadMainGame();
        await Wait(()=>FlowSceneMarker.For(client.Runner)?.Stage==2 && server.State.Phase==LobbySessionState.SessionPhase.LoadingMainGame && server.State.LoadedPlayers==1,"MainGame loaded-player gate not observed",90);
        server.StartGame();
        bool formalStartBlocked=server.State.Phase==LobbySessionState.SessionPhase.LoadingMainGame;
        await delayed.DisconnectAsync();
        await Wait(()=>server.State.Phase==LobbySessionState.SessionPhase.AwaitingStart && server.State.PlayerCount==1 && server.State.LoadedPlayers==1,"Leaving during MainGame load held the session");
        var report=new{formalStartBlocked,readyWithoutNPCs=UnityEngine.Object.FindObjectsByType<LiveStock.Multiplayer.NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length==0,lobbyLoadingBlocksStart=lobbyBlocked,clientCannotStart=clientAuthorityBlocked,departedLobbyLoaderRemoved=true,departedMainGameLoaderRemoved=true,phase=server.State.Phase.ToString(),players=server.State.PlayerCount,loaded=server.State.LoadedPlayers};
        System.IO.File.WriteAllText("Docs/Fusion_Map_LoadingEdge_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        UnityEngine.Object.Destroy(delayed.gameObject);
        await server.StopServerAsync();client.ReturnToMenu();return report;
    }
}
