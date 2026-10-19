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
var healingAura = new CardEffect { Id = GenieHealingVisualRegistration.AuraId, Positive = true,
    Effect = new ConstEffectAura { OuterRadius = 4, Interval = .2f, IntervalEffects = [new InstEffectHeal { PlayerHeal = 1 }] } };
((ServerCatalogue)Databases.Catalogue).Replicate([
    gear, hero, orb, sprayImpact, stationRegen, staffRegen, mode, healingAura, new CardGameMode { Id = "game_mode_custom" },
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

var updater = (UnitUpdater)typeof(GameZone).GetField("_defaultUnitUpdater", Any)!.GetValue(zone)!;
Unit Orb(uint id, uint owner) => new(id, new UnitInit { Key = orb.Key, OwnerId = owner, Team = TeamType.Team1 }, updater);
var first = Orb(1001, 1);
var second = Orb(1002, 1);
var otherGenie = Orb(1003, 3);
var auraKey = new Key("effect_hero_djinn_heavy_orb_heal_aura");
var apply = typeof(Unit).GetMethod("ApplyAuraInterval", Any)!;
long Tick(double seconds) => (long)Math.Round(seconds * System.Diagnostics.Stopwatch.Frequency);
void HealOrb(Unit caster, Unit target, double seconds, Key? key = null) =>
    apply.Invoke(caster, [key ?? auraKey, 0.2f, new[] { target }, new InstEffectHeal { PlayerHeal = 1 }, Tick(seconds)]);
float Hp(Unit target) => target.HealthPercentage * 100;
bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.001f;
OnZone(() =>
{
    teammate.UpdateData(new UnitUpdate { Health = 40 });
    healer.UpdateData(new UnitUpdate { Health = 40 });
    calls.Clear();
    HealOrb(first, teammate, 0);
    Check(Near(Hp(teammate), 41), "single orb heals normally");
    HealOrb(second, teammate, 0);
    Check(Near(Hp(teammate), 41), "same Genie's simultaneous second orb does not stack");
    HealOrb(otherGenie, teammate, 0.1);
    Check(Near(Hp(teammate), 41), "different Genie's staggered orb does not stack");
    HealOrb(first, teammate, 0.199);
    Check(Near(Hp(teammate), 42), "normal timer jitter does not halve single-orb healing");
    HealOrb(second, teammate, 0.25);
    Check(Near(Hp(teammate), 42), "overlap cannot add a tick after the active orb tick");
    HealOrb(otherGenie, teammate, 0.4);
    Check(Near(Hp(teammate), 43), "remaining orb takes over after original leaves/expires");
    HealOrb(first, teammate, 0.41);
    Check(Near(Hp(teammate), 43), "old orb cannot add a tick after handoff");
    HealOrb(first, healer, 0.41);
    Check(Near(Hp(healer), 41), "recipients have independent healing cadence including self");
    HealOrb(second, teammate, 0.42, new Key("fixture_other_healing_aura"));
    Check(Near(Hp(teammate), 44), "unrelated healing auras remain additive");
    HealOrb(second, teammate, 0.45);
    Check(Near(Hp(teammate), 44), "other healing cannot reset Genie cadence");
    HealOrb(first, teammate, 10);
    Check(Near(Hp(teammate), 45), "reentering after a gap heals immediately");
    Check(calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!)
        .Any(i => i.CasterPlayerId == 3 && i.HitUnits!.Contains(teammate.Id)), "handoff keeps actual healer attribution");
    return true;
});
// Five ticks per second, even with many staggered overlapping sources.
OnZone(() =>
{
    teammate.UpdateData(new UnitUpdate { Health = 40 });
    for (int tick = 0; tick < 50; tick++)
    {
        double time = 20 + tick * 0.2;
        HealOrb(first, teammate, time);
        HealOrb(second, teammate, time + 0.05);
        HealOrb(otherGenie, teammate, time + 0.1);
    }
    Check(Near(Hp(teammate), 90), "ten seconds of three overlapping orbs heals 50 HP, not 150");
    return true;
});
OnZone(() =>
{
    var key = GenieHealingVisualRegistration.RecipientKey;
    var marker = Databases.Catalogue.GetCard<CardEffect>(key)!;
    var aura = (ConstEffectAura)healingAura.Effect!;
    Check(aura.ConstantEffects!.Count(k => k == key) == 1, "healing aura includes recipient marker");
    GenieHealingVisualRegistration.RegisterAura(healingAura);
    Check(aura.ConstantEffects!.Count(k => k == key) == 1, "registration is idempotent");
    Check(marker.Effect == null && marker.Duration == null && marker.PrefabPlayer == null && marker.PrefabUnit == null,
        "marker has no gameplay buffs, timer or unknown prefab for older clients");
    teammate.UpdateData(new UnitUpdate { Health = 100 });
    teammate.AddEffects([new ConstEffectInfo(key)], first.Team, first.GetSelfSource());
    Check(teammate.ActiveEffects.Any(e => e.Key == key) && Near(Hp(teammate), 100), "full-health recipient has visual without healing or damage");
    teammate.AddEffects([new ConstEffectInfo(key)], second.Team, second.GetSelfSource());
    Check(teammate.ActiveEffects.Count(e => e.Key == key) == 1, "overlapping orbs share a single visual");
    teammate.RemoveEffects([new ConstEffectInfo(key)], first.Team, first.GetSelfSource());
    Check(teammate.ActiveEffects.Any(e => e.Key == key), "leaving one overlapping orb retains visual");
    teammate.RemoveEffects([new ConstEffectInfo(key)], second.Team, second.GetSelfSource());
    Check(!teammate.ActiveEffects.Any(e => e.Key == key), "leaving last orb clears visual");
    first.AddEffects([new ConstEffectInfo(healingAura.Key)], first.Team, first.GetSelfSource());
    first.UnitsInAuraSinceLastUpdate[aura] = [teammate];
    teammate.AddEffects([new ConstEffectInfo(key)], first.Team, first.GetSelfSource());
    first.RemoveEffects([new ConstEffectInfo(healingAura.Key)], first.Team, first.GetSelfSource());
    Check(!teammate.ActiveEffects.Any(e => e.Key == key), "removing aura cleans recipient visual");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_GENIE_ORB_HEALING_FIXTURE_OK checks={checks}");

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
