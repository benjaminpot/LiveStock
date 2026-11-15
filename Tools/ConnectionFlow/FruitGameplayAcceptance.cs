using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Headset-free integration checks using real Server/Client inputs, RPCs and physics.</summary>
public static class FruitGameplayAcceptance
{
    static readonly List<object> checks = new();
    static void Check(string test, bool pass, object evidence = null)
    {
        checks.Add(new { test, pass, evidence });
        System.IO.File.WriteAllText("Docs/Fruit_Gameplay_Acceptance_Report.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(checks, Newtonsoft.Json.Formatting.Indented));
        if (!pass) throw new InvalidOperationException(test);
    }
    static async Task Wait(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(label); await Task.Delay(100); }
    }
    static FruitPickup[] Apples(NetworkRunner runner) => ((NetworkSceneManagerDefault)runner.SceneManager).MainRunnerScene
        .GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FruitPickup>(true))
        .Where(a => a.Object != null && a.Object.IsValid && a.Runner == runner).OrderBy(a => a.Object.Id.Raw).ToArray();
    static FruitPickup Replica(NetworkRunner runner, FruitPickup serverApple)
        => runner.TryFindObject(serverApple.Object.Id, out var obj) ? obj.GetComponent<FruitPickup>() : null;

    static async Task<Ray> Approach(ConnectionManager client, NetworkWorld world, FruitPickup apple, float distance)
    {
        Vector3 center = apple.RayCollider.bounds.center;
        Ray ray = default;
        foreach (var offset in new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left, Vector3.up })
        {
            ray = new Ray(center + offset * distance, -offset);
            if (apple.HasClearRay(ray)) break;
        }
        var actor = client.GetComponentInChildren<DesktopTestPlayer>(true);
        actor.transform.position = ray.origin - Vector3.up;
        await Wait(() => world.TryGetPlayer(client.Runner.LocalPlayer, out var avatar)
            && Vector3.Distance(avatar.transform.position, actor.transform.position) < .01f, "Body pose replication");
        await Task.Delay(150);
        return ray;
    }

    public static async Task<object> Run()
    {
        checks.Clear();
        var first = ConnectionManager.Clients.Single();
        LocalConnectionTestTools.StartServer();
        ServerConnectionManager server = null;
        await Wait(() => { server = UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>(); return server != null && server.State != null; }, "Server startup");
        Check("First test Client connects", await first.ConnectAsync(server.RoomName, server.Region));
        var second = await LocalConnectionTestTools.AddClientAsync(0);
        Check("Second test Client connects", second != null);
        first.SetViewSelected(false);
        second.SetViewSelected(false);
        await Wait(() => server.State.CanLoadMap, "Lobby ready");
        server.LoadMainGame();
        await Wait(() => server.State.CanStart && first.State.Phase == LobbySessionState.SessionPhase.AwaitingStart
            && second.State.Phase == LobbySessionState.SessionPhase.AwaitingStart, "Map ready");
        Check("No apples before Start Game", Apples(server.Runner).Length == 0);
        Check("AwaitingStart blocks local collect", !first.GetComponent<PlayerGameplay>().CanInteract);
        var scene = ((NetworkSceneManagerDefault)server.Runner.SceneManager).MainRunnerScene;
        foreach (var spawner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LiveStock.Multiplayer.RandomNPCSpawner>(true)))
            spawner.Groups = Array.Empty<LiveStock.Multiplayer.RandomNPCSpawner.NPCGroup>();
        server.StartGame();
        await Wait(() => Apples(server.Runner).Length == 3 && Apples(first.Runner).Length == 3 && Apples(second.Runner).Length == 3, "Three replicated apples");
        var world = server.State.World;
        var apples = Apples(server.Runner);
        Check("Three apples have SDK ray surfaces", apples.All(a => a.GetComponent<RayInteractable>() != null));
        Check("Client cannot mutate collection state", !Replica(first.Runner, apples[0]).CollectAsServer());

        var ray = await Approach(first, world, apples[0], 4f);
        Check("Far body's original trigger rule rejects proximity", !apples[0].IsNearby(first.GetComponent<PlayerGameplay>().ReadBody()));
        first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
        await Task.Delay(500);
        Check("Server rejects an out-of-range RPC", world.CollectedFruitCount == 0 && !apples[0].IsCollected);
        ray = await Approach(first, world, apples[0], .75f);
        Check("Nearby body overlaps original trigger", apples[0].IsNearby(first.GetComponent<PlayerGameplay>().ReadBody()));
        Check("Small apple surface is ray-visible", apples[0].HasClearRay(ray));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.transform.position = ray.GetPoint(.35f);
        wall.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        await Task.Delay(200);
        Check("Solid obstacle blocks the apple", !apples[0].HasClearRay(ray));
        first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
        await Task.Delay(400);
        Check("Server rejects an occluded RPC", world.CollectedFruitCount == 0);
        UnityEngine.Object.Destroy(wall);
        await Task.Delay(200);
        var source = first.Runner.GetComponent<NetworkInputSource>();
        Check("Input cache is valid before disabling the source", source.Current.HasPose && source.Current.Body.IsValid);
        source.enabled = false;
        try
        {
            await Task.Delay(500);
            first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
            await Task.Delay(500);
            Check("Server rejects collection while the input source is disabled", world.CollectedFruitCount == 0 && !apples[0].IsCollected);
        }
        finally { source.enabled = true; }
        await Task.Delay(500);
        var cachedField = typeof(NetworkInputSource).GetField("cached", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var validInput = source.Current;
        var invalidInputs = new Dictionary<string, PlayerNetworkInput>();
        var invalid = validInput;
        invalid.HasPose = false;
        invalidInputs.Add("missing pose", invalid);
        invalid = validInput;
        invalid.Stage = 0;
        invalidInputs.Add("wrong scene stage", invalid);
        invalid = validInput;
        invalid.IsVR = true;
        invalidInputs.Add("wrong player role", invalid);
        invalid = validInput;
        invalid.Pose.RootPosition.x = float.NaN;
        invalidInputs.Add("non-finite pose", invalid);
        invalid = validInput;
        invalid.Body.Radius = float.NaN;
        invalidInputs.Add("invalid body capsule", invalid);
        foreach (var sample in invalidInputs)
        {
            source.enabled = false;
            try
            {
                cachedField.SetValue(source, sample.Value);
                await Task.Delay(400);
                first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
                await Task.Delay(400);
                Check("Server rejects collection with " + sample.Key, world.CollectedFruitCount == 0 && !apples[0].IsCollected);
            }
            finally { source.enabled = true; }
            await Task.Delay(400);
        }
        first.Runner.ProvideInput = false;
        try
        {
            await Task.Delay(1200);
            first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
            await Task.Delay(500);
            Check("Server expires body when client input stops", world.CollectedFruitCount == 0 && !apples[0].IsCollected);
        }
        finally { first.Runner.ProvideInput = true; }
        await Task.Delay(500);
        var camera = first.GetComponentInChildren<DesktopTestPlayer>(true).ViewCamera;
        camera.transform.LookAt(apples[0].RayCollider.bounds.center);
        var cameraRay = camera.ViewportPointToRay(new Vector3(.5f, .5f));
        Check("Authored third-person camera sends valid collection", first.GetComponent<PlayerGameplay>().TryCollect(Replica(first.Runner, apples[0]), cameraRay));
        await Wait(() => world.CollectedFruitCount == 1 && first.State.World.CollectedFruitCount == 1 && second.State.World.CollectedFruitCount == 1, "Shared 1/3");
        await Wait(() => Replica(first.Runner, apples[0]).IsCollected && Replica(second.Runner, apples[0]).IsCollected
            && !Replica(second.Runner, apples[0]).RayCollider.enabled, "Apple collection presentation");
        Check("Collected apple hides on both clients", Replica(first.Runner, apples[0]).IsCollected && Replica(second.Runner, apples[0]).IsCollected
            && !Replica(second.Runner, apples[0]).RayCollider.enabled);
        first.State.World.RequestCollect(Replica(first.Runner, apples[0]), ray);
        await Task.Delay(300);
        Check("Duplicate collect does not increment", world.CollectedFruitCount == 1);

        ray = await Approach(second, world, apples[1], .75f);
        second.State.World.RequestCollect(Replica(second.Runner, apples[1]), ray);
        await Wait(() => first.State.World.CollectedFruitCount == 2 && second.State.World.CollectedFruitCount == 2, "Shared 2/3");
        Check("Different players contribute to the same progress", world.CollectedFruitCount == 2);
        var ray1 = await Approach(first, world, apples[2], .75f);
        var ray2 = await Approach(second, world, apples[2], .75f);
        first.State.World.RequestCollect(Replica(first.Runner, apples[2]), ray1);
        second.State.World.RequestCollect(Replica(second.Runner, apples[2]), ray2);
        await Wait(() => first.State.World.MissionComplete && second.State.World.MissionComplete, "Completion broadcast");
        Check("Competing requests stop at exactly 3/3", world.CollectedFruitCount == 3 && world.RequiredFruitCount == 3);
        await Task.Delay(150);
        Check("Every sheep-side UI receives completion", first.GetComponent<PlayerGameplay>().Feedback == "蘋果已收集完成！"
            && second.GetComponent<PlayerGameplay>().Feedback == "蘋果已收集完成！");
        var text = new UnityEditor.SerializedObject(first.GetComponent<PlayerGameplay>()).FindProperty("stateText").objectReferenceValue as TMP_Text;
        Check("Existing Player_UI/state is wired", text != null && text.name == "state" && text.text == "蘋果已收集完成！");
        uint[] missingGlyphs;
        Check("Status font contains the Chinese feedback", text.font.HasCharacters(text.text, out missingGlyphs, false, false));
        await second.DisconnectAsync();
        Check("One player leaving retains shared progress", world.CollectedFruitCount == 3);
        await first.DisconnectAsync();
        await server.StopServerAsync();
        Check("Server can start a fresh round", await server.StartServerAsync());
        Check("Existing client can reconnect", await first.ConnectAsync(server.RoomName, server.Region));
        await Wait(() => server.State.CanLoadMap, "New round lobby");
        Check("New room has no old fruit progress", server.State.World.CollectedFruitCount == 0 && !server.State.World.MissionComplete);
        server.LoadMainGame();
        await Wait(() => server.State.CanStart, "New map ready");
        scene = ((NetworkSceneManagerDefault)server.Runner.SceneManager).MainRunnerScene;
        foreach (var spawner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LiveStock.Multiplayer.RandomNPCSpawner>(true)))
            spawner.Groups = Array.Empty<LiveStock.Multiplayer.RandomNPCSpawner.NPCGroup>();
        server.StartGame();
        await Wait(() => Apples(first.Runner).Length == 3, "Fresh apples");
        Check("New map resets all apples and progress", Apples(first.Runner).All(a => !a.IsCollected) && first.State.World.CollectedFruitCount == 0);
        await first.DisconnectAsync();
        await server.StopServerAsync();
        return new { passed = checks.Count, scope = "Real two-client Fusion/physics; no headset or browser video verification" };
    }
}
