using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Exercises real browser video and mission snapshots against a visible Editor server world.</summary>
public static class CctvGameplayAcceptance
{
    static readonly List<object> checks=new();
    static void Save()=>File.WriteAllText("Docs/Fusion_CCTV_Editor_Acceptance_Report.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(checks,Newtonsoft.Json.Formatting.Indented));
    static void Check(string test,bool pass,object evidence=null)
    {
        checks.Add(new{test,pass,evidence});
        Save();
        if(!pass)throw new InvalidOperationException(test);
    }
    static async Task Wait(Func<bool> condition,string label)
    {
        var deadline=DateTime.UtcNow.AddSeconds(150);
        while(!condition())
        {
            if(DateTime.UtcNow>deadline)throw new TimeoutException(label);
            await Task.Delay(100);
        }
    }
    static bool BrowserFlag(string output,string flag)
    {
        if(!File.Exists(output))return false;
        JObject state;
        // Browser snapshots can be read while their writer is replacing the JSON bytes.
        try{state=JObject.Parse(File.ReadAllText(output));}
        catch(IOException){return false;}
        catch(Newtonsoft.Json.JsonException){return false;}
        if(state["errors"] is JArray errors&&errors.Count>0)throw new InvalidOperationException(errors.ToString());
        return (bool?)state[flag]??false;
    }

    /// <summary>Runs shared fruit collection while two real browsers decode the authoritative Editor world.</summary>
    public static async Task<object> Run(string output)
    {
        checks.Clear();
        Save();
        var first=ConnectionManager.Clients.Single();
        ConnectionManager second=null;
        ServerConnectionManager server=null;
        var renderedViews=new HashSet<int>();
        Action<ScriptableRenderContext,Camera> recordRender=(context,camera)=>
        {
            if(camera.name=="View 1")renderedViews.Add(camera.GetInstanceID());
        };
        RenderPipelineManager.endCameraRendering+=recordRender;
        try
        {
            LocalConnectionTestTools.StartServer();
            await Wait(()=>{server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();return server!=null&&server.State!=null;},"Server startup");
            Check("First desktop connects for rendered CCTV test",await first.ConnectAsync(server.RoomName,server.Region));
            second=await LocalConnectionTestTools.AddClientAsync(0);
            Check("Second desktop connects for rendered CCTV test",second!=null);
            await Wait(()=>server.State.CanLoadMap,"Lobby readiness");
            server.LoadMainGame();
            await Wait(()=>server.State.CanStart,"Map readiness");
            server.StartGame();
            await Wait(()=>server.State.Phase==LobbySessionState.SessionPhase.Running,"Gameplay starts");
            // The server's actual renderers must remain visible to its streaming cameras in Multi-Peer.
            ConnectionTestViews.Instance.Select(server);
            int frame=Time.frameCount;
            await Wait(()=>Time.frameCount>frame+2,"Rendered player loop advances");
            Check("Server world is selected and player loop advances",ConnectionTestViews.Instance.Selected==server);
            var scene=((NetworkSceneManagerDefault)server.Runner.SceneManager).MainRunnerScene;
            var switcher=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CCTVViewSwitcher>(true)).Single();
            var signaling=switcher.ManagedSignalingManager;
            Check("Server CCTV hierarchy and signaling are active",switcher.isActiveAndEnabled&&signaling!=null&&signaling.isActiveAndEnabled&&signaling.Running,
                new{switcherActive=switcher.isActiveAndEnabled,signalingActive=signaling!=null&&signaling.isActiveAndEnabled,
                    running=signaling!=null&&signaling.Running,serverOnly=LiveStock.Multiplayer.ServerOnly.IsActiveFor(server.Runner),
                    owner=NetworkRunner.GetRunnerForGameObject(switcher.gameObject)?.name,peerMode=server.Runner.Config.PeerMode.ToString()});
            FruitPickup[] apples=null;
            await Wait(()=>{
                apples=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FruitPickup>(true))
                    .Where(a=>a.Object!=null&&a.Object.IsValid&&a.Runner==server.Runner).OrderBy(a=>a.Object.Id.Raw).ToArray();
                return apples.Length==3;
            },"Three authoritative apples");
            File.WriteAllText(output+".connect","connect");
            await Wait(()=>BrowserFlag(output,"ready"),"Two browsers show non-black advancing CCTV");
            Check("Two real browsers receive non-black CCTV and initial mission snapshot",true);
            Check("Both natural CCTV render callbacks are observed",renderedViews.Count>=2,renderedViews.Count);
            var clients=new[]{first,second,first};
            for(int index=0;index<3;index++)
            {
                var client=clients[index];
                var actor=client.GetComponentInChildren<DesktopTestPlayer>(true);
                var gameplay=client.GetComponent<PlayerGameplay>();
                var apple=apples[index];
                var center=apple.RayCollider.bounds.center;
                foreach(var offset in new[]{Vector3.back,Vector3.right,Vector3.forward,Vector3.left,Vector3.up})
                {
                    actor.transform.position=center+offset*.75f-Vector3.up;
                    actor.ViewCamera.transform.LookAt(center);
                    if(apple.IsNearby(gameplay.ReadBody())&&apple.HasClearRay(actor.ViewCamera.ViewportPointToRay(new Vector3(.5f,.5f))))break;
                }
                await Wait(()=>server.State.World.TryGetPlayer(client.Runner.LocalPlayer,out var avatar)
                    &&Vector3.Distance(avatar.transform.position,actor.transform.position)<.01f,"Player body reaches server");
                await Task.Delay(300);
                client.Runner.TryFindObject(apple.Object.Id,out var target);
                Check("Shared gameplay request for apple "+(index+1),gameplay.TryCollect(target.GetComponent<FruitPickup>(),
                    actor.ViewCamera.ViewportPointToRay(new Vector3(.5f,.5f))));
                int expected=index+1;
                await Wait(()=>server.State.World.CollectedFruitCount==expected&&first.State.World.CollectedFruitCount==expected
                    &&second.State.World.CollectedFruitCount==expected,"Shared mission progress");
                Check("Both sheep-side clients share "+expected+"/3",true);
            }
            await Wait(()=>BrowserFlag(output,"reconnected"),"Browser completion and reconnection snapshot");
            Check("Both browsers complete and a reconnect retains completion and video",BrowserFlag(output,"pass"));
            return new{passed=checks.Count,scope="Editor Multi-Peer with visible server renderers and two isolated Chrome contexts; no physical HMD proof"};
        }
        catch(Exception exception)
        {
            checks.Add(new{test="CCTV integration exception",pass=false,evidence=exception.ToString()});
            Save();
            throw;
        }
        finally
        {
            RenderPipelineManager.endCameraRendering-=recordRender;
            if(second!=null)await second.DisconnectAsync();
            if(first!=null)await first.DisconnectAsync();
            if(server!=null)await server.StopServerAsync();
        }
    }
}
