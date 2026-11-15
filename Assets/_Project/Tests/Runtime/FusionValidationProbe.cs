#if FUSION_ACCEPTANCE && DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Fusion;
using LiveStock.Multiplayer;
using UnityEngine;

/// <summary>Local file-based evidence probe included only in the explicit Development acceptance build.</summary>
public sealed class FusionValidationProbe : MonoBehaviour
{
    [Serializable]
    public sealed class PoseEvidence
    {
        public int player;
        public Vector3 root, head, left, right;
        public Quaternion rotation;
        public bool hasPose;
    }
    [Serializable]
    public sealed class Snapshot
    {
        public int probeVersion = 2;
        public string role, phase;
        public bool connected, canStart;
        public int frame, tick, players, loaded, npcs, acceptedInputs;
        public int npcMinimum, npcMaximum, apples, collected, required, targetFruit;
        public bool missionComplete;
        public string feedback;
        public string[] cctvCameras;
        public float averageFrameIntervalMs;
        public long allocatedBytes;
        public PoseEvidence local;
        public PoseEvidence[] vr, desktop;
    }
    string output;
    float nextWrite;
    double frameTime;
    int frames;
    FruitPickup selectedFruit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-fusion-probe-output");
        if (index < 0 || index + 1 >= args.Length) return;
        // The headless acceptance process has no render device for Meta's overlay camera.
        if (Application.isBatchMode)
            foreach (var overlay in FindObjectsByType<OVROverlayCanvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                overlay.enabled = false;
        var go = new GameObject("Fusion Validation Probe");
        DontDestroyOnLoad(go);
        go.AddComponent<FusionValidationProbe>().output = Path.GetFullPath(args[index + 1]);
    }

