using BNLReloadedServer.ServerTypes;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

var state = new GroundSlamEntitlements();
Check(!state.TryConsume(1, 10, 2), "hit without CTRL is rejected");

state.Begin(1, 10, 2);
Check(state.TryConsume(1, 10, 2), "CTRL arms one matching ground-pound hit");
Check(!state.TryConsume(1, 10, 2), "ground-pound hit is consumed once");

state.Begin(1, 10, 2);
state.Cancel(1);
Check(!state.TryConsume(1, 10, 2), "CTRL then double jump rejects landing hit");

state.Begin(1, 10, 2);
state.Cancel(1);
state.Begin(1, 10, 2);
Check(state.TryConsume(1, 10, 2), "CTRL after double jump arms a fresh hit");

state.Begin(1, 10, 2);
Check(!state.TryConsume(1, 11, 2), "respawned unit cannot use a stale ground pound");

state.Begin(1, 10, 2);
Check(!state.TryConsume(1, 10, 3), "different tool cannot use a pending ground pound");

Console.WriteLine("Ground-slam entitlement suite passed.");

var forceFall = new ForceFallEntitlements();
Check(forceFall.TryConsume(1, 10), "legacy CTRL impact remains compatible");

forceFall.Begin(1, 10);
Check(forceFall.TryConsume(1, 10), "CTRL arms one forced-fall impact");

forceFall.Begin(1, 10);
forceFall.Cancel(1, 10);
Check(!forceFall.TryConsume(1, 10), "CTRL then double jump rejects forced-fall impact");

forceFall.Begin(1, 10);
forceFall.Cancel(1, 10);
forceFall.Begin(1, 10);
Check(forceFall.TryConsume(1, 10), "CTRL after double jump restores forced-fall impact");

forceFall.Cancel(1, 10);
Check(!forceFall.TryConsume(1, 11), "respawned unit cannot inherit a cancelled forced fall");

Console.WriteLine("Force-fall entitlement suite passed.");
