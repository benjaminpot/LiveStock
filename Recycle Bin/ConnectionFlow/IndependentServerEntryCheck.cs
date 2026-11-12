using System;using System.Linq;using System.Threading.Tasks;using UnityEngine;
public static class IndependentServerEntryCheck{
 public static async Task<object> Run(){
 var end=DateTime.UtcNow.AddSeconds(45);ServerConnectionManager server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
 if(server!=null&&server.State==null&&!server.IsBusy){server.RoomName="LIVESTOCK-SOLO-"+DateTime.UtcNow.Ticks;await server.StartServerAsync();}
 while(server==null||server.State==null){server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();if(DateTime.UtcNow>end)throw new Exception("Server-only startup failed");await Task.Delay(100);}
 bool alone=ConnectionManager.Clients.Count==0&&server.State.PlayerCount==0;
 LocalConnectionTestTools.StartDesktopTest();await Task.Delay(500);var client=ConnectionManager.Clients.Single();
 bool disconnected=!client.IsConnected&&server.State.PlayerCount==0;
 LocalConnectionTestTools.CloseSelected();await Task.Delay(500);
 bool closed=ConnectionManager.Clients.Count==0;
 var report=new{serverOnly=alone,newMenuDisconnected=disconnected,closeSelectedRemovedClient=closed};
 System.IO.File.WriteAllText("Docs/Connection_Test_Server_Entry_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
 if(!alone||!disconnected||!closed)throw new Exception("Independent Server entry failed");return report;
 }
}