    void Update()
    {
        frameTime += Time.unscaledDeltaTime * 1000;
        frames++;
        if (Time.unscaledTime < nextWrite || string.IsNullOrEmpty(output)) return;
        nextWrite = Time.unscaledTime + .5f;
        var server = FindFirstObjectByType<ServerConnectionManager>();
        var client = FindFirstObjectByType<ConnectionManager>();
        ProcessCommand(server, client);
        var runner = server != null ? server.Runner : client != null ? client.Runner : null;
        var state = runner != null ? LobbySessionState.For(runner) : null;
        var world = state != null ? state.World : null;
        var spawner = FindObjectsByType<RandomNPCSpawner>(FindObjectsSortMode.None)
            .FirstOrDefault(s => NetworkRunner.GetRunnerForGameObject(s.gameObject) == runner);
        var evidence = new Snapshot
        {
            role = server != null ? "server" : client != null && client.IsDesktop ? "desktop" : "vr-preview",
            phase = state != null ? state.Phase.ToString() : "",
            connected = runner != null && runner.IsRunning,
            canStart = state != null && state.CanStart,
            frame = Time.frameCount, tick = runner != null && runner.IsRunning ? runner.Tick.Raw : 0,
            players = state != null ? state.PlayerCount : 0,
            loaded = state != null ? state.LoadedPlayers : 0,
            npcs = FindObjectsByType<NetworkNpcAuthorityGate>(FindObjectsSortMode.None).Length,
            acceptedInputs = state != null ? state.World.AcceptedInputCount : 0,
            npcMinimum = spawner != null ? Math.Min(spawner.MaxNetworkedNPCs, spawner.Groups.Where(g => g.Prefab != null).Sum(g => g.MinCount)) : 0,
            npcMaximum = spawner != null ? Math.Min(spawner.MaxNetworkedNPCs, spawner.Groups.Where(g => g.Prefab != null).Sum(g => g.MaxCount)) : 0,
            apples = FindObjectsByType<FruitPickup>(FindObjectsSortMode.None).Count(a => a.Object != null && a.Object.IsValid && a.Runner == runner),
            collected = world != null ? world.CollectedFruitCount : 0,
            required = world != null ? world.RequiredFruitCount : 0,
            missionComplete = world != null && world.MissionComplete,
            targetFruit = selectedFruit != null && selectedFruit.Object != null && selectedFruit.Object.IsValid ? (int)selectedFruit.Object.Id.Raw : 0,
            feedback = client != null ? client.GetComponent<PlayerGameplay>()?.Feedback : null,
            cctvCameras = server != null ? FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.name == "View 1" || c.name.StartsWith("CCTV_")).Select(c => $"{c.name} active={c.gameObject.activeInHierarchy} enabled={c.enabled} mask={c.cullingMask} override={c.overrideSceneCullingMask} pos={c.transform.position} forward={c.transform.forward} texture={c.targetTexture != null} scene={c.gameObject.scene.name}").ToArray() : Array.Empty<string>(),
            averageFrameIntervalMs = (float)(frameTime / frames),
            allocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
            vr = FindObjectsByType<VRNetworkAvatar>(FindObjectsSortMode.None)
                .Where(a => a.Object != null && a.Object.IsValid && a.HasPose)
                .Select(a => Pose(a.Pose, a.Object.InputAuthority.PlayerId)).ToArray(),
            desktop = FindObjectsByType<DesktopNetworkAvatar>(FindObjectsSortMode.None)
                .Where(a => a.Object != null && a.Object.IsValid)
                .Select(a => new PoseEvidence { player = a.Object.InputAuthority.PlayerId, root = a.transform.position, rotation = a.transform.rotation }).ToArray()
        };
        if (client != null && client.IsConnected)
        {
            var vr = client.GetComponent<VRClientPoseSource>();
            var desktop = client.GetComponentInChildren<DesktopTestPlayer>();
            evidence.local = client.IsDesktop && desktop != null
                ? new PoseEvidence { root = desktop.transform.position, rotation = desktop.transform.rotation }
                : vr != null && vr.IsConfigured ? Pose(vr.ReadPose(), client.Runner.LocalPlayer.PlayerId) : null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(evidence));
    }

    static PoseEvidence Pose(VRTrackedPose pose, int player) => new PoseEvidence
    {
        player = player, root = pose.RootPosition, rotation = pose.RootRotation,
        head = pose.Head, left = pose.LeftHand, right = pose.RightHand, hasPose = true
    };

    void ProcessCommand(ServerConnectionManager server, ConnectionManager client)
    {
        string path = output + ".command";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim();
        File.Delete(path);
        if (command == "connect" && client != null) client.Connect();
        else if (command == "load" && server != null) server.LoadMainGame();
        else if (command == "start" && server != null) server.StartGame();
        else if (command == "stop" && server != null) server.StopServer();
        else if (command == "capture-cctv" && server != null) CaptureCctv();
        else if (command.StartsWith("approach-fruit ") && client != null && client.IsDesktop
            && int.TryParse(command.Substring("approach-fruit ".Length), out int fruitIndex)) ApproachFruit(client, fruitIndex);
        else if (command == "collect-fruit" && client != null && selectedFruit != null)
        {
            var camera = client.GetComponentInChildren<DesktopTestPlayer>().ViewCamera;
            client.GetComponent<PlayerGameplay>().TryCollect(selectedFruit, camera.ViewportPointToRay(new Vector3(.5f, .5f)));
        }
        else if (command == "repeat-fruit" && client != null && selectedFruit != null)
        {
            var camera = client.GetComponentInChildren<DesktopTestPlayer>().ViewCamera;
            client.State.World.RequestCollect(selectedFruit, camera.ViewportPointToRay(new Vector3(.5f, .5f)));
        }
        else if (command == "move" && client != null)
        {
            var actor = client.GetComponentInChildren<DesktopTestPlayer>();
            var vr = client.GetComponent<VRClientPoseSource>();
            if (client.IsDesktop && actor != null) actor.transform.position += new Vector3(1, 0, .5f);
            else if (vr != null && vr.IsConfigured)
            {
                vr.RigRoot.position += new Vector3(1, .1f, .5f);
                vr.HeadAnchor.localPosition += new Vector3(.1f, .05f, 0);
                vr.LeftHandAnchor.localPosition += new Vector3(0, .1f, .03f);
                vr.RightHandAnchor.localPosition += new Vector3(.05f, 0, .1f);
            }
        }
        else if (command == "quit") Application.Quit();
    }

    /// <summary>Positions the existing desktop test actor, then uses its real camera and shared gameplay request.</summary>
    void ApproachFruit(ConnectionManager client, int index)
    {
        selectedFruit = null;
        var apples = FindObjectsByType<FruitPickup>(FindObjectsSortMode.None)
            .Where(a => a.Object != null && a.Object.IsValid && a.Runner == client.Runner)
            .OrderBy(a => a.Object.Id.Raw).ToArray();
        if (index < 0 || index >= apples.Length || apples[index].IsCollected) return;
        var fruit = apples[index];
        var actor = client.GetComponentInChildren<DesktopTestPlayer>();
        var gameplay = client.GetComponent<PlayerGameplay>();
        foreach (var offset in new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left, Vector3.up })
        {
            actor.transform.position = fruit.RayCollider.bounds.center + offset * .75f - Vector3.up;
            actor.ViewCamera.transform.LookAt(fruit.RayCollider.bounds.center);
            if (fruit.IsNearby(gameplay.ReadBody()) && fruit.HasClearRay(
                actor.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f)), client.transform))
            {
                selectedFruit = fruit;
                return;
            }
        }
    }

    /// <summary>Captures the actual sender render textures to distinguish rendering faults from browser decoding.</summary>
    void CaptureCctv()
    {
        int index = 0;
        var previous = RenderTexture.active;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.name == "View 1" && c.targetTexture != null))
        {
            var target = camera.targetTexture;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output), "cctv-" + index++ + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(pixels); }
        }
    }
}
#endif
