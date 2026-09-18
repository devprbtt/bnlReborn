// Life steal: a living player with the LifeSteal buff is healed for buff x damage whenever it damages
// another player's unit; never for blocks/devices, itself, or while dead, and never above max health.
using System.Linq.Expressions;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
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
attacker.AddHealth(attacker.LifeStealAmount(40f));
Check(Math.Abs(attacker.HealthPercentage * 100f - 60f) < 0.01f, "the heal lands on the attacker");
attacker.AddHealth(attacker.LifeStealAmount(1000f));
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

// Killing blow: the callback reports both the capped and the full hit damage, and life steal is paid on the full hit.
var damaged = new List<(float damage, float hitDamage)>();
var probeUpdater = updater with { OnUnitDamaged = (t, d, hit, _) => damaged.Add((d, hit)) };
var prey = new Unit(20, new UnitInit { Key = hero.Key, Team = TeamType.Team2, PlayerId = 20, OwnerId = 20 }, probeUpdater);
prey.UpdateData(new UnitUpdate { Health = 8f });
prey.TakeDamage(new BNLReloadedServer.ServerTypes.DamageData(0f, 0f, 40f, 0f, 0f, 0f, 0f, 0f, false, false, false, false), attacker.CreateImpactData(), false, attacker, attacker.Team);
Check(prey.IsDead && damaged.Count == 1 && Math.Abs(damaged[0].damage - 8f) < 0.01f && Math.Abs(damaged[0].hitDamage - 40f) < 0.01f,
    "a killing 40-damage hit on 8 HP reports damage 8 and hit damage 40");
var finisher = Create(21, TeamType.Team1); finisher.UpdateData(new UnitUpdate { Health = 50f });
finisher.AddEffect(new ConstEffectInfo(steal.Key), finisher.Team, finisher.GetSelfSource());
finisher.AddHealth(finisher.LifeStealAmount(damaged[0].hitDamage));
Check(Math.Abs(finisher.HealthPercentage * 100f - 70f) < 0.01f, "life steal on the killing blow pays on the full 40 (50% -> +20), not the 8 that was left");
Console.WriteLine($"Life steal fixture passed: {checks} checks.");
