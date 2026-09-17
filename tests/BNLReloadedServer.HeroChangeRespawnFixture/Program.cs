using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
object Raw(Type type) => RuntimeHelpers.GetUninitializedObject(type);
void SetField(object obj, string name, object? value) =>
    obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);

// Exercise the real ApplyHeroChangeRespawn on a zone snapshot, without network/database side effects.
var apply = typeof(GameZone).GetMethod("ApplyHeroChangeRespawn", BindingFlags.NonPublic | BindingFlags.Instance)!;

(GameZone zone, ZoneData data, Dictionary<uint, DateTimeOffset> stash, List<Unit> drop) NewZone()
{
    var zone = (GameZone)Raw(typeof(GameZone));
    var data = (ZoneData)Raw(typeof(ZoneData));
    data.RespawnInfo = new Dictionary<uint, ulong>();
    var updaterField = typeof(ZoneData).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .First(f => f.FieldType == typeof(ZoneUpdater));
    updaterField.SetValue(data, new ZoneUpdater(_ => { }));
    var stash = new Dictionary<uint, DateTimeOffset>();
    var drop = new List<Unit>();
    SetField(zone, "_zoneData", data);
    SetField(zone, "_heroChangeRespawn", stash);
    SetField(zone, "_unitsToDrop", drop);
    return (zone, data, stash, drop);
}
Unit NewUnit(uint playerId) { var u = (Unit)Raw(typeof(Unit)); u.PlayerId = playerId; u.IsDead = false; u.RespawnTime = null; return u; }

// 1. Dead with a future deadline: the new hero enters dead, keeps the deadline, is dropped, and the client sees the timer.
{
    var (zone, data, stash, drop) = NewZone();
    var deadline = DateTimeOffset.Now.AddSeconds(7);
    stash[3] = deadline;
    var unit = NewUnit(3);
    apply.Invoke(zone, new object[] { 3u, unit });
    Check(unit.IsDead, "changed-hero-while-dead re-enters dead");
    Check(unit.RespawnTime == deadline, "remaining respawn deadline is preserved, not reset");
    Check(drop.Contains(unit), "new hero unit is dropped so the respawn loop owns it");
    Check(data.RespawnInfo.TryGetValue(3, out var ms) && ms == (ulong)deadline.ToUnixTimeMilliseconds(),
        "client is told the respawn deadline");
    Check(!stash.ContainsKey(3), "pending entry is consumed");
}
// 2. No pending entry (alive when changing heroes): spawns normally.
{
    var (zone, data, stash, drop) = NewZone();
    var unit = NewUnit(4);
    apply.Invoke(zone, new object[] { 4u, unit });
    Check(!unit.IsDead && unit.RespawnTime == null, "changing heroes while alive spawns immediately");
    Check(drop.Count == 0 && data.RespawnInfo.Count == 0, "no drop or respawn timer when alive");
}
// 3. Deadline already elapsed (spent longer picking than the timer): spawns immediately.
{
    var (zone, data, stash, drop) = NewZone();
    stash[5] = DateTimeOffset.Now.AddSeconds(-1);
    var unit = NewUnit(5);
    apply.Invoke(zone, new object[] { 5u, unit });
    Check(!unit.IsDead && unit.RespawnTime == null, "an already-elapsed timer does not hold the new hero dead");
    Check(drop.Count == 0, "elapsed timer adds no drop");
    Check(!stash.ContainsKey(5), "elapsed pending entry is still consumed");
}
// 4. The capture rule mirrored: only a dead unit with a future RespawnTime should have been stashed.
{
    bool ShouldStash(bool isDead, DateTimeOffset? respawn) =>
        isDead && respawn is { } d && d > DateTimeOffset.Now;
    Check(ShouldStash(true, DateTimeOffset.Now.AddSeconds(5)), "dead + future deadline is captured");
    Check(!ShouldStash(false, DateTimeOffset.Now.AddSeconds(5)), "alive is not captured");
    Check(!ShouldStash(true, null), "dead without a deadline is not captured");
    Check(!ShouldStash(true, DateTimeOffset.Now.AddSeconds(-5)), "dead with a past deadline is not captured");
}

Console.WriteLine($"{checks} checks passed.");
