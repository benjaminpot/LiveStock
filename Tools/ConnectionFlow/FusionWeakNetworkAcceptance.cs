using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using LiveStock.Multiplayer;
using UnityEngine;

/// <summary>Checks pose convergence, expired-input rejection, fruit progress and lifecycle under SDK network conditions.</summary>
public static class FusionWeakNetworkAcceptance
{
    static async Task Wait(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(label);
            await Task.Delay(100);
        }
    }

    public static async Task<object> Run()
    {
        var config = NetworkProjectConfig.Global;
        var original = config.NetworkConditions;
        var results = new List<object>();
        var pc = ConnectionManager.Clients.Single();
        LocalConnectionTestTools.StartVRPreview();
        await Wait(() => ConnectionManager.Clients.Count == 2, "VR preview Menu");
        var vr = ConnectionManager.Clients.Single(c => !c.IsDesktop);
        var source = vr.GetComponent<VRClientPoseSource>();
        var cases = new[] {
            new {delay=0f, loss=0f, jitter=0f},
            new {delay=.05f, loss=.02f, jitter=.01f},
            new {delay=.1f, loss=.05f, jitter=.02f}
        };
        try
        {
            foreach (var scenario in cases)
            {
                var conditions = original.Clone();
                conditions.Enabled = scenario.delay > 0;
                conditions.DelayMin = conditions.DelayMax = scenario.delay;
                conditions.AdditionalJitter = scenario.jitter;
                conditions.LossChanceMin = conditions.LossChanceMax = scenario.loss;
                conditions.AdditionalLoss = 0;
                config.NetworkConditions = conditions;
                LocalConnectionTestTools.StartServer();
                ServerConnectionManager server = null;
                await Wait(() => {
                    server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
                    return server != null && server.State != null;
                }, "Server startup");
                if (!await pc.ConnectAsync(server.RoomName, server.Region)
                    || !await vr.ConnectAsync(server.RoomName, server.Region))
                    throw new InvalidOperationException("Client connection failed under simulated conditions.");
                await Wait(() => server.State.CanLoadMap && server.State.LoadedPlayers == 2, "Lobby readiness");
                source.RigRoot.position += new Vector3(.6f, .1f, .4f);
                source.RigRoot.rotation = Quaternion.Euler(0, 35, 0);
                source.HeadAnchor.localPosition += new Vector3(.03f, .04f, 0);
                source.LeftHandAnchor.localPosition += new Vector3(0, .02f, .03f);
                source.RightHandAnchor.localPosition += new Vector3(.02f, 0, .03f);
                var actor = pc.GetComponentInChildren<DesktopTestPlayer>();
                actor.transform.position += Vector3.right;
                var timer = Stopwatch.StartNew();
                float error = float.PositiveInfinity;
                await Wait(() => {
                    var sheep = UnityEngine.Object.FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None);
                    if (sheep.Length != 3 || sheep.Any(s => !s.HasPose)) return false;
                    var expected = source.ReadPose();
                    error = sheep.Max(s => Mathf.Max(Vector3.Distance(s.Pose.RootPosition, expected.RootPosition),
                        Vector3.Distance(s.Pose.Head, expected.Head), Vector3.Distance(s.Pose.LeftHand, expected.LeftHand),
                        Vector3.Distance(s.Pose.RightHand, expected.RightHand)));
                    return error < .01f && sheep.All(s => Quaternion.Angle(s.transform.rotation, expected.RootRotation) < 1f);
                }, "Static root and three-target convergence");
                double convergenceSeconds = timer.Elapsed.TotalSeconds;
                server.LoadMainGame();
                await Wait(()=>server.State.CanStart,"Ready to start gameplay");
                var scene=((NetworkSceneManagerDefault)server.Runner.SceneManager).MainRunnerScene;
                var spawner=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RandomNPCSpawner>()).Single();
                int minimum=Math.Min(spawner.MaxNetworkedNPCs,spawner.Groups.Where(g=>g.Prefab!=null).Sum(g=>g.MinCount));
                int maximum=Math.Min(spawner.MaxNetworkedNPCs,spawner.Groups.Where(g=>g.Prefab!=null).Sum(g=>g.MaxCount));
                server.StartGame();
                await Wait(() => server.State.Phase == LobbySessionState.SessionPhase.Running
                    && !spawner.IsSpawning && spawner.AliveCount>0 && spawner.AliveCount>=minimum && spawner.AliveCount<=maximum
                    && UnityEngine.Object.FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length == spawner.AliveCount*3,
                    "Configured map NPCs on each peer");
                int npcCount=spawner.AliveCount;
                var apple=UnityEngine.Object.FindObjectsByType<FruitPickup>(FindObjectsSortMode.None)
                    .First(a=>a.Runner==server.Runner);
                var center=apple.RayCollider.bounds.center;
                var camera=actor.ViewCamera;
                foreach(var offset in new[]{Vector3.back,Vector3.right,Vector3.forward,Vector3.left,Vector3.up})
                {
                    actor.transform.position=center+offset*.75f-Vector3.up;
                    camera.transform.LookAt(center);
                    if(apple.IsNearby(pc.GetComponent<PlayerGameplay>().ReadBody())
                        &&apple.HasClearRay(camera.ViewportPointToRay(new Vector3(.5f,.5f))))break;
                }
                await Wait(()=>server.State.World.TryGetPlayer(pc.Runner.LocalPlayer,out var avatar)
                    &&Vector3.Distance(avatar.transform.position,actor.transform.position)<.01f,"Body reaches Server under network conditions");
                pc.Runner.TryFindObject(apple.Object.Id,out var localApple);
                var ray=camera.ViewportPointToRay(new Vector3(.5f,.5f));
                pc.Runner.ProvideInput=false;
                try
                {
                    await Task.Delay(1500);
                    pc.State.World.RequestCollect(localApple.GetComponent<FruitPickup>(),ray);
                    await Task.Delay(1000);
                    if(server.State.World.CollectedFruitCount!=0)throw new InvalidOperationException("Expired input collected fruit under network conditions.");
                }
                finally{pc.Runner.ProvideInput=true;}
                await Task.Delay(1000);
                if(!pc.GetComponent<PlayerGameplay>().TryCollect(localApple.GetComponent<FruitPickup>(),ray))
                    throw new InvalidOperationException("Fresh input cannot request fruit under network conditions.");
                await Wait(()=>server.State.World.CollectedFruitCount==1&&pc.State.World.CollectedFruitCount==1
                    &&vr.State.World.CollectedFruitCount==1,"Shared fruit progress under network conditions");
                int accepted = server.State.World.AcceptedInputCount;
                var rtt = new List<double>();
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(1000);
                    if (!pc.IsConnected || !vr.IsConnected) throw new InvalidOperationException("Peer disconnected.");
                    rtt.Add(pc.Runner.GetPlayerRtt(pc.Runner.LocalPlayer) * 1000);
                    rtt.Add(vr.Runner.GetPlayerRtt(vr.Runner.LocalPlayer) * 1000);
                }
                bool pass = server.State.World.AcceptedInputCount > accepted
                    && UnityEngine.Object.FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length == npcCount*3;
                results.Add(new {pass, configuredPacketDelayMs=scenario.delay*1000, configuredLoss=scenario.loss,
                    configuredJitterMs=scenario.jitter*1000, observedAverageClientRttMs=rtt.Average(),
                    convergenceSeconds, maximumStaticPositionErrorMetres=error, npcCountPerRunner=npcCount,
                    expiredInputRejected=true,sharedFruitCount=pc.State.World.CollectedFruitCount,
                    scope="SDK simulation on all three Editor Runners. Static convergence and lifecycle checked. Visual smoothness requires observation."});
                File.WriteAllText("Docs/Fusion_Map_WeakNetwork_Report.json",
                    Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
                if (!pass) throw new InvalidOperationException("Input or NPC count failed under simulated conditions.");
                await server.StopServerAsync();
                await Wait(() => pc.Runner == null && vr.Runner == null && !pc.IsBusy && !vr.IsBusy, "Client cleanup");
                await Task.Delay(500);
            }
        }
        finally { config.NetworkConditions = original; }
        return new {passed=results.Count, restoredConditions=ReferenceEquals(config.NetworkConditions, original)};
    }
}
