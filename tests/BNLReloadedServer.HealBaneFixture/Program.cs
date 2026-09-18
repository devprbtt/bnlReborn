// Heal Bane: a HealBane attacker applies CatalogueHelper.HealBaneDebuff to every enemy player it
// damages; the debuff cuts the victim's healing by its card value for its card duration.
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
var hero = new CardUnit { Id = "fixture_hb_hero", Data = new UnitDataPlayer(), Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } } };
var opponents = new EffectTargeting { AffectedTeam = RelativeTeamType.Opponent, AffectedUnits = [UnitType.Player] };
var bane = new CardEffect { Id = "fixture_hb_bane", Positive = true, Effect = new ConstEffectBuff { Targeting = new EffectTargeting { AffectedTeam = RelativeTeamType.Both }, Buffs = new() { [BuffType.HealBane] = 1 } } };
var debuff = new CardEffect { Id = "effect_heal_bane_debuff", Positive = false, Duration = 10, Effect = new ConstEffectBuff { Targeting = opponents, Buffs = new() { [BuffType.HealthGain] = -0.6f } } };
catalogue.Replicate([hero, bane, debuff, new CardGlobalLogic { Id = "global_logic" }]);
Check(CatalogueHelper.HealBaneDebuff.GetCard<CardEffect>() is { } card && card.Key == debuff.Key, "the server's debuff key resolves to the catalogue card effect_heal_bane_debuff");
Unit Create(uint id, TeamType team, uint? player = null) => new(id, new UnitInit { Key = hero.Key, Team = team, PlayerId = player ?? id, OwnerId = id }, updater);
var applies = typeof(BNLReloadedServer.ServerTypes.GameZone).GetMethod("OnHitApplies", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Applies(Unit a, Unit t) => (bool)applies.Invoke(null, new object[] { a, t })!;

var attacker = Create(1, TeamType.Team1);
var victim = Create(2, TeamType.Team2);
victim.UpdateData(new UnitUpdate { Health = 50f });
Check(!attacker.IsBuff(BuffType.HealBane), "no perk: attacker has no heal bane");
attacker.AddEffect(new ConstEffectInfo(bane.Key), attacker.Team, attacker.GetSelfSource());
Check(attacker.IsBuff(BuffType.HealBane) && attacker.GetBuff(BuffType.HealBane) == 1, "perk grants the active-flag buff");
Check(Applies(attacker, victim), "living player hitting another player's unit qualifies");
// What the zone does on hit:
victim.AddEffects([new ConstEffectInfo(CatalogueHelper.HealBaneDebuff)], attacker.Team, attacker.GetSelfSource());
Check(victim.ActiveEffects.Any(e => e.Key == debuff.Key), "the debuff lands on the victim");
Check(Math.Abs(victim.HealthGainAmount(10f) - 4f) < 0.01f, "victim's healing is cut by 60%");
victim.AddHealth(10f);
Check(Math.Abs(victim.HealthPercentage * 100f - 54f) < 0.01f, "a 10 heal restores only 4 while baned");
var first = victim.ActiveEffects.First(e => e.Key == debuff.Key);
victim.AddEffects([new ConstEffectInfo(CatalogueHelper.HealBaneDebuff)], attacker.Team, attacker.GetSelfSource());
Check(victim.ActiveEffects.Count(e => e.Key == debuff.Key) == 1, "a second hit refreshes instead of stacking");
victim.PurgeEffects(false, true); // expiry stand-in
Check(!victim.ActiveEffects.Any(e => e.Key == debuff.Key) && Math.Abs(victim.HealthGainAmount(10f) - 10f) < 0.01f, "healing is back to normal once the debuff ends");
var ally = Create(3, TeamType.Team1);
ally.AddEffects([new ConstEffectInfo(CatalogueHelper.HealBaneDebuff)], attacker.Team, attacker.GetSelfSource());
Check(!ally.ActiveEffects.Any(e => e.Key == debuff.Key), "the debuff card's opponent targeting refuses allies");
var block = new Unit(4, new UnitInit { Key = hero.Key, Team = TeamType.Team2, PlayerId = null, OwnerId = 9 }, updater);
Check(!Applies(attacker, block) && !Applies(attacker, attacker), "blocks/devices and self never qualify");
Console.WriteLine($"Heal bane fixture passed: {checks} checks.");
