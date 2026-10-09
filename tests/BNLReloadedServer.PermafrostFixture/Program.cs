// Loads migrated production cards; exercises authoritative area logic on a real GameZone.
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

using var cardsJson = JsonDocument.Parse(File.ReadAllText(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "cards.json")));
var realCards = cardsJson.RootElement.EnumerateArray()
    .Where(doc => doc.TryGetProperty("category", out _))
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
    .. realCards.Where(c => c.Id is not ("global_logic" or "game_mode_friendly" or "game_mode_custom") && c is not CardBlock), gear, hero, mode, new CardGameMode { Id = "game_mode_custom" },
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
var unbuffered = Stub<IServiceZone>();
var zone = new GameZone(service, unbuffered, Stub<IBuffer>(), Stub<ISender>(), map,
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

OnZone(() =>
{
    void Tick(DateTimeOffset at) => Call("TickAvalancheAreas", at);
    void Move(Unit unit, Vector3 feet) => unit.Transform = new ZoneTransform { Position = feet };
    Unit Cast(string key)
    {
        var castAbility = Databases.Catalogue.GetCard<CardAbility>(new Key(key))!;
        var impact = new ImpactData { InsidePoint=landing, Normal=new Vector3s(0,1,0), ShotPos=landing,
            CasterPlayerId=caster.PlayerId, CasterUnitId=caster.Id, SourceKey=castAbility.Key, HitUnits=[] };
        Call("ApplyInstEffect", new UnitSource(caster), Array.Empty<Unit>(), ((AbilityBehaviorCast)castAbility.Behavior!).HitEffect!, impact, null, null, null, true);
        return ((IDictionary<uint,Unit>)typeof(GameZone).GetField("_units",Any)!.GetValue(zone)!).Values
            .Where(u => u.PlayerId is null).MaxBy(u=>u.Id)!;
    }
    var areaSlow = new Key("effect_abe_avalanche_area_slow");
    var root = new Key("effect_abe_permafrost_root");
    bool Slowed(Unit u) => u.ActiveEffects.Any(e=>e.Key==areaSlow);
    bool Rooted(Unit u) => u.ActiveEffects.Any(e=>e.Key==root);
    void ClearRoot(Unit u) { u.ActiveEffects = u.ActiveEffects.RemoveAll(e=>e.Key==root); }
    var normal=Cast("ability_abe_avalanche");
    var t=normal.CreationTime.AddMilliseconds(10);
    Tick(t);
    Check(normal.Key==new Key("unit_abe_avalanche_area"), "base cast creates replicated area");
    Check(normal.UnitCard!.Lifetime==4 && normal.UnitCard.Data is UnitDataCommon, "area lifetime 4s with no shower");
    Check(Slowed(enemyNear)&&Slowed(enemyEdge), "base slow includes 3 and 4.5 block enemies");
    Check(!Slowed(ally)&&!Slowed(caster)&&!Slowed(enemyFar), "allies, caster and far enemies excluded");
    Check(Near(enemyNear.GetBuff(BuffType.RunSpeed),-.3f), "30 percent slow");
    Tick(t.AddSeconds(2));
    Check(!Rooted(enemyNear), "normal Avalanche never roots");
    Tick(normal.CreationTime.AddSeconds(4));
    Check(!Slowed(enemyNear)&&!Slowed(enemyEdge), "normal slow removed exactly at four seconds");
    normal.IsDead=true;
    var area=Cast("ability_abe_avalanche_permafrost");
    t=area.CreationTime.AddMilliseconds(10); Tick(t);
    Check(area.Key==new Key("unit_abe_permafrost_area"), "perk ability creates Permafrost area");
    Check(Slowed(enemyNear)&&!Slowed(enemyEdge), "Permafrost radius is 4 rather than 5");
    Tick(area.CreationTime.AddMilliseconds(1999));
    Check(!Rooted(enemyNear), "no root before two continuous seconds");
    Tick(t.AddSeconds(2));
    Check(Rooted(enemyNear)&&Near(enemyNear.GetBuff(BuffType.Root),1), "root at two seconds");
    var rootInfo=enemyNear.ActiveEffects.First(e=>e.Key==root);
    var remaining=(long)rootInfo.TimestampEnd!.Value-DateTimeOffset.Now.ToUnixTimeMilliseconds();
    Check(remaining is >450 and <=500, "root lasts half a second");
    Check(Near(enemyNear.GetBuff(BuffType.Disabled),0), "root does not disable weapons");
    ClearRoot(enemyNear); Tick(t.AddSeconds(2.6));
    Check(!Rooted(enemyNear), "no repeated root while staying inside");
    Move(enemyNear,landing+new Vector3(6,0,0)); Tick(t.AddSeconds(2.7));
    Check(!Slowed(enemyNear), "slow removed on exit");
    Move(enemyNear,landing+new Vector3(3,0,0)); Tick(t.AddSeconds(2.8));
    Check(Slowed(enemyNear)&&!Rooted(enemyNear), "reentry slows without a second root");
    Tick(area.CreationTime.AddSeconds(4));
    Check(!Slowed(enemyNear)&&!Rooted(enemyNear), "Permafrost stops at four seconds");
    area.IsDead=true;

    // Continuous exposure resets on exit; late arrivals get their own timer.
    area=Cast("ability_abe_avalanche_permafrost"); t=area.CreationTime.AddMilliseconds(10); Tick(t);
    Move(enemyNear,landing+new Vector3(6,0,0)); Tick(t.AddSeconds(1));
    Move(enemyNear,landing+new Vector3(3,0,0)); Tick(t.AddSeconds(1.1));
    Move(enemyFar,landing+new Vector3(2,0,0)); Tick(t.AddSeconds(1.2));
    Tick(t.AddSeconds(2.1)); Check(!Rooted(enemyNear)&&!Rooted(enemyFar), "exit resets dwell; late entrant does not inherit timer");
    Tick(t.AddSeconds(3.1)); Check(Rooted(enemyNear)&&!Rooted(enemyFar), "reentered enemy roots after fresh two seconds");
    Tick(t.AddSeconds(3.2)); Check(Rooted(enemyFar), "late entrant roots on its own two second timer");
    ClearRoot(enemyNear); ClearRoot(enemyFar);
    area.IsDead=true; Tick(t.AddSeconds(3.3));
    Check(!Slowed(enemyNear)&&!Slowed(enemyFar), "early area destruction clears its slow");

    // Overlap keeps one 30% slow until the last source disappears.
    var first=Cast("ability_abe_avalanche"); var second=Cast("ability_abe_avalanche");
    Tick(DateTimeOffset.Now);
    Check(Near(enemyNear.GetBuff(BuffType.RunSpeed),-.3f), "overlapping areas do not multiply slow");
    first.IsDead=true; Tick(DateTimeOffset.Now);
    Check(Slowed(enemyNear), "one expiring area cannot remove another area's slow");
    second.IsDead=true; Tick(DateTimeOffset.Now);
    Check(!Slowed(enemyNear), "last source expiry removes slow");
    // Exact boundary uses player midpoint and excludes vertically distant enemies.
    Move(enemyNear,landing+new Vector3(4,.05f,0)); Move(enemyFar,landing+new Vector3(0,6,0));
    area=Cast("ability_abe_avalanche_permafrost");
    Move(enemyNear,area.Transform.Position+new Vector3(4,-.95f,0)); Tick(DateTimeOffset.Now);
    Check(Slowed(enemyNear)&&!Slowed(enemyFar), "exact radius included; vertical distance respected");
    Move(enemyNear,area.Transform.Position+new Vector3(4.01f,-.95f,0)); Tick(DateTimeOffset.Now);
    Check(!Slowed(enemyNear), "just outside radius excluded");
    area.IsDead=true; Tick(DateTimeOffset.Now);
    // Native expiry path must drop the replicated unit (and therefore its VFX).
    Move(enemyNear,landing+new Vector3(1,0,0));
    area=Cast("ability_abe_avalanche_permafrost");
    Check(Slowed(enemyNear), "native lifecycle starts slow");
    typeof(Unit).GetField("_expirationTime",Any)!.SetValue(area,DateTimeOffset.Now.AddMilliseconds(-1));
    area.CreationTime=DateTimeOffset.Now.AddSeconds(-4.1);
    ((Action)Call("OnTick",1UL)!)();
    Check(!Slowed(enemyNear), "native zone tick clears expired area slow");
    Check(!((IDictionary<uint,Unit>)typeof(GameZone).GetField("_units",Any)!.GetValue(zone)!).ContainsKey(area.Id),
        "native unit lifetime drops area from registry");
    Check(((StubProxy)(object)unbuffered).Calls.Any(c=>c.Name=="SendUnitDrop" && (uint)c.Args[0]! == area.Id), "area drop replicated to clients");
    Check(new Key("ability_abe_avalanche").ConvertAbility([new Key("perk_hero_abe_permafrost")]) ==
        new Key("ability_abe_avalanche_permafrost"), "equipping the perk selects its ability");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_PERMAFROST_FIXTURE_OK checks={checks}");

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

