using System;

// Executes the production rules without starting Unity or a signaling server.
public static class CCTVMonitorStateTests
{
    public static void Main()
    {
        var one = new CCTVMonitorState(3);
        var two = new CCTVMonitorState(3);
        one.AddViewer("phone");
        two.AddViewer("phone");
        two.AddViewer("pc");
        one.Tick(5, new[] { "phone" });
        two.Tick(5, new[] { "phone", "pc" });
        Equal(25, one.Monitors[0].Heat, "one viewer gains 5 heat/s");
        Equal(50, two.Monitors[0].Heat, "two viewers gain 10 heat/s");
        Equal(50, two.Monitors[1].Heat, "both players watch a second camera automatically");
        Equal(0, two.Monitors[2].Heat, "unselected monitor stays cool");
        two.Tick(2, Array.Empty<string>());
        Equal(30, two.Monitors[0].Heat, "last viewer leaving cools by 10/s");
        two.Tick(5, Array.Empty<string>());
        Equal(0, two.Monitors[0].Heat, "cooling never goes below zero");

        var switches = new CCTVMonitorState(2);
        switches.AddViewer("phone");
        switches.AddViewer("pc");
        Check(!switches.TrySwitch("unknown", 1), "unknown connection rejected");
        Check(!switches.TrySwitch("phone", -1) && !switches.TrySwitch("phone", 2), "invalid view rejected");
        Check(!switches.TrySwitch("phone", 0), "selecting same monitor is a no-op");
        Check(switches.TrySwitch("phone", 1), "first switch allowed immediately");
        Equal(0, switches.Viewers["pc"].SelectedIndex, "phone switch leaves PC on its own monitor");
        Equal(0, switches.Viewers["pc"].CooldownRemaining, "phone cooldown does not affect PC");
        Check(switches.TrySwitch("pc", 1), "PC can switch while phone is cooling down");
        Check(!switches.TrySwitch("phone", 0), "rapid switch rejected by Unity rules");
        switches.Tick(2, Array.Empty<string>());
        Check(!switches.TrySwitch("phone", 0), "switch still rejected after two seconds");
        switches.Tick(1, Array.Empty<string>());
        Check(switches.TrySwitch("phone", 0), "switch allowed after three seconds");

        var split = new CCTVMonitorState(3);
        split.AddViewer("phone");
        split.AddViewer("pc");
        split.TrySwitch("phone", 2);
        split.Tick(5, new[] { "pc", "phone", "phone" });
        Equal(25, split.Monitors[0].Heat, "PC heats its selected camera");
        Equal(25, split.Monitors[2].Heat, "phone heats another camera, duplicate connection counts once");
        Equal(50, split.Monitors[1].Heat, "both viewers heat their shared secondary camera");
        split.RemoveViewer("phone");
        split.Tick(1, new[] { "pc", "phone" });
        Equal(15, split.Monitors[2].Heat, "disconnected camera cools without changing PC");
        Equal(0, split.Viewers["pc"].SelectedIndex, "disconnect leaves other selection intact");
        split.AddViewer("phone");
        Equal(0, split.Viewers["phone"].CooldownRemaining, "reconnected browser starts with its own fresh state");
        Check(ReferenceEquals(split.AddViewer("pc"), split.Viewers["pc"]), "duplicate add preserves existing session");

        var heat = new CCTVMonitorState(3);
        heat.AddViewer("phone");
        heat.AddViewer("pc");
        heat.Tick(10, new[] { "phone", "pc" });
        Check(heat.Monitors[0].Overheated, "two viewers overheat after ten seconds");
        Equal(100, heat.Monitors[0].Heat, "heat capped at 100");
        Equal(0, heat.Monitors[0].Viewers, "overheated monitor stops showing viewers");
        Check(heat.TrySwitch("phone", 2), "can leave an overheated monitor for a cool third camera");
        Equal(0, heat.Viewers["pc"].SelectedIndex, "other viewer stays on overheated camera");
        heat.Tick(3, Array.Empty<string>());
        Check(!heat.TrySwitch("phone", 0), "cannot select an overheated monitor even when cooldown ended");
        heat.Tick(6, Array.Empty<string>());
        Check(heat.Monitors[0].Overheated, "monitor remains unavailable until ten seconds");
        heat.Tick(1, Array.Empty<string>());
        Check(!heat.Monitors[0].Overheated, "monitor recovers after ten seconds");
        Equal(0, heat.Monitors[0].Heat, "recovery resets heat");
        Check(heat.TrySwitch("phone", 0), "recovered monitor can be selected");

        var boundary = new CCTVMonitorState(1);
        boundary.AddViewer("phone");
        boundary.Tick(31, new[] { "phone" });
        Equal(5, boundary.Monitors[0].Heat, "large tick crosses overheat, recovery and resumes heating");
        var frames = new CCTVMonitorState(1);
        frames.AddViewer("phone");
        frames.AddViewer("pc");
        for (int frame = 0; frame < 1210; frame++) frames.Tick(1f / 60, new[] { "phone", "pc" });
        Check(!frames.Monitors[0].Overheated, "small frame steps eventually recover");
        Check(frames.Monitors[0].Heat < 2, "frame-step recovery resumes heating from zero");
        var dual = new CCTVMonitorState(3);
        dual.AddViewer("phone");
        dual.AddViewer("pc");
        Equal(1, dual.Viewers["phone"].SecondaryIndex, "second monitor starts on another camera");
        Equal(0, dual.Viewers["phone"].CooldownRemaining, "both initial views require no extra click or cooldown");
        Check(dual.TrySwitch("phone", 2, 1), "secondary can independently select a third physical camera");
        Check(!dual.TrySwitch("phone", 2, 1), "secondary shares player's cooldown");
        Check(!dual.TrySwitch("phone", 2, 2), "invalid third slot rejected");
        dual.Tick(3, new[] { "phone", "phone", "pc" });
        Equal(30, dual.Monitors[0].Heat, "same physical camera shared by two people heats at 10/s");
        Equal(15, dual.Monitors[1].Heat, "phone's second camera heats at 5/s");
        Check(dual.TrySwitch("phone", 0, 1), "selecting other slot swaps views");
        Equal(2, dual.Viewers["phone"].SelectedIndex, "primary becomes former secondary");
        Equal(0, dual.Viewers["phone"].SecondaryIndex, "secondary becomes former primary");
        Equal(0, dual.Viewers["pc"].SelectedIndex, "swap never changes other player");
        Check(!dual.TrySwitch("phone", 1), "primary shares second slot's cooldown");
        dual.Tick(3, new[] { "phone" });
        Equal(0, dual.Monitors[1].Heat, "camera no longer watched permits cooling");
        Check(dual.TrySwitch("phone", 1, 0), "primary can choose another camera, still max two slots");
        var singleCamera = new CCTVMonitorState(1);
        singleCamera.AddViewer("phone");
        Equal(-1, singleCamera.Viewers["phone"].SecondaryIndex, "single-camera scene has no duplicate second slot");
        Check(!singleCamera.TrySwitch("phone", 0, 1), "empty second slot cannot accept input");
        var blocked = new CCTVMonitorState(2);
        blocked.AddViewer("phone");
        blocked.Tick(20, new[] { "phone" });
        Check(blocked.Monitors[0].Overheated && blocked.Monitors[1].Overheated, "both views accumulate heat");
        Check(!blocked.TrySwitch("phone", 0, 1), "cannot select an overheated camera");
        blocked.Tick(10, new[] { "phone" });
        Check(blocked.TrySwitch("phone", 0, 1), "can select recovered second camera");
        blocked.RemoveViewer("phone");
        blocked.Tick(0, Array.Empty<string>());
        Equal(0, blocked.Monitors[0].Viewers, "disconnect releases primary watcher");
        Equal(0, blocked.Monitors[1].Viewers, "disconnect releases secondary watcher");
        Console.WriteLine("CCTV rule checks passed, including two-view limit and shared per-player cooldown.");
    }

    private static void Equal(float expected, float actual, string message)
    {
        Check(Math.Abs(expected - actual) < 0.01f, $"{message}: expected {expected}, actual {actual}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
