using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static class FlowExternalClient
{
    static async Task Wait(Func<bool> predicate,string message)
    {
        var end=DateTime.UtcNow.AddSeconds(60);
        while(!predicate()){if(DateTime.UtcNow>end)throw new Exception(message);await Task.Delay(100);}
    }
    public static async Task<object> Join()
    {
        if(!Application.isPlaying)throw new Exception("Client-only Play Mode has not started.");
        var client=UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
        client.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="ConnectButton").onClick.Invoke();
        await Wait(()=>client.IsConnected && client.State!=null && client.State.PlayerCount==1 && client.State.LoadedPlayers==1,"Standalone server join/readiness failed");
        var report=new{connected=client.IsConnected,room=client.Runner.SessionInfo.Name,mode=client.Runner.Config.PeerMode.ToString(),phase=client.State.Phase.ToString(),players=client.State.PlayerCount,loaded=client.State.LoadedPlayers,localServerObjects=UnityEngine.Object.FindObjectsByType<ServerConnectionManager>(FindObjectsSortMode.None).Length,clientHasStateAuthority=client.State.Object.HasStateAuthority,clientCanStart=client.State.CanStart,stage=FlowSceneMarker.For(client.Runner)?.Stage};
        System.IO.File.WriteAllText("Docs/VR_Lobby_Standalone_Join_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
    public static async Task<object> VerifyGame()
    {
        var client=UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
        await Wait(()=>client.State!=null && client.State.Phase==LobbySessionState.SessionPhase.InMainGame && FlowSceneMarker.For(client.Runner)?.Stage==2,"Standalone server did not take client into MainGame");
        var report=new{connected=client.IsConnected,room=client.Runner.SessionInfo.Name,mode=client.Runner.Config.PeerMode.ToString(),phase=client.State.Phase.ToString(),players=client.State.PlayerCount,loaded=client.State.LoadedPlayers,stage=FlowSceneMarker.For(client.Runner)?.Stage,stateId=client.State.Object.Id.ToString()};
        System.IO.File.WriteAllText("Docs/VR_Lobby_Standalone_Game_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
