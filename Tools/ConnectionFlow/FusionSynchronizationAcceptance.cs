using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Statistics;
using LiveStock.Multiplayer;
using LiveStock.MotionRecording;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>End-to-end input, target, NPC and extension checks. Start from one disconnected Desktop test menu.</summary>
public static class FusionSynchronizationAcceptance
{
    static readonly List<object> checks = new();
    static NetworkNpcAuthorityGate[] Npcs() => UnityEngine.Object.FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None);
    static void Check(string test, bool pass, object evidence = null)
    {
        checks.Add(new { test, pass, evidence });
        System.IO.File.WriteAllText("Docs/Fusion_Map_Acceptance_Report.json", Newtonsoft.Json.JsonConvert.SerializeObject(checks, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new InvalidOperationException(test);
    }
    static async Task Wait(Func<bool> predicate, string label, int seconds = 120)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException(label);
            await Task.Delay(100);
        }
    }
    static float PoseError(ThreeTargetPose a, ThreeTargetPose b) => Mathf.Max(Vector3.Distance(a.Head,b.Head),Vector3.Distance(a.Left,b.Left),Vector3.Distance(a.Right,b.Right));

    public static async Task<object> Run()
    {
        checks.Clear();
        var pc = ConnectionManager.Clients.Single();
        LocalConnectionTestTools.StartServer();
        ServerConnectionManager server = null;
        await Wait(() => { server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();return server!=null&&server.State!=null; }, "Server");
        await pc.ConnectAsync(server.RoomName,server.Region);
        LocalConnectionTestTools.StartVRPreview();
        await Wait(() => ConnectionManager.Clients.Count==2,"VR menu");
        var vr=ConnectionManager.Clients.Single(c=>!c.IsDesktop);
        await vr.ConnectAsync(server.RoomName,server.Region);
        await Wait(() => server.State.CanLoadMap,"Lobby ready");
        Check("Lobby contains no generated NPCs",Npcs().Length==0);
        Check("Each Client provides its own tick input",pc.Runner.ProvideInput&&vr.Runner.ProvideInput&&!server.Runner.ProvideInput
            &&pc.Runner.GetComponent<NetworkInputSource>()!=vr.Runner.GetComponent<NetworkInputSource>());
        await Wait(() => server.State.World.AcceptedInputCount>10,"Server consumes input");
        Check("Server consumes continuous Fusion input",server.State.World.AcceptedInputCount>10);
        Check("Lobby cannot formally start gameplay",!server.State.CanStart);
        server.StartGame();
        Check("Start Game in Lobby leaves phase unchanged",server.State.Phase==LobbySessionState.SessionPhase.Lobby);
        var buttons=new SerializedObject(server);
        var load=(UnityEngine.UI.Button)buttons.FindProperty("loadMapButton").objectReferenceValue;
        var start=(UnityEngine.UI.Button)buttons.FindProperty("startGameButton").objectReferenceValue;
        Check("Server has two distinct wired buttons",load!=null&&start!=null&&load!=start&&load.onClick.GetPersistentMethodName(0)==nameof(ServerConnectionManager.LoadMainGame)&&start.onClick.GetPersistentMethodName(0)==nameof(ServerConnectionManager.StartGame));
        load.onClick.Invoke();
        await Wait(()=>server.State.CanStart&&pc.State.Phase==LobbySessionState.SessionPhase.AwaitingStart&&vr.State.Phase==LobbySessionState.SessionPhase.AwaitingStart,"Map ready without gameplay");
        Check("All Runners use configured gameplay scene",new[]{server.Runner,pc.Runner,vr.Runner}.All(r=>r.GetSceneRef(FlowSceneMarker.For(r).gameObject)==r.GetSceneRef(NetworkSceneSettings.Current.MainGameScene)),NetworkSceneSettings.Current.MainGameScene);
        await Task.Delay(2000);
        Check("Ready map waits without NPCs or demo objects",Npcs().Length==0&&UnityEngine.Object.FindObjectsByType<NetworkSwitch>(FindObjectsSortMode.None).Length==0);
        Check("Each Runner has portable map setup",UnityEngine.Object.FindObjectsByType<MapNetworkSetup>(FindObjectsSortMode.None).Length==3);
        var actorBeforeStart=pc.GetComponentInChildren<DesktopTestPlayer>();
        actorBeforeStart.transform.position+=Vector3.right*.5f;
        await Task.Delay(700);
        server.State.World.TryGetPlayer(pc.Runner.LocalPlayer,out var beforeAvatar);
        Check("Client movement already synchronizes while awaiting start",Vector3.Distance(beforeAvatar.transform.position,actorBeforeStart.transform.position)<.01f);
        Check("Load is disabled and Start is enabled after readiness",!load.interactable&&start.interactable&&!pc.State.CanStart);
        var mapScene=((NetworkSceneManagerDefault)server.Runner.SceneManager).MainRunnerScene;
        var spawner=mapScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RandomNPCSpawner>()).Single();
        int minimumNpcs=Math.Min(spawner.MaxNetworkedNPCs,spawner.Groups.Where(g=>g.Prefab!=null).Sum(g=>g.MinCount));
        int maximumNpcs=Math.Min(spawner.MaxNetworkedNPCs,spawner.Groups.Where(g=>g.Prefab!=null).Sum(g=>g.MaxCount));
        start.onClick.Invoke();
        start.onClick.Invoke();
        await Wait(() => server.State.Phase==LobbySessionState.SessionPhase.Running && !spawner.IsSpawning
            && spawner.AliveCount>=minimumNpcs && Npcs().Length==spawner.AliveCount*3,"Configured NPCs on three Runners");
        int npcCount=spawner.AliveCount;
        Check("Server generation respects authored map count",npcCount>0&&npcCount>=minimumNpcs&&npcCount<=maximumNpcs,
            new{minimumNpcs,maximumNpcs,generated=npcCount});
        await Wait(() => Npcs().All(n=>n.GetComponent<NetworkTargetPose>().HasPose),"NPC target snapshots");
        var npcs=Npcs();
        Check("Every Runner receives the configured NPC identities",npcs.GroupBy(n=>n.Runner).Count()==3&&npcs.GroupBy(n=>n.Runner).All(g=>g.Count()==npcCount),
            npcs.GroupBy(n=>n.Runner).Select(g=>new{runner=g.Key.name,count=g.Count()}).ToArray());
        Check("AI and navigation run only on the Server",npcs.All(n=>n.GetComponent<AnimalNPC>().enabled==n.Object.HasStateAuthority
            &&n.GetComponent<NavMeshAgent>().enabled==n.Object.HasStateAuthority));
        Check("Remote recordings cannot overwrite network targets",npcs.Where(n=>!n.Object.HasStateAuthority).All(n=>!n.GetComponentInChildren<VRRecordingTargetPlayer>().enabled));
        var authorities=npcs.Where(n=>n.Runner==server.Runner).ToArray();
        Check("All generated Agents start on baked navigation",authorities.All(n=>n.GetComponent<NavMeshAgent>().isOnNavMesh));
        Check("Waypoints belong to the Server scene",authorities.All(n=>n.GetComponent<AnimalNPC>().waypoints.All(w=>NetworkRunner.GetRunnerForGameObject(w.gameObject)==server.Runner)));
        var selected=authorities[0];
        var old=authorities.ToDictionary(n=>n.Object.Id,n=>n.transform.position);
        await Task.Delay(2500);
        Check("NPC roots move through the existing AI",authorities.Count(n=>Vector3.Distance(n.transform.position,old[n.Object.Id])>.1f)>0);
        var animal=selected.GetComponent<AnimalNPC>();var agent=selected.GetComponent<NavMeshAgent>();
        var playback=selected.GetComponentInChildren<VRRecordingTargetPlayer>();
        var motion=selected.GetComponent<AnimalMotionController>();
        animal.enabled=false;motion.enabled=false;agent.isStopped=true;playback.Pause();
        await Task.Delay(1500);
        var peers=Npcs().Where(n=>n.Object.Id==selected.Object.Id).ToArray();
        var expected=selected.GetComponent<NetworkTargetPose>().State;
        float targetError=peers.Max(n=>PoseError(expected,n.GetComponent<NetworkTargetPose>().State));
        Check("Root-relative target state converges on every peer",targetError<.001f,targetError);
        float applied=peers.Max(n=>Vector3.Distance(n.transform.InverseTransformPoint(n.GetComponent<NetworkTargetPose>().HeadTarget.position),expected.Head));
        Check("Remote IK receives the transmitted head target",applied<.01f,applied);
        var sourceSkin=selected.GetComponentInChildren<SkinnedMeshRenderer>();
        float boneError=peers.Max(n=>{
            var skin=n.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.sharedMesh==sourceSkin.sharedMesh);
            return skin.bones.Select((b,i)=>Vector3.Distance(n.transform.InverseTransformPoint(b.position),selected.transform.InverseTransformPoint(sourceSkin.bones[i].position))).Max();
        });
        Check("The complete skinned pose agrees after targets settle",boneError<.03f,boneError);
        animal.enabled=true;motion.enabled=true;agent.isStopped=false;playback.Resume();
        Check("Only authority accepts damage",peers.Where(n=>n.Runner!=server.Runner).All(n=>!n.TryApplyDamage(1))&&selected.TryApplyDamage(1));
        await Wait(()=>peers.All(n=>n.Health==0),"Death state");
        Check("Death state replicates",peers.All(n=>n.Health==0));
        await Wait(()=>peers.All(n=>n.Health>0),"Respawn");
        Check("Respawn restores authority and life state",animal.enabled&&agent.enabled&&peers.All(n=>n.Health>0));

        var switches=UnityEngine.Object.FindObjectsByType<NetworkSwitch>(FindObjectsSortMode.None);
        var toggle=switches.Single(s=>s.Runner==pc.Runner);
        var actor=pc.GetComponentInChildren<DesktopTestPlayer>();
        actor.transform.position=toggle.transform.position+Vector3.back*.5f;
        await Task.Delay(700);
        toggle.RequestToggle();
        await Wait(()=>switches.All(s=>s.IsOn),"Persistent switch");
        Check("Central interaction updates persistent state",switches.All(s=>s.IsOn));
        var boxes=UnityEngine.Object.FindObjectsByType<NetworkMover>(FindObjectsSortMode.None);
        float before=boxes.First().transform.position.y;await Task.Delay(700);
        Check("The root-only example moves on all peers",boxes.Length==3&&Mathf.Abs(boxes.First().transform.position.y-before)>.001f);
        server.Runner.SessionInfo.IsOpen=true;
        var late=await LocalConnectionTestTools.AddClientAsync(0);
        await Wait(()=>late.State!=null&&late.State.Phase==LobbySessionState.SessionPhase.Running&&Npcs().Length==npcCount*4,"Late join state");
        await Wait(()=>UnityEngine.Object.FindObjectsByType<NetworkSwitch>(FindObjectsSortMode.None).All(s=>s.IsOn),"Late join switch");
        Check("Late join receives the current switch and map NPCs",Npcs().Count(n=>n.Runner==late.Runner)==npcCount);
        await late.DisconnectAsync();UnityEngine.Object.Destroy(late.gameObject);server.Runner.SessionInfo.IsOpen=false;
        await Wait(()=>Npcs().Length==npcCount*3,"Late peer removed");
        RandomNPCSpawner.SpawnFor(server.Runner);await Task.Delay(500);
        Check("Repeated spawn requests keep the configured NPC count",spawner.AliveCount==npcCount&&Npcs().Length==npcCount*3);
        Check("Input remains active during NPC simulation",vr.IsConnected&&pc.IsConnected&&server.State.World.AcceptedInputCount>100);
        return new {passed=checks.Count,server=server.Runner.name,npcs=npcCount,readyForSoak=true};
    }

    /// <summary>Measures the shared Editor process while existing connected peers simulate NPCs.</summary>
    public static async Task<object> Soak(int seconds=600,int count=60)
    {
        var server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        var runner=server.Runner;
        var scene=((NetworkSceneManagerDefault)runner.SceneManager).MainRunnerScene;
        var spawner=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RandomNPCSpawner>()).Single();
        if(spawner.AliveCount!=count)
        {
            spawner.MaxNetworkedNPCs=count;spawner.Groups[0].MinCount=count;spawner.Groups[0].MaxCount=count;spawner.RespawnGenerated();
        }
        await Wait(()=>spawner.AliveCount==count&&!spawner.IsSpawning,"Stress generation");
        var frames=new List<float>();var incoming=new double[3];var outgoing=new double[3];
        var runners=new[]{runner}.Concat(ConnectionManager.Clients.Where(c=>c.IsConnected).Select(c=>c.Runner)).ToArray();
        int lastFrame=-1;int startTick=runner.Tick.Raw;long startMemory=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        var errors=new List<string>();int minNpcs=count;
        Application.LogCallback log=(message,stack,type)=>{if(type==UnityEngine.LogType.Exception||type==UnityEngine.LogType.Error)errors.Add(message);};
        EditorApplication.CallbackFunction sample=()=>{
            if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frames.Add(Time.unscaledDeltaTime*1000f);
            for(int i=0;i<runners.Length;i++)if(runners[i]!=null&&runners[i].TryGetFusionStatistics(out var stats))
            {incoming[i]+=stats.SimulationSnapshot.Stats.GetValueOrDefault(FusionStatType.InBandwidth,0);outgoing[i]+=stats.SimulationSnapshot.Stats.GetValueOrDefault(FusionStatType.OutBandwidth,0);}
        };
        var timer=Stopwatch.StartNew();Application.logMessageReceived+=log;EditorApplication.update+=sample;
        try
        {
            while(timer.Elapsed.TotalSeconds<seconds)
            {
                await Task.Delay(1000);minNpcs=Math.Min(minNpcs,spawner.AliveCount);
                System.IO.File.WriteAllText(".tmp/FusionSoakProgress.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{count,seconds=timer.Elapsed.TotalSeconds,frames=frames.Count,minNpcs,errors=errors.Count}));
                if(!runners.All(r=>r!=null&&r.IsRunning))throw new InvalidOperationException("Peer disconnected during soak.");
            }
        }
        finally{EditorApplication.update-=sample;Application.logMessageReceived-=log;}
        var ordered=frames.OrderBy(f=>f).ToArray();
        var result=new{npcCount=count,seconds=timer.Elapsed.TotalSeconds,frames=frames.Count,averageFrameMs=frames.Average(),p95FrameMs=ordered[(int)(ordered.Length*.95f)],
            observedServerTicksPerSecond=(runner.Tick.Raw-startTick)/timer.Elapsed.TotalSeconds,minNpcs,peerCount=runners.Length,
            allocatedMemoryDeltaBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()-startMemory,
            sampledTraffic=runners.Select((r,i)=>new{runner=r.name,inBytesPerSecond=incoming[i]/timer.Elapsed.TotalSeconds,outBytesPerSecond=outgoing[i]/timer.Elapsed.TotalSeconds}).ToArray(),
            errors=errors.Distinct().ToArray(),scope="All peers share one Editor; frame and memory values include the Editor and all visible/hidden scene copies."};
        System.IO.File.WriteAllText("Docs/Fusion_Map_Soak_"+count+"_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        if(minNpcs!=count||errors.Count>0)throw new InvalidOperationException("Soak failed. Read the report.");
        return result;
    }
}
