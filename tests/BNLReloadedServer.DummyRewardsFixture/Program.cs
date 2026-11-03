// Real-catalogue end-to-end checks for test dummies: spawn through the zone, damage through Unit.TakeDamage so the
// zone's damage/kill callbacks run, then read bricks, loot units and life steal off the real units.
// Usage: dotnet run -- <live-cards.json>
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0, failures = 0;
void Check(bool pass, string name) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); }

var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(cards);
var hero = Catalogue.Key("unit_hero_trondson");
var mode = Catalogue.Key("game_mode_custom");

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1), (3u, TeamType.Team2) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero, Nickname = $"p{id}" };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(32, 16, 32),
    BlocksData = new byte[32 * 16 * 32 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(8, 4, 8) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(24, 4, 24) }]
};
var service = Stub<IServiceZone>();
var zone = new GameZone(service, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] a) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, a);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
}
var units = (IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!;
var group = (CustomGamePlayerGroup)RuntimeHelpers.GetUninitializedObject(typeof(CustomGamePlayerGroup));
var info = new CustomGameInfo { Status = CustomGameStatus.Match, TestDummies = true, ResourceCap = 10000 };
typeof(CustomGamePlayerGroup).GetField("<GameInfo>k__BackingField", Any)!.SetValue(group, info);
typeof(GameZone).GetField("_gameInitiator", Any)!.SetValue(zone, group);

OnZone(() =>
{
    var shooter = (Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!;
    var ally = (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!;
    var enemy = (Unit)Call("CreatePlayerUnit", 3u, Stub<IServiceZone>())!;
    foreach (var u in new[] { shooter, ally, enemy }) { u.IsActive = true; u.Resource = 0; }
    var staff = Catalogue.Key("gear_trondson_sonic_staff");
    ImpactData Hit(Unit by) => new() { CasterPlayerId = by.PlayerId, CasterUnitId = by.Id, SourceKey = staff, ShotPos = by.Transform.Position };
    DamageData Damage(float hp) => new(hp, hp, hp, hp, hp, hp, hp, hp, false, false, false, false);
    int Loot() => units.Values.Count(u => u.Key == Catalogue.Key("unit_pickup_hero_loot") && !u.IsDead);

    Unit? Spawn(uint by, int hp)
    {
        var before = units.Keys.ToHashSet();
        zone.ReceivedCustomTestDummy(by, hp);
        return units.Values.FirstOrDefault(u => !before.Contains(u.Id) && u.IsTestDummy);
    }

    // Hero test dummy (custom setting).
    var dummy = Spawn(1, 100);
    Check(dummy != null && dummy.Team == TeamType.Team2, "custom dummy spawns as the spawner's enemy");
    int lootBefore = Loot();
    dummy!.TakeDamage(Damage(100), Hit(shooter), false, shooter, shooter.Team);
    Check(dummy.IsDead, "real damage kills the dummy");
    Check(shooter.Resource >= 50, $"killer gets the dummy kill bricks (got {shooter.Resource})");
    Check(ally.Resource >= 50, $"ally gets team kill bricks (got {ally.Resource})");
    Check(Loot() > lootBefore, "dummy drops the hero health pickup");

    // Life steal on a dummy.
    var target = Spawn(1, 1000)!;
    shooter.UpdateData(new UnitUpdate { Health = 20, Buffs = new Dictionary<BuffType, float> { { BuffType.LifeSteal, .5f } } });
    float hp0 = shooter.HealthPercentage;
    target.TakeDamage(Damage(20), Hit(shooter), false, shooter, shooter.Team);
    Check(shooter.HealthPercentage > hp0, "life steal heals from dummy damage");

    // 100 ms life steal cooldown, on a fake monotonic clock.
    long clock = Environment.TickCount64 + 10_000;
    typeof(Unit).GetField("LifeStealClock", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, (Func<long>)(() => clock));
    float Hp() => shooter.HealthPercentage;
    shooter.UpdateData(new UnitUpdate { Health = 20 });
    float h0 = Hp(); target.TakeDamage(Damage(20), Hit(shooter), false, shooter, shooter.Team);
    float h1 = Hp(); clock += 50; target.TakeDamage(Damage(20), Hit(shooter), false, shooter, shooter.Team);
    float h2 = Hp(); clock += 50; target.TakeDamage(Damage(20), Hit(shooter), false, shooter, shooter.Team);
    float h3 = Hp();
    Check(h1 > h0, "first hit after the cooldown steals life");
    Check(h2 == h1, "a hit 50 ms later steals nothing");
    Check(h3 > h2, "a hit 100 ms after the last steal heals again");

    // Instant player heal (Trondson's 15 HP) lands on a damaged dummy, healed by the dummy's own team.
    var healTarget = Spawn(3, 1000) ?? throw new Exception("team 2 could not spawn");
    var healTargetTeamHealer = shooter; // player 3 spawned it, so it is on Team1 with the shooter
    healTarget.TakeDamage(Damage(500), Hit(enemy), false, enemy, enemy.Team);
    float before = healTarget.HealthPercentage;
    var healImpact = Hit(healTargetTeamHealer);
    Call("ApplyInstEffect", healTargetTeamHealer.GetSelfSource(healImpact), new[] { healTarget }, new InstEffectHeal { PlayerHeal = 15 }, healImpact, null, null, null, true);
    Check(Math.Abs((healTarget.HealthPercentage - before) * 1000 - 15) < .01f, $"instant 15 HP heal lands on a dummy ({(healTarget.HealthPercentage - before) * 1000:0.##})");

    // Map-editor device dummy with the CDB kill_reward/loot the owner set.
    var device = (Unit)Call("CreateUnit", Catalogue.Key("unit_device_generic_test_dummy_player").GetCard<CardUnit>(),
        ZoneTransformHelper.ToZoneTransform(new Vector3(10, 4, 10), Quaternion.Identity), enemy, null, false, true)!;
    shooter.Resource = 0; ally.Resource = 0; lootBefore = Loot();
    device.TakeDamage(Damage(10_000_000), Hit(shooter), false, shooter, shooter.Team);
    Check(device.IsDead, "device dummy dies");
    Check(shooter.Resource >= 50, $"device dummy kill pays CDB player_reward (got {shooter.Resource})");
    Check(Loot() > lootBefore, "device dummy drops its CDB loot");

    // An immunity applied while its effect is active ends that effect (real CDB slow and slow immunity).
    var slow = new ConstEffectInfo(Catalogue.Key("effect_status_slow_glue"), 5f);
    shooter.AddEffect(slow, enemy.Team, enemy.GetSelfSource(Hit(enemy)));
    Check(shooter.ActiveEffects.Any(e => e.Key == slow.Key), "glue slow lands on the hero");
    shooter.AddEffect(new ConstEffectInfo(Catalogue.Key("effect_status_slow_immunity")), shooter.Team, shooter.GetSelfSource());
    Check(!shooter.ActiveEffects.Any(e => e.Key == slow.Key), "slow immunity removes the slow already applied");
    shooter.AddEffect(new ConstEffectInfo(Catalogue.Key("effect_status_slow_glue"), 5f), enemy.Team, enemy.GetSelfSource(Hit(enemy)));
    Check(!shooter.ActiveEffects.Any(e => e.Key == slow.Key), "and still blocks a new one");
    return true;
});
zone.Stop();
Console.WriteLine($"DUMMY_REWARDS checks={checks} failures={failures}");
return failures == 0 ? 0 : 1;

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
