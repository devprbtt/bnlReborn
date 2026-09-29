// Every heal on a player with a player behind it (hero, projectile, their station or block) sends a card-less
// impact naming that player, with the amount in ShotPos.X, so clients can show who is healing them and how much,
// several healers at once. Drives GameZone.ApplyInstEffect and Unit.ApplyBuffEffects with real units and
// records SendImpact.
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
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

var gear = new CardGear { Id = "fixture_ha_gear" };
var hero = new CardUnit
{
    Id = "fixture_ha_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var orb = new CardUnit
{
    Id = "fixture_ha_orb", Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center, Lifetime = 60,
    Health = new UnitHealth { Health = new Health { MaxHealth = 50, HealthType = HealthType.World } },
    Data = new UnitDataProjectile { MaxSpeed = 0, Acceleration = 0, TriggerRadius = 0.1f, CollideWith = RelativeTeamType.Friendly }
};
var sprayImpact = new CardImpact { Id = "fixture_ha_spray" };
CardEffect Regen(string id, float rate) => new()
{
    Id = id, Positive = true,
    Effect = new ConstEffectBuff { Buffs = new Dictionary<BuffType, float> { [BuffType.HealthRegen] = rate } }
};
var stationRegen = Regen("fixture_ha_station_regen", 10f);
var staffRegen = Regen("fixture_ha_staff_regen", 5f);
var mode = new CardGameMode { Id = "game_mode_friendly" };
((ServerCatalogue)Databases.Catalogue).Replicate([
    gear, hero, orb, sprayImpact, stationRegen, staffRegen, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_ha_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1), (3u, TeamType.Team1) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 16, 16),
    BlocksData = new byte[16 * 16 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(4, 4, 4) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(12, 4, 12) }]
};
var service = Stub<IServiceZone>();
var zone = new GameZone(service, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, args);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
var units = (IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!;
var (healer, teammate) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));
var thirdHealer = OnZone(() => (Unit)Call("CreatePlayerUnit", 3u, Stub<IServiceZone>())!);
var calls = ((StubProxy)(object)service).Calls;

// Applies one heal tick and returns the impacts it sent.
List<ImpactData> Heal(EffectSource source, Unit target, float amount, Key? impact = null, float startHealth = 40f)
{
    return OnZone(() =>
    {
        target.UpdateData(new UnitUpdate { Health = startHealth });
        calls.Clear();
        var effect = new InstEffectHeal { PlayerHeal = amount, Impact = impact };
        var impactData = new ImpactData { CasterPlayerId = healer.PlayerId, CasterUnitId = healer.Id, SourceKey = gear.Key, HitUnits = [] };
        Call("ApplyInstEffect", source, new[] { target }, effect, impactData, null, null, null, true);
        return calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!).ToList();
    });
}
bool IsAttribution(ImpactData i, Unit by) => i.Impact is null && i.CasterPlayerId == by.PlayerId &&
    i.CasterUnitId == by.Id && i.HitUnits is [var hit] && hit == teammate.Id;
bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

var direct = Heal(new UnitSource(healer), teammate, 10);
Check(direct.Count == 1 && IsAttribution(direct[0], healer), "a hero's own heal on a hurt teammate sends one card-less attribution impact");
Check(Near(direct[0].ShotPos.X, 10f), "the attribution carries the amount healed (10)");
Check(direct[0].SourceKey == gear.Key, "the attribution carries the heal's source gear");

Check(Heal(new UnitSource(healer), teammate, 10, startHealth: 100f).Count == 0, "no attribution when the teammate is at full health");
Check(Heal(new UnitSource(healer), healer, 10).Count == 0, "no attribution for healing yourself");

var orbUnit = OnZone(() =>
{
    var before = units.Keys.ToHashSet();
    var origin = new Vector3(6, 8, 6);
    Call("CreateProjectileUnit", orb.Key, 0f, new ShotData { TargetPos = origin + Vector3.UnitX }, origin, healer);
    return units.Values.Single(u => !before.Contains(u.Id) && u.Key == orb.Key);
});
var fromOrb = Heal(new UnitSource(orbUnit), teammate, 2);
Check(fromOrb.Count == 1 && IsAttribution(fromOrb[0], healer), "a heal from the hero's projectile (orb, globe) is attributed to its owner");

var fromBlock = Heal(new BlockSource(new Vector3s(4, 4, 4), new Block()), teammate, 10);
Check(fromBlock.Count == 1 && IsAttribution(fromBlock[0], healer) && Near(fromBlock[0].ShotPos.X, 10f),
    "a heal from a block or station a player placed is attributed to that player");

