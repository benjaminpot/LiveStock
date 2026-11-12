using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Fusion;
public static class SimplePeerAcceptance
{
 static List<object> results=new List<object>();
 static void Check(string test,bool pass,object evidence=null){results.Add(new{test,pass,evidence});System.IO.File.WriteAllText("Docs/Connection_Test_Independent_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));if(!pass)throw new Exception(test);}
 static async Task Wait(Func<bool> condition,string label){var end=DateTime.UtcNow.AddSeconds(60);while(!condition()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(100);}}
 static async Task F2(){InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.F2));await Task.Delay(120);InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());await Task.Delay(120);}
 public static async Task<object> Run(){
 results.Clear();await Wait(()=>ConnectionManager.Clients.Count==1,"First menu");var first=ConnectionManager.Clients[0];
 Check("Desktop opens at disconnected Menu without a Server",!first.IsConnected&&first.IsDesktop&&UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>()==null);
 bool missing=await first.ConnectAsync("LIVESTOCK-DEV","asia");Check("Manual connect without Server fails",!missing&&first.Status.Contains("GameNotFound"),first.Status);
 LocalConnectionTestTools.StartServer();ServerConnectionManager server=null;await Wait(()=>{server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();return server!=null&&server.State!=null;},"Server start");
 Check("Starting Server leaves Client disconnected",!first.IsConnected&&server.State.PlayerCount==0);
 first.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="ConnectButton").onClick.Invoke();await Wait(()=>server.State.CanStart,"First joins");
 LocalConnectionTestTools.StartDesktopTest();await Wait(()=>ConnectionManager.Clients.Count==2,"Second menu");var second=ConnectionManager.Clients.Last();
 Check("Second Desktop opens disconnected with its own prefab",!second.IsConnected&&second.GetComponentInChildren<DesktopTestPlayer>()!=null&&server.State.PlayerCount==1);
 await F2();Check("F2 selects Server",ConnectionTestViews.Instance.Selected==server);
 await F2();Check("F2 selects first Client",ConnectionTestViews.Instance.Selected==first&&first.ViewSelected&&!second.ViewSelected);
 await F2();Check("F2 selects second Client",ConnectionTestViews.Instance.Selected==second&&second.ViewSelected&&!first.ViewSelected);
 second.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="ConnectButton").onClick.Invoke();await Wait(()=>server.State.PlayerCount==2&&server.State.LoadedPlayers==2,"Second joins");
 await Wait(()=>UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).Count()==6,"Two penguins per peer");
 Check("Server and both clients each receive two penguins",UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).GroupBy(a=>a.Runner).All(g=>g.Count()==2));
 var player=second.GetComponentInChildren<DesktopTestPlayer>();var stationary=first.GetComponentInChildren<DesktopTestPlayer>();var before=player.transform.position;var stationaryBefore=stationary.transform.position;
 InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.D));await Task.Delay(500);InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());await Task.Delay(500);
 var avatars=UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).Where(a=>a.Object.InputAuthority==second.Runner.LocalPlayer).ToArray();
 float error=avatars.Max(a=>Vector3.Distance(a.transform.position,player.transform.position));
 Check("Selected PC moves and replicated penguin matches on all peers",Vector3.Distance(before,player.transform.position)>.5f&&error<.1f,new{error,replicas=avatars.Length});
 Check("Penguin meshes remain attached to their player roots",UnityEngine.Object.FindObjectsByType<DesktopTestPlayer>(FindObjectsSortMode.None).All(p=>p.GetComponentsInChildren<Renderer>().All(r=>Vector3.Distance(r.bounds.center,p.transform.position)<2f)) && UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).All(p=>p.GetComponentsInChildren<Renderer>().All(r=>Vector3.Distance(r.bounds.center,p.transform.position)<2f)));
 Check("Deselected PC ignores shared keyboard",Vector3.Distance(stationaryBefore,stationary.transform.position)<.001f);
 var ids=avatars.Select(a=>a.Object.Id.ToString()).ToArray();ConnectionTestViews.Instance.Select(server);
 server.transform.root.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="StartGameButton").onClick.Invoke();
 await Wait(()=>server.State.Phase==LobbySessionState.SessionPhase.InMainGame&&first.State.Phase==LobbySessionState.SessionPhase.InMainGame&&second.State.Phase==LobbySessionState.SessionPhase.InMainGame,"MainGame");
 Check("Both players and penguins survive MainGame",server.State.LoadedPlayers==2&&UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).Count()==6);
 ConnectionTestViews.Instance.Select(second);second.ReturnToMenu();await Wait(()=>!second.IsConnected&&server.State.PlayerCount==1&&second.Status=="Ready to connect.","Disconnect");await Wait(()=>UnityEngine.Object.FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None).Count()==2,"Despawn departed penguin");
 Check("Disconnect returns only selected peer to Menu and removes its penguin",second.Status=="Ready to connect."&&first.IsConnected);
 await server.StopServerAsync();await Wait(()=>!first.IsConnected,"Server shutdown");Check("Stopping Server disconnects remaining Client",!first.IsConnected);
 return new{passed=results.Count};
 }
}
