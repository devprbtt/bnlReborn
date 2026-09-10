using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;
static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
foreach (var build in new[] {ZonePhaseType.Build, ZonePhaseType.Build2})
{
    var phase = new ZonePhase {PhaseType = ZonePhaseType.Waiting, StartTime = 1000, EndTime = 1000};
    int transitions = 0;
    void Advance() { transitions++; phase = new ZonePhase {PhaseType = build == ZonePhaseType.Build ? ZonePhaseType.Assault : ZonePhaseType.Assault2}; }
    Check(!BuildPhaseDeadline.AdvanceIfDue(phase, 1100, Advance), "setup cannot advance before Build is committed: " + build);
    phase.PhaseType = build;
    Check(BuildPhaseDeadline.AdvanceIfDue(phase, 1100, Advance) && transitions == 1, "zero deadline survives slow setup: " + build);
    Check(!BuildPhaseDeadline.AdvanceIfDue(phase, 1200, Advance) && transitions == 1, "repeated tick cannot advance twice: " + build);
    phase = new ZonePhase {PhaseType = build, StartTime = 1000, EndTime = 6000};
    Check(!BuildPhaseDeadline.AdvanceIfDue(phase, 5999, Advance), "normal countdown does not expire early: " + build);
    Check(BuildPhaseDeadline.AdvanceIfDue(phase, 6000, Advance), "normal countdown expires at deadline: " + build);
    phase = new ZonePhase {PhaseType = build};
    Check(!BuildPhaseDeadline.AdvanceIfDue(phase, long.MaxValue, Advance), "unbounded build stays unbounded: " + build);
}
