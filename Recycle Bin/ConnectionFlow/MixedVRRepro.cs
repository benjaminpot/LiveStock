using System;using System.Linq;using System.Threading.Tasks;using UnityEngine;
public static class MixedVRRepro{
 static async Task Wait(Func<bool> p,string label){var end=DateTime.UtcNow.AddSeconds(45);while(!p()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(100);}}
 public static async Task<object> Run(){
 await Wait(()=>ConnectionManager.Clients.Count==1,"Desktop menu");var pc=ConnectionManager.Clients.Single();
 LocalConnectionTestTools.StartServer();ServerConnectionManager server=null;await Wait(()=>{server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();return server!=null&&server.State!=null;},"Server");
 pc.Connect();await Wait(()=>pc.IsConnected&&server.State.CanStart,"Desktop connect");
 LocalConnectionTestTools.StartVRTest();await Wait(()=>ConnectionManager.Clients.Any(c=>!c.IsDesktop),"VR menu");var vr=ConnectionManager.Clients.Single(c=>!c.IsDesktop);
 vr.Connect();await Wait(()=>vr.IsConnected&&server.State.PlayerCount==2&&server.State.LoadedPlayers==2,"VR connect/readiness");
 ConnectionTestViews.Instance.Select(pc);var actor=pc.GetComponentInChildren<DesktopTestPlayer>();actor.transform.position+=Vector3.right*2;await Task.Delay(1200);
 var replicas=UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None);var rig=vr.GetComponentInChildren<OVRCameraRig>(true);
 var report=new{vrConnected=vr.IsConnected,pcConnected=pc.IsConnected,players=server.State.PlayerCount,loaded=server.State.LoadedPlayers,vrHasState=vr.State!=null,pcReplicaCount=replicas.Length,replicas=replicas.Select(a=>new{runner=a.Runner.name,enabled=a.enabled,renderers=a.GetComponentsInChildren<Renderer>().Select(r=>r.enabled).ToArray(),error=Vector3.Distance(a.transform.position,actor.transform.position)}).ToArray(),vrRig=rig!=null,vrScripts=vr.GetComponentsInChildren<MonoBehaviour>(true).Where(b=>b!=null&&b.GetType().Namespace!=null&&b.GetType().Namespace.Contains("LiveStock")).Select(b=>b.GetType().Name).Distinct().ToArray()};
 System.IO.File.WriteAllText("Docs/VR_Mixed_Reproduction.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
 }
}
