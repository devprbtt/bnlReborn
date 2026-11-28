// Life steal: a living player with the LifeSteal buff is healed for buff x damage whenever it damages
// another player's unit; never for blocks/devices, itself, or while dead, and never above max health.
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
var clockField = typeof(Unit).GetField("LifeStealClock", BindingFlags.Static | BindingFlags.NonPublic)!;
long clock = 1000;
clockField.SetValue(null, (Func<long>)(() => clock));
var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var callbacks = constructor.GetParameters().Select(p =>
{
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray();
var updater = (UnitUpdater)constructor.Invoke(callbacks);
var catalogue = (ServerCatalogue)Databases.Catalogue;
var hero = new CardUnit { Id = "fixture_ls_hero", Data = new UnitDataPlayer(), Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } } };
var everyone = new EffectTargeting { AffectedTeam = RelativeTeamType.Both };
var steal = new CardEffect { Id = "fixture_ls_half", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.LifeSteal] = 0.5f } } };
var more = new CardEffect { Id = "fixture_ls_quarter", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.LifeSteal] = 0.25f } } };
catalogue.Replicate([hero, steal, more, new CardGlobalLogic { Id = "global_logic" }]);
Unit Create(uint id, TeamType team, uint? player = null) => new(id, new UnitInit { Key = hero.Key, Team = team, PlayerId = player ?? id, OwnerId = id }, updater);
var applies = typeof(BNLReloadedServer.ServerTypes.GameZone).GetMethod("OnHitApplies", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Applies(Unit a, Unit t) => (bool)applies.Invoke(null, new object[] { a, t })!;

var attacker = Create(1, TeamType.Team1);
var victim = Create(2, TeamType.Team2);
attacker.UpdateData(new UnitUpdate { Health = 40f });
victim.UpdateData(new UnitUpdate { Health = 100f });
Check(Math.Abs(attacker.HealthPercentage * 100f - 40f) < 0.01f, "attacker starts wounded at 40/100");
Check(Applies(attacker, victim) && attacker.LifeStealAmount(40f) == 0f, "no buff: on-hit qualifies but life steal pays nothing");
attacker.AddEffect(new ConstEffectInfo(steal.Key), attacker.Team, attacker.GetSelfSource());
Check(Applies(attacker, victim), "buffed living player hitting another player's unit qualifies");
Check(Math.Abs(attacker.LifeStealAmount(40f) - 20f) < 0.01f, "50% life steal returns half the damage");
attacker.RestoreLifeSteal(40f);
Check(attacker.Stats!.GetValueOrDefault(ScoreType.HealPlayerByHero) == 20f, "lifesteal contributes actual restored health to match healing");
Check(Math.Abs(attacker.HealthPercentage * 100f - 60f) < 0.01f, "the heal lands on the attacker");
clock += Unit.LifeStealCooldownMilliseconds;
attacker.RestoreLifeSteal(1000f);
Check(attacker.Stats!.GetValueOrDefault(ScoreType.HealPlayerByHero) == 60f, "overheal excluded from lifesteal healing stats");
attacker.RestoreLifeSteal(50f);
Check(attacker.Stats!.GetValueOrDefault(ScoreType.HealPlayerByHero) == 60f, "full-health lifesteal adds no healing stats");
Check(Math.Abs(attacker.HealthPercentage * 100f - 100f) < 0.01f, "life steal never exceeds max health");
attacker.AddEffect(new ConstEffectInfo(more.Key), attacker.Team, attacker.GetSelfSource());
Check(Math.Abs(attacker.LifeStealAmount(40f) - 30f) < 0.01f, "stacked life steal buffs add (75%)");
Check(!Applies(attacker, attacker), "self damage never steals");
var block = new Unit(3, new UnitInit { Key = hero.Key, Team = TeamType.Team2, PlayerId = null, OwnerId = 9 }, updater);
Check(!Applies(attacker, block), "damage to non-player units (blocks, devices) never steals");
attacker.Killed(attacker.CreateBlankImpactData());
Check(attacker.IsDead && !Applies(attacker, victim), "a dead attacker (lingering projectile) never steals");
var json = "{\"_id\":\"effect_fixture_ls\",\"category\":\"effect\",\"effect\":{\"type\":\"buff\",\"buffs\":{\"life_steal\":0.5,\"health_gain\":0.1}}}";
var parsed = System.Text.Json.JsonSerializer.Deserialize<Card>(json, JsonHelper.DefaultSerializerSettings) as CardEffect;
Check(parsed?.Effect is ConstEffectBuff { Buffs: { } buffs } && buffs.TryGetValue(BuffType.LifeSteal, out var v) && v == 0.5f && buffs.ContainsKey(BuffType.HealthGain),
    "CDB key \"life_steal\" deserializes to BuffType.LifeSteal");

// Only gear counts as a tool: turrets, devices, blocks and abilities that damage enemies do not heal.
var gear = new CardGear { Id = "fixture_ls_gear" };
var saucer = new CardUnit { Id = "fixture_ls_saucer", Data = new UnitDataProjectile() };
var turret = new CardUnit { Id = "fixture_ls_turret", Data = new UnitDataTurret() };
var ability = new CardAbility { Id = "fixture_ls_ability", Icon = "", KillscoreIcon = "", Prefab = "", Behavior = new AbilityBehaviorCast { Application = new AbilityApplicationSelf() }, Charges = new AbilityCharges { MaxCharges = 1, ChargeCooldown = 1 } };
var trapBlock = new CardBlock { Id = "fixture_ls_block" };
catalogue.Replicate(catalogue.All.Concat(new Card[] { gear, saucer, turret, ability, trapBlock }).ToList());
var isTool = typeof(BNLReloadedServer.ServerTypes.GameZone).GetMethod("IsToolDamage", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Tool(Key? key) => (bool)isTool.Invoke(null, new object[] { new ImpactData { SourceKey = key ?? Key.None } })!;
Check(Tool(gear.Key), "gear (weapon) damage is tool damage");
Check(Tool(saucer.Key), "a projectile unit fired from gear counts as tool damage");
Check(!Tool(turret.Key), "turret damage is not tool damage");
Check(!Tool(ability.Key), "ability damage is not tool damage");
Check(!Tool(trapBlock.Key), "trap block damage is not tool damage");
Check(!Tool(null), "damage with no source is not tool damage");

if (args is [var cataloguePath])
{
    catalogue.Replicate(File.ReadAllText(cataloguePath).Deserialize<List<Card>>()!);
    var rivetGun = new Key("gear_engineer_rivet_gun");
    var perk = new Key("effect_perk_life_steal_pos");
    var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
    players[1] = new PlayerLobbyState { PlayerId = 1, Team = TeamType.Team1, Hero = new Key("unit_hero_engineer"), Nickname = "Tony" };
    players[2] = new PlayerLobbyState { PlayerId = 2, Team = TeamType.Team2, Hero = new Key("unit_hero_sarge_stone"), Nickname = "Sarge" };
    players[3] = new PlayerLobbyState { PlayerId = 3, Team = TeamType.Team1, Hero = new Key("unit_hero_kira"), Nickname = "Kira" };
    var map = new MapData
    {
        Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(32, 16, 32),
        BlocksData = new byte[32 * 16 * 32 * 6].Zip(0).ToArray(),
        SpawnPoints =
        [
            new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(8, 4, 8) },
            new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(10, 4, 8) }
        ]
    };
    static T Stub<T>() where T : class => DispatchProxy.Create<T, ZoneProxy>();
    var service = Stub<IServiceZone>();
    var zone = new GameZone(service, service, Stub<IBuffer>(), Stub<ISender>(), map, Stub<IGameInitiator>(), players);
    var done = new TaskCompletionSource();
    zone.EnqueueAction(() =>
    {
        try
        {
            var create = typeof(GameZone).GetMethod("CreatePlayerUnit", Hidden)!;
            var tony = (Unit)create.Invoke(zone, [1u, service])!;
            var sarge = (Unit)create.Invoke(zone, [2u, service])!;
            var kira = (Unit)create.Invoke(zone, [3u, service])!;
            tony.SpawnProtectionTime = sarge.SpawnProtectionTime = null;
            tony.UpdateData(new UnitUpdate { Health = 40, CurrentGear = rivetGun });
            tony.AddEffect(new ConstEffectInfo(perk), tony.Team, tony.GetSelfSource());
            Check(tony.CurrentGear?.Key == rivetGun && tony.GetBuff(BuffType.LifeSteal) == 0.35f,
                "real Tony and the 35% life steal perk equip");
            var hitPos = sarge.Transform.Position + Vector3.UnitY;
            ulong shotId = 0;
            float Health(Unit unit) => unit.GetUpdateData().Health!.Value;
            bool Close(float actual, float expected) => Math.Abs(actual - expected) < .002f;
            List<ulong> Fire(Unit shooter, int count = 11, byte toolIndex = 0)
            {
                var ids = Enumerable.Range(0, count).Select(_ => ++shotId).ToList();
                zone.ReceivedCastRequest(shooter.PlayerId!.Value, new CastData
                {
                    ToolIndex = toolIndex, ShotPos = shooter.Transform.Position + Vector3.UnitY,
                    Shots = ids.Select(id => new ShotData { ShotId = id, TargetPos = hitPos }).ToList()
                });
                return ids;
            }
            void Hit(IEnumerable<ulong> ids) => zone.ReceivedHit((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ids.ToDictionary(id => id, _ => new HitData { InsidePoint = hitPos, TargetId = sarge.Id }));

            var fullShot = Fire(tony);
            Hit(fullShot);
            Check(Close(Health(sarge), 127), "11 Rivet Gun body pellets remove 33 HP");
            Check(Close(Health(tony), 51.55f), $"33 damage heals 11.55 HP (Tony has {Health(tony):0.00} HP)");
            Hit(fullShot);
            Check(Close(Health(tony), 51.55f), "consumed hit IDs cannot heal twice");
            clock += 50;
            var blocked = Fire(tony);
            Hit(blocked.Take(5));
            clock += 50;
            Hit(blocked.Skip(5));
            Check(Close(Health(tony), 51.55f), "a separate shot blocked by cooldown stays blocked across hit reports");

            var split = Fire(tony);
            Hit(split.Take(4));
            Check(Close(Health(tony), 55.75f), "partial hits credit only pellets that land");
            clock += 50;
            Hit(split.Skip(4));
            Check(Close(Health(tony), 63.10f), "later hit reports from the same shot finish its heal during cooldown");

            kira.UpdateData(new UnitUpdate { Health = 40, CurrentGear = new Key("gear_kira_uzi") });
            kira.AddEffect(new ConstEffectInfo(perk), kira.Team, kira.GetSelfSource());
            Hit(Fire(kira, 2));
            Check(Close(Health(kira), 42.8f), "both of Kira's projectile hits contribute to life steal");
            clock += 100;
            Hit(Fire(kira, 2, 1));
            Check(Close(Health(kira), 44.9f), "both of Kira's hitscan hits contribute to life steal");

            clock += 50;
            sarge.UpdateData(new UnitUpdate { Health = 4 });
            Hit(Fire(tony));
            Check(sarge.IsDead && Close(Health(tony), 64.50f), "lethal volley heals from actual HP removed, including the killing pellet");
            Check(Close(tony.Stats!.GetValueOrDefault(ScoreType.HealPlayerByHero), 24.50f),
                "match stats record actual pellet healing");
            done.SetResult();
        }
        catch (Exception error) { done.SetException(error); }
    });
    try { done.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(); }
    finally { zone.Stop(); }
}
Console.WriteLine($"Life steal fixture passed: {checks} checks.");

public class ZoneProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == "GetGameMode") return new Key("game_mode_custom");
        if (method?.Name == "get_GameInstanceId") return "life-steal-fixture";
        var type = method?.ReturnType;
        return type is { IsValueType: true } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }
}
