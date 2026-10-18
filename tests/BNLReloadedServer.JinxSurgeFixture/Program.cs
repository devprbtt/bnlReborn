// Sweet Science's Power Surge against our own gravity well after an enemy Genie's Jinx turned it on us.
// Real live catalogue (args[0]), real GameZone ticks, real well / Jinx / Power Surge cards.
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
const int SizeX = 24, SizeY = 10, SizeZ = 24, FloorTop = 3;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
// "live" runs the cards as they are; "sized" applies the planned CDB change (a 1x1x1 well, as the client already assumes).
var variant = args.Length > 1 ? args[1] : "sized";
if (variant == "sized")
    foreach (var c in cards.OfType<CardUnit>().Where(c => c.Id is "unit_device_boxer_gravity_well" or "unit_device_boxer_gravity_well_test_map"))
        c.Size = new Vector3s(1, 1, 1);
catalogue.Replicate(cards);
CardUnit UnitCard(string id) => catalogue.GetCard<CardUnit>(new Key(id))!;
var wellCard = UnitCard("unit_device_boxer_gravity_well");
var jinxCard = UnitCard("unit_projectile_djinn_jinx");
var surge = catalogue.GetCard<CardAbility>(new Key("ability_boxer_power_surge"))!;
var gravityPull = new Key("effect_device_gravity_well");
var confusion = new Key("effect_hero_djinn_jinx_confused");

var blocks = new byte[SizeX * SizeY * SizeZ * 6];
for (var x = 0; x < SizeX; x++)
for (var y = 0; y < FloorTop; y++)
for (var z = 0; z < SizeZ; z++)
    BitConverter.TryWriteBytes(blocks.AsSpan(((x * SizeY + y) * SizeZ + z) * 6, 2), (ushort)3); // block_stone
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(SizeX, SizeY, SizeZ),
    BlocksData = blocks.Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(2, FloorTop, 2) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(21, FloorTop, 21) }]
};
var players = new ConcurrentDictionary<uint, PlayerLobbyState>
{
    [1] = new() { PlayerId = 1, Team = TeamType.Team1, Hero = new Key("unit_hero_boxer"), Nickname = "boxer" },
    [2] = new() { PlayerId = 2, Team = TeamType.Team2, Hero = new Key("unit_hero_djinn"), Nickname = "genie" }
};
var mode = new Key("game_mode_friendly");
var zone = new GameZone(Stub<IServiceZone>(), Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] a) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, a);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
}
ulong tick = 0;
void Ticks(int n) { for (var i = 0; i < n; i++) ((Action)Call("OnTick", tick++)!)(); }
void Move(Unit unit, Vector3 pos) { unit.Transform.Position = pos; Call("UnitMoved", unit, 0UL, unit.Transform, pos); }
ZoneTransform At(Vector3 pos) => new() { Position = pos, Rotation = Vector3s.Zero, LocalVelocity = Vector3s.Zero };

var (boxer, genie) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));
boxer.ZoneService = Stub<IServiceZone>(); genie.ZoneService = Stub<IServiceZone>();
var wellPos = new Vector3(12.5f, FloorTop + 0.5f, 12.5f);
var well = OnZone(() =>
{
    Move(boxer, new Vector3(12.5f, FloorTop, 14.5f)); // 2 m from the well: inside its 3.5 m pull and Power Surge's 8 m
    Move(genie, new Vector3(20.5f, FloorTop, 20.5f));
    var w = (Unit)Call("CreateUnit", wellCard, At(wellPos), boxer, null, false, false)!;
    Ticks(5);
    return w;
});
Check(well.Team == TeamType.Team1 && !well.IsDead, "our Boxer's real gravity well is alive on Team1");
// The well is really spawned by its thrown container (a landmine whose trigger is unit_spawn). With a size the
// spawn goes through placement adjustment, so check where it lands against where the container triggered.
var containerCard = UnitCard("unit_device_boxer_gravity_well_container");
var spawned = OnZone(() =>
{
    var units = (IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!;
    var containerPos = new Vector3(6.5f, FloorTop + 0.5f, 6.5f);
    var container = (Unit)Call("CreateUnit", containerCard, At(containerPos), boxer, null, false, false)!;
    var before = units.Keys.ToHashSet();
    var trigger = ((UnitDataLandmine)containerCard.Data!).TriggerEffect!;
    var impact = container.CreateImpactDataExact(container.GetMidpoint(), container.GetMidpoint(), Vector3s.Zero, false, containerCard.Key);
    Call("ApplyInstEffect", container.GetSelfSource(impact), Array.Empty<Unit>(), trigger, impact, null, null, null, true);
    var fresh = units.Values.Single(u => !before.Contains(u.Id) && u.Key == wellCard.Key);
    Console.WriteLine($"INFO {variant}: container trigger point {container.GetMidpoint()}, spawned well at {fresh.Transform.Position}");
    return (fresh, container.GetMidpoint());
});
Check(Vector3.Distance(spawned.fresh.Transform.Position, spawned.Item2) <= 1.01f && spawned.fresh.Transform.Position.Y >= FloorTop,
    "the container spawns the well where it triggered, above the floor");
Check(spawned.fresh.Team == TeamType.Team1, "the spawned well belongs to the Boxer's team");

Check(!boxer.ActiveEffects.Any(e => e.Key == gravityPull), "our own well does not pull our Boxer");

OnZone(() =>
{
    Call("CreateUnit", jinxCard, At(wellPos + new Vector3(0, 0.5f, 0)), genie, null, false, false);
    Ticks(10);
    return true;
});
if (variant == "live")
{
    // Live cards: the sizeless well never enters the unit octree, so no area effect can find it.
    Check(!well.ActiveEffects.Any(e => e.Key == confusion), "live cards: Jinx's aura cannot reach the sizeless well");
    var behavior0 = (AbilityBehaviorCast)surge.Behavior!;
    OnZone(() =>
    {
        var impact0 = boxer.CreateImpactDataExact(boxer.GetMidpoint(), boxer.GetMidpoint(), Vector3s.Zero, false, surge.Key);
        Call("ApplyInstEffect", boxer.GetSelfSource(impact0), new Unit[] { boxer }, behavior0.HitEffect, impact0, null, null, null, true);
        Ticks(2);
        return true;
    });
    Check(!well.IsBuff(BuffType.Disabled), "live cards: Power Surge cannot reach the sizeless well either");
    zone.Stop();
    Console.WriteLine($"BNL_JINX_SURGE_FIXTURE_LIVE_REPRODUCED checks={checks}");
    return;
}
Check(well.ActiveEffects.Any(e => e.Key == confusion), "enemy Jinx confuses our well");
Check(well.Team == TeamType.Team2, "the jinxed well switches to the Genie's team");
Check(boxer.ActiveEffects.Any(e => e.Key == gravityPull), "the jinxed well now pulls our Boxer");

OnZone(() =>
{
    var behavior = (AbilityBehaviorCast)surge.Behavior!;
    var impact = boxer.CreateImpactDataExact(boxer.GetMidpoint(), boxer.GetMidpoint(), Vector3s.Zero, false, surge.Key);
    Call("ApplyInstEffect", boxer.GetSelfSource(impact), new Unit[] { boxer }, behavior.HitEffect, impact, null, null, null, true);
    Ticks(10);
    return true;
});
Check(well.IsBuff(BuffType.Disabled), "our Power Surge disables the jinxed well");
Check(!boxer.ActiveEffects.Any(e => e.Key == gravityPull), "the disabled well stops pulling our Boxer");

zone.Stop();
Console.WriteLine($"BNL_JINX_SURGE_FIXTURE_OK checks={checks}");

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
