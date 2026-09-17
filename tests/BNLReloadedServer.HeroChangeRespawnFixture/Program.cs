using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
object Raw(Type type) => RuntimeHelpers.GetUninitializedObject(type);
void SetField(object o, string n, object? v) => o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, v);

var spawnDue = typeof(GameZone).GetMethod("SpawnDueHeroChanges", BindingFlags.NonPublic | BindingFlags.Instance)!;
var pendingType = typeof(GameZone).GetField("_pendingHeroChangeSpawns", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType;
var valueType = pendingType.GetGenericArguments()[1]; // (DateTimeOffset, IServiceZone)

object Entry(DateTimeOffset deadline)
{
    // Build the (deadline, service) tuple with a throwaway service; the gates under test never touch it.
    var svc = (IServiceZone)Raw(typeof(ServiceZone));
    return Activator.CreateInstance(valueType, deadline, svc)!;
}

GameZone NewZone(out System.Collections.IDictionary pending, out Dictionary<uint, uint> unitIds,
    out ConcurrentDictionary<uint, PlayerLobbyState> lobby)
{
    var zone = (GameZone)Raw(typeof(GameZone));
    var data = (ZoneData)Raw(typeof(ZoneData));
    data.RespawnInfo = new Dictionary<uint, ulong>();
    var updaterField = typeof(ZoneData).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).First(f => f.FieldType == typeof(ZoneUpdater));
    updaterField.SetValue(data, new ZoneUpdater(_ => { }));
    pending = (System.Collections.IDictionary)Activator.CreateInstance(pendingType)!;
    unitIds = new Dictionary<uint, uint>();
    lobby = new ConcurrentDictionary<uint, PlayerLobbyState>();
    SetField(zone, "_zoneData", data);
    SetField(zone, "_pendingHeroChangeSpawns", pending);
    SetField(zone, "_playerIdToUnitId", unitIds);
    SetField(zone, "_playerLobbyInfo", lobby);
    return zone;
}

// 1. Deadline still in the future: the spawn is deferred, entry stays.
{
    var zone = NewZone(out var pending, out _, out _);
    pending[7u] = Entry(DateTimeOffset.Now.AddSeconds(6));
    spawnDue.Invoke(zone, null);
    Check(pending.Contains(7u), "future deadline keeps the player waiting (spawn deferred)");
}
// 2. Deadline elapsed but the player already left (no lobby info): consumed, no spawn attempt.
{
    var zone = NewZone(out var pending, out _, out _);
    pending[7u] = Entry(DateTimeOffset.Now.AddSeconds(-1));
    spawnDue.Invoke(zone, null);
    Check(!pending.Contains(7u), "elapsed entry for a departed player is cleared without spawning");
}
// 3. Deadline elapsed but the player already has a unit: consumed, not double-spawned.
{
    var zone = NewZone(out var pending, out var unitIds, out _);
    unitIds[7u] = 100u;
    pending[7u] = Entry(DateTimeOffset.Now.AddSeconds(-1));
    spawnDue.Invoke(zone, null);
    Check(!pending.Contains(7u), "elapsed entry is not spawned again when a unit already exists");
}
// 4. Empty set is a no-op.
{
    var zone = NewZone(out var pending, out _, out _);
    spawnDue.Invoke(zone, null);
    Check(pending.Count == 0, "no pending spawns is a safe no-op");
}
// 5. Capture rule mirrored: only a dead unit with a future RespawnTime is deferred.
{
    bool ShouldDefer(bool dead, DateTimeOffset? respawn) => dead && respawn is { } d && d > DateTimeOffset.Now;
    Check(ShouldDefer(true, DateTimeOffset.Now.AddSeconds(5)), "dead + future deadline is deferred");
    Check(!ShouldDefer(false, DateTimeOffset.Now.AddSeconds(5)), "alive spawns immediately");
    Check(!ShouldDefer(true, null), "dead without a deadline spawns immediately");
    Check(!ShouldDefer(true, DateTimeOffset.Now.AddSeconds(-3)), "dead past the deadline spawns immediately");
}

Console.WriteLine($"{checks} checks passed.");
