// Avalanche's marker landing slows enemy players within 5 blocks by 30% for 2 seconds and nobody else.
// Loads the real migrated cards (ability_abe_avalanche, effect_hero_abe_avalanche_slow and
// impact_abe_snow_thrower_splash) from a catalogue-array JSON file, then applies the ability's
// hit effect through GameZone.ApplyInstEffect exactly as a landed ability projectile does.
// Run against the pre-migration cards it must fail: that is the ablation check.
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
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

using var cardsJson = JsonDocument.Parse(File.ReadAllText(args[0]));
var realCards = cardsJson.RootElement.EnumerateArray()
    .Select(doc => JsonSerializer.Deserialize<Card>(doc.GetRawText(), JsonHelper.DefaultSerializerSettings)!)
    .ToList();

var gear = new CardGear { Id = "fixture_as_gear" };
var hero = new CardUnit
{
    Id = "fixture_as_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 160, HealthType = HealthType.Player } }
};
var mode = new CardGameMode { Id = "game_mode_friendly" };
((ServerCatalogue)Databases.Catalogue).Replicate([
    .. realCards, gear, hero, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_as_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

var ability = Databases.Catalogue.GetCard<CardAbility>(new Key("ability_abe_avalanche"))!;
var hitEffect = ((AbilityBehaviorCast)ability.Behavior!).HitEffect!;
var slowKey = new Key("effect_hero_abe_avalanche_slow");
var landingImpact = new Key("impact_abe_snow_thrower_splash");

var players = new System.Collections.Concurrent.ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1), (3u, TeamType.Team2), (4u, TeamType.Team2), (5u, TeamType.Team2) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
const int Side = 32;
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(Side, Side, Side),
    BlocksData = new byte[Side * Side * Side * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(6, 4, 6) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(24, 4, 24) }]
};
var service = Stub<IServiceZone>();
var zone = new GameZone(service, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] callArgs) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, callArgs);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
var octree = typeof(GameZone).GetField("_unitOctree", Any)!.GetValue(zone)!;
var octreeRemove = octree.GetType().GetMethod("Remove", [typeof(Unit)])!;

// Marker lands on the floor at (16, 4, 16); the normal lifts the effect centre to y = 5.
var landing = new Vector3(16, 4, 16);
Unit Place(uint playerId, Vector3 feet) => OnZone(() =>
{
    var unit = (Unit)Call("CreatePlayerUnit", playerId, Stub<IServiceZone>())!;
    while ((bool)octreeRemove.Invoke(octree, [unit])!) { }
    unit.Transform = new ZoneTransform { Position = feet };
    Call("AddUnitToOctree", unit, unit.Transform);
    return unit;
});
var caster = Place(1, landing + new Vector3(1, 0, 0));
var ally = Place(2, landing + new Vector3(0, 0, 2));
var enemyNear = Place(3, landing + new Vector3(3, 0, 0));
var enemyEdge = Place(4, landing + new Vector3(0, 0, 4.5f));
var enemyFar = Place(5, landing + new Vector3(8, 0, 0));
var calls = ((StubProxy)(object)service).Calls;

var started = DateTimeOffset.Now.ToUnixTimeMilliseconds();
var impacts = OnZone(() =>
{
    calls.Clear();
    var impactData = new ImpactData
    {
        InsidePoint = landing, Normal = new Vector3s(0, 1, 0), ShotPos = landing,
        CasterPlayerId = caster.PlayerId, CasterUnitId = caster.Id, SourceKey = ability.Key, HitUnits = []
    };
    Call("ApplyInstEffect", new UnitSource(caster), Array.Empty<Unit>(), hitEffect, impactData, null, null, null, true);
    return calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!).ToList();
});

ConstEffectInfo? Slow(Unit unit) => unit.ActiveEffects.FirstOrDefault(e => e.Key == slowKey);
foreach (var (unit, name) in new[] { (enemyNear, "enemy 3 blocks away"), (enemyEdge, "enemy 4.5 blocks away") })
{
    Check(Slow(unit) is not null, name + " receives the Avalanche slow");
    Check(Near(unit.GetBuff(BuffType.RunSpeed), -0.3f) && Near(unit.GetBuff(BuffType.SprintSpeed), -0.3f) &&
          Near(unit.GetBuff(BuffType.SwimSpeed), -0.3f) && Near(unit.GetBuff(BuffType.JumpHeight), -0.3f),
        name + " run, sprint, swim and jump are 30% slower");
    Check(Near(unit.GetBuff(BuffType.DashTime), 0.15f) && Near(unit.GetBuff(BuffType.DashDistance), -0.15f),
        name + " dash carries the scaled 15% penalty");
    var remaining = (long)Slow(unit)!.TimestampEnd!.Value - started;
    Check(remaining is >= 1900 and <= 2200, name + $" slow ends after 2 seconds (measured {remaining} ms)");
}
Check(Slow(enemyFar) is null && Near(enemyFar.GetBuff(BuffType.RunSpeed), 0), "enemy 8 blocks away is untouched");
Check(Slow(ally) is null && Near(ally.GetBuff(BuffType.RunSpeed), 0), "allies in the area are untouched");
Check(Slow(caster) is null && Near(caster.GetBuff(BuffType.RunSpeed), 0), "the Yeti who threw it is untouched");
Check(impacts.Count == 1 && impacts[0].Impact == landingImpact, "landing plays the Snow Thrower splash impact once");
Check(impacts[0].HitUnits?.Order().SequenceEqual(new[] { enemyNear.Id, enemyEdge.Id }.Order()) is true,
    "impact names exactly the slowed enemies");
var spawned = OnZone(() => ((IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!)
    .Values.Count(u => u.PlayerId is null));
Check(spawned == 0, "no avalanche shower unit is spawned");

zone.Stop();
Console.WriteLine($"BNL_AVALANCHE_SLOW_FIXTURE_OK checks={checks}");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{
    var proxy = DispatchProxy.Create<T, StubProxy>();
    ((StubProxy)(object)proxy).Returns = returns ?? [];
    return proxy;
}

public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    public readonly List<(string Name, object?[] Args)> Calls = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        lock (Calls) Calls.Add((method.Name, args ?? []));
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
