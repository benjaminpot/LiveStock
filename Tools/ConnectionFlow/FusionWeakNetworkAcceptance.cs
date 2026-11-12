using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using LiveStock.Multiplayer;
using UnityEngine;

/// <summary>Checks static pose convergence and lifecycle under representative SDK network conditions.</summary>
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
                server.StartGame();
                await Wait(() => server.State.Phase == LobbySessionState.SessionPhase.Running
                    && UnityEngine.Object.FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length == 180,
                    "60 NPCs on each peer");
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
                    && UnityEngine.Object.FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length == 180;
                results.Add(new {pass, configuredPacketDelayMs=scenario.delay*1000, configuredLoss=scenario.loss,
                    configuredJitterMs=scenario.jitter*1000, observedAverageClientRttMs=rtt.Average(),
                    convergenceSeconds, maximumStaticPositionErrorMetres=error, npcCountPerRunner=60,
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
