using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
public static class VRMixedAcceptance
{
 static List<object> results = new List<object>();
 static string reportPath = "Docs/VR_Mixed_Acceptance_Report.json";
 static void Check(string test, bool pass, object evidence=null)
 {
  results.Add(new{test,pass,evidence});System.IO.File.WriteAllText(reportPath,Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
  if(!pass)throw new Exception(test);
 }
 static async Task Wait(Func<bool> condition,string label)
 {
  var end=DateTime.UtcNow.AddSeconds(60);while(!condition()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(100);}
 }
 static Transform Target(VRNetworkAvatar avatar,string name)=>(Transform)new UnityEditor.SerializedObject(avatar.VisualTargets).FindProperty(name).objectReferenceValue;
 static VRNetworkAvatar[] Sheep()=>UnityEngine.Object.FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None);
 static DesktopNetworkAvatar[] Penguins()=>UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None);
 static async Task<object> Run(bool vrFirst)
 {
  await Wait(()=>ConnectionManager.Clients.Count==1,"Initial menu");
  if(vrFirst)LocalConnectionTestTools.StartDesktopTest();else LocalConnectionTestTools.StartVRPreview();
  await Wait(()=>ConnectionManager.Clients.Count==2,"Both disconnected menus");
  var vr=ConnectionManager.Clients.Single(c=>!c.IsDesktop);var pc=ConnectionManager.Clients.Single(c=>c.IsDesktop);
  var source=vr.GetComponent<VRClientPoseSource>();
  string prefix=vrFirst?"VR-first ":"Desktop-first ";
  Check(prefix+"VR role has calibrated source and explicit device-free mode",vr.IsVRPreview&&source.IsConfigured&&!vr.IsDesktop&&!source.TrackingRig.gameObject.activeInHierarchy);
  vr.Connect();await Wait(()=>!vr.IsBusy,"Missing room response");
  Check(prefix+"ContextMenu Connect reports missing Server",!vr.IsConnected&&vr.Status.Contains("GameNotFound"),vr.Status);
  LocalConnectionTestTools.StartServer();ServerConnectionManager server=null;
  await Wait(()=>{server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();return server!=null&&server.State!=null;},"Server opened");
  var first=vrFirst?vr:pc;var second=vrFirst?pc:vr;
  first.Connect();await Wait(()=>first.IsConnected&&server.State.LoadedPlayers==1,"First connected");
  second.Connect();await Wait(()=>second.IsConnected&&server.State.LoadedPlayers==2,"Second connected");
  Check(prefix+"Real Server and both Clients stay connected",server.State.PlayerCount==2&&pc.Runner.IsConnectedToServer&&vr.Runner.IsConnectedToServer,new{players=server.State.PlayerCount,loaded=server.State.LoadedPlayers});
  await Wait(()=>Sheep().Length==3&&Sheep().All(s=>s.HasPose)&&Penguins().Length==3,"VR sheep and Desktop penguin replicas");
  Check(prefix+"One sheep and one penguin per Runner",Sheep().Select(s=>s.Runner).Distinct().Count()==3&&Penguins().Select(s=>s.Runner).Distinct().Count()==3);
  Check(prefix+"Remote sheep is free of cameras and XR managers",Sheep().All(s=>s.GetComponentsInChildren<Camera>(true).Length==0&&s.GetComponentsInChildren<OVRManager>(true).Length==0));
  var serverSheep=Sheep().Single(s=>s.Runner==server.Runner);
  var bones=serverSheep.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(s=>s.bones).Distinct().ToArray();
  var boneRotations=bones.Select(b=>b.localRotation).ToArray();
  source.HeadAnchor.localPosition+=new Vector3(.07f,.03f,.04f);source.HeadAnchor.localRotation=Quaternion.Euler(5,12,0);
  source.LeftHandAnchor.localPosition+=new Vector3(-.12f,.08f,.05f);source.LeftHandAnchor.localRotation=Quaternion.Euler(0,-12,8);
  source.RightHandAnchor.localPosition+=new Vector3(.1f,-.04f,.08f);source.RightHandAnchor.localRotation=Quaternion.Euler(8,10,0);
  await Task.Delay(1500);
  var expected=source.ReadPose();
  float head=Sheep().Max(s=>Vector3.Distance(s.Pose.Head,expected.Head));
  float left=Sheep().Max(s=>Vector3.Distance(s.Pose.LeftHand,expected.LeftHand));
  float right=Sheep().Max(s=>Vector3.Distance(s.Pose.RightHand,expected.RightHand));
  Check(prefix+"Tracked head and both hands reach all network replicas",Mathf.Max(head,left,right)<.005f,new{head,left,right});
  float targets=Sheep().Max(s=>Vector3.Distance(Target(s,"headTarget").position,s.transform.TransformPoint(expected.Head)));
  Check(prefix+"Replicated poses drive existing sheep IK targets",targets<.02f,targets);
  float boneChange=bones.Select((b,i)=>Quaternion.Angle(b.localRotation,boneRotations[i])).Max();
  Check(prefix+"Sheep skeleton responds after target movement",boneChange>.1f,boneChange);
  ConnectionTestViews.Instance.Select(vr);
  var actor=pc.GetComponentInChildren<DesktopTestPlayer>();actor.transform.position+=Vector3.right*.7f;
  await Task.Delay(1000);float penguinError=Penguins().Max(p=>Vector3.Distance(p.transform.position,actor.transform.position));
  Check(prefix+"Desktop penguin keeps syncing while VR view is selected",penguinError<.1f&&server.State.PlayerCount==2,penguinError);
  var sheepId=serverSheep.Object.Id;server.StartGame();
  await Wait(()=>server.State.Phase==LobbySessionState.SessionPhase.InMainGame&&vr.State.Phase==LobbySessionState.SessionPhase.InMainGame&&pc.State.Phase==LobbySessionState.SessionPhase.InMainGame,"Mixed MainGame");
  await Task.Delay(1000);
  Check(prefix+"Same sheep and mixed players survive shared scene change",Sheep().Length==3&&Sheep().All(s=>s.Object.Id==sheepId)&&server.State.LoadedPlayers==2&&pc.IsConnected&&vr.IsConnected);
  float visualBounds=Sheep().Max(a=>a.GetComponentsInChildren<Renderer>().Max(r=>Vector3.Distance(r.bounds.center,a.transform.position)+r.bounds.extents.magnitude));
  Check(prefix+"Sheep mesh remains aligned with its network root after loading",visualBounds<5f,visualBounds);
  expected=source.ReadPose();float rootError=Sheep().Max(s=>Vector3.Distance(s.transform.position,expected.RootPosition));
  Check(prefix+"VR root follows the loaded scene spawn",rootError<.05f,rootError);
  vr.ReturnToMenu();await Wait(()=>vr.Status=="Ready to connect."&&server.State.PlayerCount==1&&Sheep().Length==0,"VR leave cleanup");
  Check(prefix+"VR exit removes only its sheep and preserves Desktop",pc.IsConnected&&Penguins().Length==2);
  vr.Connect();await Wait(()=>!vr.IsBusy,"Late join response");Check(prefix+"VR ContextMenu rejects closed game",!vr.IsConnected&&vr.Status.Contains("GameClosed"),vr.Status);
  await server.StopServerAsync();await Wait(()=>!pc.IsConnected&&pc.Runner==null,"Desktop shutdown cleanup");
  Check(prefix+"Actual disconnect clears Desktop connection state",!pc.IsConnected);
  return new{passed=results.Count,vrFirst};
 }
 public static async Task<object> DesktopFirst(){results.Clear();reportPath="Docs/VR_Mixed_DesktopFirst_Report.json";return await Run(false);}
 public static async Task<object> VRFirst(){results.Clear();reportPath="Docs/VR_Mixed_VRFirst_Report.json";return await Run(true);}
}
