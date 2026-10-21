using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

internal static class CaptureDecayChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS decay: " + name); }
        Vector3[] centers = [new(0, 10, 0), new(0, 10, 30), new(0, 10, 60)];
        SkyBridgeConquest.Player a = new(1, TeamType.Team1, centers[0]);
        SkyBridgeConquest.Player b = new(2, TeamType.Team2, centers[0]);
        SkyBridgeConquest New() => new(centers);
        var mode = New();
        mode.Step(6, [a]); mode.Step(1.5f, []);
        Check(mode.Zones[0].Progress == 6 && mode.Zones[0].Capturing == a.Team, "short absence preserves progress and ring team");
        mode.Step(.5f, []);
        Check(mode.Zones[0].Progress == 6, "two-second boundary preserves progress");
        mode.Step(.5f, []);
        Check(mode.Zones[0].Progress == 5.5f, "progress decays at one capture-second per second after grace");
        mode.Step(.5f, [a]);
        Check(mode.Zones[0].Progress == 6, "return resumes retained progress");
        mode.Step(2, []);
        Check(mode.Zones[0].Progress == 6, "return renews grace period");
        mode.Step(100, []);
        Check(mode.Zones[0].Progress == 0 && mode.Zones[0].Capturing == TeamType.Neutral && mode.Zones[0].Owner == TeamType.Neutral, "decay clamps to zero and clears capture team");
        Check(mode.ZonesCaptured(a.Id) == 0, "decay awards no capture credit");
        var whole = New(); var split = New();
        whole.Step(6, [a]); split.Step(6, [a]); whole.Step(3.25f, []);
        for (int i = 0; i < 13; i++) split.Step(.25f, []);
        Check(whole.Zones[0].Progress == 4.75f && split.Zones[0].Progress == whole.Zones[0].Progress, "grace-crossing tick matches split ticks");
        split.Step(20, [a, b]);
        Check(split.Zones[0].Progress == 4.75f && split.Zones[0].Contested, "occupied tie pauses progress");
        split.Step(2, []);
        Check(split.Zones[0].Progress == 4.75f, "occupied tie resets absence timer");
        var reverse = New(); reverse.Step(6, [a]); reverse.Step(2, [b]);
        Check(reverse.Zones[0].Progress == 4 && reverse.Zones[0].Capturing == a.Team, "opposing majority gradually erases original team's ring");
        reverse.Step(5, [b]);
        Check(reverse.Zones[0].Progress == 1 && reverse.Zones[0].Capturing == b.Team, "remaining tick time starts opposing capture after erasure");
        reverse.Step(9, [b]);
        Check(reverse.Zones[0].Owner == b.Team && reverse.ZonesCaptured(b.Id) == 1, "opponent captures after rebuilding progress");
        reverse.Step(4, [a]); reverse.Step(1, [b]);
        Check(reverse.Zones[0].Progress == 3 && reverse.Zones[0].Capturing == a.Team, "owner returning removes enemy progress gradually");
        reverse.Step(100, [b]);
        Check(reverse.Zones[0].Progress == 0 && reverse.Zones[0].Capturing == TeamType.Neutral && reverse.Zones[0].Owner == b.Team, "owner clears hostile progress without capturing own point");
        reverse.Step(100, []);
        Check(reverse.Zones[0].Owner == b.Team, "abandoned completed ownership is persistent");
        var exact = New(); exact.Step(4, [a]); exact.Step(4, [b]);
        Check(exact.Zones[0].Progress == 0 && exact.Zones[0].Capturing == TeamType.Neutral, "exact erasure boundary has no false capture");
        exact.Step(0, [b]); Check(exact.Zones[0].Progress == 0, "zero elapsed does not advance capture");
        var resumed = New(); resumed.Step(6, [b]); resumed.Step(3, []); resumed.Step(5, [b]);
        Check(resumed.Zones[0].Owner == b.Team && resumed.ZonesCaptured(b.Id) == 1, "team two resumes and earns capture credit once");
        var round = New();
        SkyBridgeConquest.Player[] pair = [a, new(3, a.Team, centers[1])];
        round.Step(10, pair); round.Step(4, [new(4, b.Team, centers[2])]); round.Step(3, []);
        round.Step(420, pair); Check(round.Attacking, "attack starts normally with partially abandoned third zone");
        round.Step(90, pair);
        Check(round.Zones.All(z => z.Progress == 0 && z.Capturing == TeamType.Neutral), "new round clears all partial capture state");
        round.Step(5, [a]); round.Step(2, []);
        Check(round.Zones[0].Progress == 5, "new round gets a fresh absence grace period");
        Console.WriteLine($"PASS capture decay: {checks} checks");
    }
}
