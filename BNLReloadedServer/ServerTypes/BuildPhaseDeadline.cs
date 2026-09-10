using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public static class BuildPhaseDeadline
{
    // Called only on the zone update queue. Advance must commit the next phase
    // before returning, so later ticks cannot advance the same phase twice.
    public static bool AdvanceIfDue(ZonePhase phase, long now, Action advance)
    {
        if (phase.PhaseType is not (ZonePhaseType.Build or ZonePhaseType.Build2) ||
            phase.EndTime is not long deadline || now < deadline) return false;
        advance();
        return true;
    }
}
