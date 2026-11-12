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
        public string role, phase;
        public bool connected, canStart;
        public int frame, tick, players, loaded, npcs, acceptedInputs;
        public float averageFrameIntervalMs;
        public long allocatedBytes;
        public PoseEvidence local;
        public PoseEvidence[] vr, desktop;
    }
    string output;
    float nextWrite;
    double frameTime;
    int frames;

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
}
#endif
