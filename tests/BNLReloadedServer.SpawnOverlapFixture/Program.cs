// Players whose respawn timers run out in the same tick and who chose the same respawn device must not
// spawn inside each other. The device locks as PlayerBlocked while someone stands on it; the fixture runs a
// real GameZone tick with three dead players on one device and requires exactly one to spawn, the rest to
// wait in the death cam until the device clears, and a waiting player to be free to pick another spawn.
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;
using Octree;

const ushort AirId = 0, FloorId = 1;
const int SizeX = 16, SizeY = 8, SizeZ = 16, FloorTop = 3;
const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
var gear = new CardGear { Id = "fixture_spawn_gear" };
var hero = new CardUnit { Id = "fixture_spawn_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
var device = new CardUnit
{
    Id = "fixture_spawn_device", Data = new UnitDataCommon(), Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center,
    Labels = [UnitLabel.RespawnPoint], SpawnPoint = new UnitSpawnPoint { SideShift = 0 }
};
var match = new CardMatch { Id = "fixture_spawn_match", Data = new MatchDataShieldCapture() };
var mode = new CardGameMode { Id = "game_mode_friendly" };
catalogue.Replicate([
    gear, hero, device, match, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true }
]);

((UnitDataPlayer)hero.Data!).Gears = [gear.Key]; // keys exist only after Replicate

var blocks = new byte[SizeX * SizeY * SizeZ * 6];
for (var x = 0; x < SizeX; x++)
for (var y = 0; y < FloorTop; y++)
for (var z = 0; z < SizeZ; z++)
    BitConverter.TryWriteBytes(blocks.AsSpan(((x * SizeY + y) * SizeZ + z) * 6, 2), FloorId);

var devicePos = new Vector3(8.5f, FloorTop + 0.5f, 8.5f);
var mapData = new MapData
{
    Match = MatchType.ShieldCapture,
    Properties = new MapDataProps(),
    Size = new Vector3s(SizeX, SizeY, SizeZ),
    BlocksData = blocks.Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(3f, FloorTop, 3f) }],
    Units = [new MapUnit { UnitKey = device.Key, Team = TeamType.Team1, Position = devicePos }]
};

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var id in new uint[] { 1, 2, 3 })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = TeamType.Team1, Hero = hero.Key, Nickname = $"p{id}" };

var zone = new GameZone(Stub<IServiceZone>(), Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), mapData,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);

T Field<T>(string name) => (T)typeof(GameZone).GetField(name, Any)!.GetValue(zone)!;
object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, args);
void Tick(ulong n) => ((Action)Call("OnTick", n)!)();

// Everything runs on the zone's own action queue, exactly as live ticks do.
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}

var zoneData = Field<ZoneData>("_zoneData");
var deviceSpawnId = OnZone(() => Field<Dictionary<uint, Unit>>("_playerSpawnPoints").Keys.Single());
var baseSpawnId = OnZone(() => zoneData.SpawnPoints.Keys.Single(k => k != deviceSpawnId));
var units = OnZone(() => new uint[] { 1, 2, 3 }.Select(id => (Unit)Call("CreatePlayerUnit", id, Stub<IServiceZone>())!).ToList());
foreach (var u in units) u.ZoneService = Stub<IServiceZone>(); // as SendLoadZone does for a joined player
Check(units.All(u => u.PlayerId is not null && !u.IsDead), "three live player units created through the zone");

// Die together, all on the device, all timers already expired: the reported situation.
OnZone(() =>
{
    foreach (var u in units)
    {
        u.IsDead = true;
        u.IsDropped = true;
        u.RespawnTime = DateTimeOffset.Now.AddSeconds(-1);
        zoneData.UpdatePlayerSelectedSpawn(u.PlayerId!.Value, deviceSpawnId);
    }
    Tick(1);
    return 0;
});