var sprayed = Heal(new UnitSource(healer), teammate, 4.2f, sprayImpact.Key);
Check(sprayed.Count == 2 && sprayed.Any(i => i.Impact == sprayImpact.Key) &&
      sprayed.Any(i => IsAttribution(i, healer) && Near(i.ShotPos.X, 4.2f)),
    "a heal with its own impact card (Caulk Gun spray) keeps it and adds the attribution with the amount");

// Regeneration from two players' auras at once: a station (10/s) placed by one and Trondson-style regen (5/s)
// from another. One tick is split 2:1 between them and adds up to the health actually gained.
Unit Owned(Unit owner) => OnZone(() =>
{
    var before = units.Keys.ToHashSet();
    var origin = new Vector3(6, 8, 6);
    Call("CreateProjectileUnit", orb.Key, 0f, new ShotData { TargetPos = origin + Vector3.UnitX }, origin, owner);
    return units.Values.Single(u => !before.Contains(u.Id) && u.Key == orb.Key);
});
var station = Owned(healer);
var staff = Owned(thirdHealer);
var (regenImpacts, gained) = OnZone(() =>
{
    teammate.UpdateData(new UnitUpdate { Health = 40f });
    teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)], TeamType.Team1, new UnitSource(station, station.CreateImpactData()));
    teammate.AddEffects([new ConstEffectInfo(staffRegen.Key)], TeamType.Team1, new UnitSource(staff, staff.CreateImpactData()));
    var before = teammate.HealthPercentage;
    calls.Clear();
    teammate.ApplyBuffEffects(1f);
    var impacts = calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!).ToList();
    return (impacts, (teammate.HealthPercentage - before) * 100f);
});
var fromStation = regenImpacts.Where(i => IsAttribution(i, healer)).Sum(i => i.ShotPos.X);
var fromStaff = regenImpacts.Where(i => IsAttribution(i, thirdHealer)).Sum(i => i.ShotPos.X);
Check(gained > 0 && regenImpacts.Count == 2, $"one regen tick healed {gained:0.##} and sent one attribution per healer");
Check(Near(fromStation + fromStaff, gained), "the regen attributions add up to the health gained");
Check(Near(fromStation, 2f * fromStaff), "the split follows each healer's regen rate (10 vs 5)");

// A heal station nobody owns (placed with the map): the attribution names the station unit, with no player.
var mapStation = OnZone(() =>
{
    var before = units.Keys.ToHashSet();
    var origin = new Vector3(8, 8, 8);
    Call("CreateProjectileUnit", orb.Key, 0f, new ShotData { TargetPos = origin + Vector3.UnitX }, origin, null);
    return units.Values.Single(u => !before.Contains(u.Id) && u.Key == orb.Key);
});
var (mapImpacts, mapGained) = OnZone(() =>
{
    teammate.RemoveEffects([new ConstEffectInfo(stationRegen.Key, null), new ConstEffectInfo(staffRegen.Key, null)],
        TeamType.Team1, new UnitSource(station, station.CreateImpactData()));
    teammate.RemoveEffects([new ConstEffectInfo(staffRegen.Key, null)], TeamType.Team1, new UnitSource(staff, staff.CreateImpactData()));
    teammate.UpdateData(new UnitUpdate { Health = 40f });
    teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)], TeamType.Team1, new UnitSource(mapStation, mapStation.CreateImpactData()));
    var before = teammate.HealthPercentage;
    calls.Clear();
    teammate.ApplyBuffEffects(1f);
    var impacts = calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!).ToList();
    return (impacts, (teammate.HealthPercentage - before) * 100f);
});
Check(mapStation.OwnerPlayerId is null && mapStation.PlayerId is null, "the map station has no owner");
Check(mapGained > 0 && mapImpacts.Count == 1 && mapImpacts[0].CasterPlayerId is null &&
      mapImpacts[0].CasterUnitId == mapStation.Id && mapImpacts[0].Impact is null &&
      mapImpacts[0].HitUnits is [var mapHit] && mapHit == teammate.Id && Near(mapImpacts[0].ShotPos.X, mapGained),
    "an unowned station's regen is attributed to the station unit with the amount and no player");

zone.Stop();
Console.WriteLine($"BNL_HEAL_ATTRIBUTION_FIXTURE_OK checks={checks}");

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
