using System;
using System.Collections.Generic;

// Game rules independent of cameras, WebRTC and the web UI.
public sealed class CCTVMonitorState
{
    public sealed class Monitor
    {
        public float Heat { get; internal set; }
        public float RecoveryRemaining { get; internal set; }
        public int Viewers { get; internal set; }
        public bool Overheated => RecoveryRemaining > 0;
    }

    public sealed class Viewer
    {
        public int SelectedIndex { get; internal set; }
        public int SecondaryIndex { get; internal set; } = -1;
        public float CooldownRemaining { get; internal set; }

        public int IndexAt(int slot) => slot == 0 ? SelectedIndex : slot == 1 ? SecondaryIndex : -1;
    }

    public Monitor[] Monitors { get; }
    public IReadOnlyDictionary<string, Viewer> Viewers => viewers;
    public float HeatLimit { get; }
    private readonly Dictionary<string, Viewer> viewers = new Dictionary<string, Viewer>();
    private readonly float heatPerViewer;
    private readonly float coolingRate;
    private readonly float recoveryDuration;
    private readonly float switchCooldown;

    public CCTVMonitorState(int count, float heatLimit = 100, float heatPerViewer = 5,
        float coolingRate = 10, float recoveryDuration = 10, float switchCooldown = 3)
    {
        if (count < 1 || heatLimit <= 0 || heatPerViewer <= 0 || coolingRate < 0 ||
            recoveryDuration <= 0 || switchCooldown < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Invalid CCTV rules.");
        Monitors = new Monitor[count];
        for (int i = 0; i < count; i++) Monitors[i] = new Monitor();
        HeatLimit = heatLimit;
        this.heatPerViewer = heatPerViewer;
        this.coolingRate = coolingRate;
        this.recoveryDuration = recoveryDuration;
        this.switchCooldown = switchCooldown;
    }

    public Viewer AddViewer(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException(nameof(connectionId));
        if (viewers.TryGetValue(connectionId, out var existing)) return existing;
        var viewer = new Viewer();
        for (int i = 0; i < Monitors.Length; i++)
            if (!Monitors[i].Overheated) { viewer.SelectedIndex = i; break; }
        // Fill up to two distinct slots on connection, without a separate UI
        // switch. If only a hot second camera exists, show its recovery screen.
        for (int i = 0; i < Monitors.Length; i++)
            if (i != viewer.SelectedIndex)
            {
                viewer.SecondaryIndex = i;
                if (!Monitors[i].Overheated) break;
            }
        viewers.Add(connectionId, viewer);
        return viewer;
    }

    public void RemoveViewer(string connectionId) => viewers.Remove(connectionId);

    public bool TrySwitch(string connectionId, int index, int slot = 0)
    {
        if (!viewers.TryGetValue(connectionId, out var viewer) || index < 0 || index >= Monitors.Length ||
            slot < 0 || slot > 1 || viewer.IndexAt(slot) < 0 ||
            index == viewer.IndexAt(slot) || viewer.CooldownRemaining > 0 || Monitors[index].Overheated)
            return false;
        int previous = viewer.IndexAt(slot);
        // Selecting the other visible monitor swaps the two slots; one player
        // never counts twice on the same physical monitor.
        if (slot == 0)
        {
            if (viewer.SecondaryIndex == index) viewer.SecondaryIndex = previous;
            viewer.SelectedIndex = index;
        }
        else
        {
            if (viewer.SelectedIndex == index) viewer.SelectedIndex = previous;
            viewer.SecondaryIndex = index;
        }
        viewer.CooldownRemaining = switchCooldown;
        return true;
    }

    public void Tick(float deltaTime, IEnumerable<string> connectedViewers)
    {
        if (deltaTime < 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        var counts = new int[Monitors.Length];
        foreach (var viewer in viewers.Values)
            viewer.CooldownRemaining = Math.Max(0, viewer.CooldownRemaining - deltaTime);
        foreach (var connectionId in new HashSet<string>(connectedViewers))
            if (viewers.TryGetValue(connectionId, out var viewer))
            {
                counts[viewer.SelectedIndex]++;
                if (viewer.SecondaryIndex >= 0 && viewer.SecondaryIndex != viewer.SelectedIndex)
                    counts[viewer.SecondaryIndex]++;
            }
        for (int i = 0; i < Monitors.Length; i++)
        {
            var monitor = Monitors[i];
            int count = counts[i];
            float remaining = deltaTime;
            // Account for a frame crossing the heat limit or recovery boundary.
            while (remaining > 0)
            {
                if (monitor.Overheated)
                {
                    float elapsed = Math.Min(remaining, monitor.RecoveryRemaining);
                    monitor.RecoveryRemaining = Math.Max(0, monitor.RecoveryRemaining - elapsed);
                    remaining -= elapsed;
                    if (!monitor.Overheated) monitor.Heat = 0;
                }
                else if (count > 0)
                {
                    float rate = heatPerViewer * count;
                    float elapsed = Math.Min(remaining, (HeatLimit - monitor.Heat) / rate);
                    monitor.Heat = Math.Min(HeatLimit, monitor.Heat + rate * elapsed);
                    remaining -= elapsed;
                    if (monitor.Heat >= HeatLimit) monitor.RecoveryRemaining = recoveryDuration;
                    else remaining = 0;
                }
                else
                {
                    monitor.Heat = Math.Max(0, monitor.Heat - coolingRate * remaining);
                    remaining = 0;
                }
            }
            monitor.Viewers = monitor.Overheated ? 0 : count;
        }
    }
}
