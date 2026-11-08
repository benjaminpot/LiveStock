using System;using System.Linq;using System.Threading.Tasks;using UnityEngine;
public static class VRMixedVisualDemo{
 public static async Task<object> Run(){
 LocalConnectionTestTools.StartServer();ServerConnectionManager server=null;var ready=DateTime.UtcNow.AddSeconds(60);while(server==null||server.State==null){server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();if(DateTime.UtcNow>ready)throw new Exception("Server ready");await Task.Delay(100);}
 foreach(var client in ConnectionManager.Clients)client.Connect();
 var end=DateTime.UtcNow.AddSeconds(60);while(server.State==null||!server.State.CanStart){if(DateTime.UtcNow>end)throw new Exception("Mixed Lobby");await Task.Delay(100);}
 server.StartGame();while(server.State.Phase!=LobbySessionState.SessionPhase.InMainGame){if(DateTime.UtcNow>end)throw new Exception("Mixed Game");await Task.Delay(100);}await Task.Delay(1500);
 var pc=ConnectionManager.Clients.Single(c=>c.IsDesktop);ConnectionTestViews.Instance.Select(pc);
 var camera=pc.GetComponentInChildren<DesktopTestPlayer>().ViewCamera;camera.transform.position=new Vector3(13,5,-7);camera.transform.rotation=Quaternion.LookRotation(new Vector3(13,3,-11)-camera.transform.position);
 var report=UnityEngine.Object.FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None).Select(a=>new{runner=a.Runner.name,root=a.transform.position.ToString(),maxDistance=a.GetComponentsInChildren<Renderer>().Max(r=>Vector3.Distance(r.bounds.center,a.transform.position)+r.bounds.extents.magnitude),targets=a.VisualTargets.IsConfigured}).ToArray();
 System.IO.File.WriteAllText("Docs/VR_Mixed_Visual_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
 }
}
