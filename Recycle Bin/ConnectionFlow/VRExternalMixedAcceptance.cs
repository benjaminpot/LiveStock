using System;using System.Linq;using System.Threading.Tasks;using UnityEngine;
public static class VRExternalMixedAcceptance
{
 static async Task Wait(Func<bool> predicate,string label){var end=DateTime.UtcNow.AddSeconds(90);while(!predicate()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(100);}}
 static VRNetworkAvatar[] Sheep()=>UnityEngine.Object.FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None);
 public static async Task<object> JoinAndMove()
 {
  await Wait(()=>ConnectionManager.Clients.Count==2,"Mixed menus");var vr=ConnectionManager.Clients.Single(c=>!c.IsDesktop);var pc=ConnectionManager.Clients.Single(c=>c.IsDesktop);
  vr.Connect();pc.Connect();await Wait(()=>vr.IsConnected&&pc.IsConnected&&vr.State!=null&&vr.State.LoadedPlayers==2&&Sheep().Length==2&&Sheep().All(s=>s.HasPose),"Standalone mixed Lobby");
  var source=vr.GetComponent<VRClientPoseSource>();source.HeadAnchor.localPosition+=new Vector3(.1f,.04f,0);source.LeftHandAnchor.localPosition+=new Vector3(0,.07f,.03f);source.RightHandAnchor.localPosition+=new Vector3(.05f,0,.1f);
  var actor=pc.GetComponentInChildren<DesktopTestPlayer>();actor.transform.position+=Vector3.right*.6f;ConnectionTestViews.Instance.Select(vr);await Task.Delay(1500);
  var pose=source.ReadPose();float error=Sheep().Max(s=>Vector3.Distance(s.Pose.Head,pose.Head));
  var penguins=UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None);float pcError=penguins.Max(p=>Vector3.Distance(p.transform.position,actor.transform.position));
  var report=new{vrConnected=vr.IsConnected,pcConnected=pc.IsConnected,vrRole=!vr.IsDesktop,preview=vr.IsVRPreview,players=vr.State.PlayerCount,loaded=vr.State.LoadedPlayers,localServers=UnityEngine.Object.FindObjectsByType<ServerConnectionManager>(FindObjectsSortMode.None).Length,clientAuthority=vr.State.Object.HasStateAuthority,head=new[]{pose.Head.x,pose.Head.y,pose.Head.z},left=new[]{pose.LeftHand.x,pose.LeftHand.y,pose.LeftHand.z},right=new[]{pose.RightHand.x,pose.RightHand.y,pose.RightHand.z},headError=error,penguinError=pcError};
  System.IO.File.WriteAllText("Docs/VR_External_Mixed_Lobby_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
  if(error>.01f||pcError>.1f||report.localServers!=0||report.clientAuthority||report.players!=2)throw new Exception("Standalone mixed pose mismatch");return report;
 }
 public static async Task<object> VerifyGame()
 {
  var vr=ConnectionManager.Clients.Single(c=>!c.IsDesktop);var pc=ConnectionManager.Clients.Single(c=>c.IsDesktop);
  await Wait(()=>vr.State!=null&&vr.State.Phase==LobbySessionState.SessionPhase.InMainGame&&pc.State.Phase==LobbySessionState.SessionPhase.InMainGame,"Standalone mixed MainGame");await Task.Delay(1000);
  var source=vr.GetComponent<VRClientPoseSource>();float rootError=Sheep().Max(s=>Vector3.Distance(s.transform.position,source.RigRoot.position));
  var report=new{phase=vr.State.Phase.ToString(),players=vr.State.PlayerCount,loaded=vr.State.LoadedPlayers,vrConnected=vr.IsConnected,pcConnected=pc.IsConnected,sheepReplicas=Sheep().Length,rootError};System.IO.File.WriteAllText("Docs/VR_External_Mixed_Game_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));if(rootError>.1f||report.sheepReplicas!=2||!report.pcConnected||!report.vrConnected)throw new Exception("Standalone mixed game failed");return report;
 }
 public static async Task<object> VerifyDisconnect()
 {
  await Wait(()=>ConnectionManager.Clients.All(c=>!c.IsConnected&&c.Runner==null),"Standalone server shutdown");var report=new{disconnected=true};System.IO.File.WriteAllText("Docs/VR_External_Mixed_Disconnect_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
 }
}