bool SameBody(Unit a, Unit b) => Vector2.Distance(new(a.Transform.Position.X, a.Transform.Position.Z),
    new(b.Transform.Position.X, b.Transform.Position.Z)) < 0.64f && MathF.Abs(a.Transform.Position.Y - b.Transform.Position.Y) < 1.9f;

var alive = units.Where(u => !u.IsDead).ToList();
Check(alive.Count == 1, $"exactly one of three spawns on the device in the shared tick (spawned {alive.Count})");
var first = alive[0];
Check(Vector2.Distance(new(first.Transform.Position.X, first.Transform.Position.Z), new(devicePos.X, devicePos.Z)) < 0.01f,
    "the one who spawned is on the device");
Check(zoneData.SpawnPoints[deviceSpawnId].Lock == SpawnPointLockType.PlayerBlocked, "the device now reports PlayerBlocked to clients");

OnZone(() => { Tick(2); Tick(3); return 0; });
Check(units.Count(u => !u.IsDead) == 1, "the others keep waiting while the device is occupied");

// A waiting player may give up and pick the base; they spawn there without waiting for the device.
var switcher = units.First(u => u.IsDead);
OnZone(() => { zoneData.UpdatePlayerSelectedSpawn(switcher.PlayerId!.Value, baseSpawnId); Tick(4); return 0; });
Check(!switcher.IsDead, "a waiting player who picks another spawn spawns there");
Check(!SameBody(switcher, first), "and not inside the player on the device");

// The first player walks off; the device frees and the last waiting player spawns on it.
var last = units.Single(u => u.IsDead);
OnZone(() =>
{
    first.Transform.Position = first.Transform.Position + new Vector3(3, 0, 0);
    typeof(GameZone).GetMethod("UnitMoved", Any)!.Invoke(zone, [first, 0UL, first.Transform, devicePos]);
    Tick(5); Tick(6);
    return 0;
});
Check(!last.IsDead, "the last player spawns once the device clears");
Check(units.Where(u => !u.IsDead).SelectMany((a, i) => units.Where(u => !u.IsDead).Skip(i + 1).Select(b => (a, b))).All(p => !SameBody(p.a, p.b)),
    "no two live players share a body");

// A respawn must not leave the old body in the octree: exactly one entry per player unit.
var octree = Field<BoundsOctreeEx<Unit>>("_unitOctree");
var entries = OnZone(() => units.Sum(u => octree.GetColliding(new BoundingBoxEx(new Vector3(SizeX / 2f, SizeY / 2f, SizeZ / 2f),
    new Vector3(SizeX, SizeY, SizeZ) * 4)).Count(e => e == u)));
Check(entries == 3, $"one octree entry per respawned player (found {entries})");

// A player who died standing on the device must not lock it against their own respawn.
OnZone(() =>
{
    first.Transform.Position = devicePos with { Y = FloorTop + 0.08f };
    typeof(GameZone).GetMethod("UnitMoved", Any)!.Invoke(zone, [first, 0UL, first.Transform, first.Transform.Position]);
    foreach (var u in units.Where(u => u != first)) // clear the others off the device
    {
        u.Transform.Position = new Vector3(2.5f + u.PlayerId!.Value, FloorTop, 12.5f);
        typeof(GameZone).GetMethod("UnitMoved", Any)!.Invoke(zone, [u, 0UL, u.Transform, u.Transform.Position]);
    }
    first.IsDead = true;
    first.IsDropped = true;
    first.RespawnTime = DateTimeOffset.Now.AddSeconds(-1);
    zoneData.UpdatePlayerSelectedSpawn(first.PlayerId!.Value, deviceSpawnId);
    Tick(7); Tick(8);
    return 0;
});
Check(!first.IsDead, "a player who died on the device can still respawn on it");

zone.Stop();
Console.WriteLine($"Spawn overlap fixture passed: {checks} checks.");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{
    var proxy = DispatchProxy.Create<T, StubProxy>();
    ((StubProxy)(object)proxy).Returns = returns ?? [];
    return proxy;
}

public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
