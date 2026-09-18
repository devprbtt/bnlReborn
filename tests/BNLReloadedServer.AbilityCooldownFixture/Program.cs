// Ability cooldown reduction must act on a cooldown that is already running: gaining the buff cuts
// the remaining wait immediately and the client is told the new end time; losing it leaves the
// remainder alone, so a brief buff is a one-shot cut and a lasting one a faster rate.
using System.Linq.Expressions;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
bool Near(double actual, double expected, double tolerance = 0.15) => Math.Abs(actual - expected) <= tolerance;

var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var callbacks = constructor.GetParameters().Select(p =>
{
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray();
var cooldownUpdates = new List<ulong>();
var updater = (UnitUpdater)constructor.Invoke(callbacks);
updater = updater with
{
    OnUnitUpdate = (_, update, _) => { if (update.AbilityChargeCooldownEnd is { } end) cooldownUpdates.Add(end); }
};

var catalogue = (ServerCatalogue)Databases.Catalogue;
const float baseCooldown = 20f;
var hero = new CardUnit { Id = "fixture_cdr_hero", Data = new UnitDataPlayer() };
var ability = new CardAbility { Id = "fixture_cdr_ability", Icon = "", KillscoreIcon = "", Prefab = "", Behavior = new AbilityBehaviorCast { Application = new AbilityApplicationSelf() }, Charges = new AbilityCharges { MaxCharges = 2, ChargeCooldown = baseCooldown } };
var everyone = new EffectTargeting { AffectedTeam = RelativeTeamType.Both };
var half = new CardEffect { Id = "fixture_cdr_half", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.AbilityCooldownReduction] = 0.5f } } };
var quarter = new CardEffect { Id = "fixture_cdr_quarter", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.AbilityCooldownReduction] = 0.25f } } };
var instant = new CardEffect { Id = "fixture_cdr_instant", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.AbilityCooldownReduction] = 10000f } } };
var unrelated = new CardEffect { Id = "fixture_cdr_unrelated", Positive = true, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.BuildSpeed] = 1f } } };
catalogue.Replicate([hero, ability, half, quarter, instant, unrelated]);

Unit CreateHero()
{
    var unit = new Unit(1, new UnitInit { Key = hero.Key, Team = TeamType.Team1, PlayerId = 1, OwnerId = 1 }, updater);
    unit.AbilityKey = ability.Key;
    unit.AbilityCharges = ability.Charges!.MaxCharges;
    return unit;
}
double Remaining(Unit unit) => (unit.TimeTillNextAbilityCharge!.Value - DateTimeOffset.Now).TotalSeconds;
void Apply(Unit unit, CardEffect effect) => unit.AddEffect(new ConstEffectInfo(effect.Key), unit.Team, unit.GetSelfSource());
void Remove(Unit unit, CardEffect effect) => unit.RemoveEffect(new ConstEffectInfo(effect.Key), unit.Team, unit.GetSelfSource());

// 1. Buff gained mid-cooldown halves the remaining wait and notifies the client.
var player = CreateHero();
player.AbilityUsed();
Check(player.AbilityCharges == 1 && Near(Remaining(player), baseCooldown), "ability use starts the unbuffed cooldown");
cooldownUpdates.Clear();
Apply(player, half);
Check(Near(Remaining(player), baseCooldown / 2), "gaining 50% reduction halves the remaining cooldown");
Check(cooldownUpdates.Count == 1 && Near(DateTimeOffset.FromUnixTimeMilliseconds((long)cooldownUpdates[0]).Subtract(DateTimeOffset.Now).TotalSeconds, baseCooldown / 2),
    "client receives the rescaled cooldown end");

// 2. Losing the buff keeps the cut; nothing is stretched back and the client is not bothered.
cooldownUpdates.Clear();
Remove(player, half);
Check(Near(Remaining(player), baseCooldown / 2), "losing the buff keeps the shortened remaining time");
Check(cooldownUpdates.Count == 0, "no cooldown update is sent when reduction is lost");

// 3. Stacking scales by the ratio of multipliers, relative to the last multiplier applied.
Apply(player, half);   // multiplier back to 0.5 from 1: cuts again (10 -> 5)
Check(Near(Remaining(player), baseCooldown / 4), "re-gaining reduction cuts the remainder again");
Apply(player, quarter); // 0.75 reduction -> multiplier 0.25, ratio 0.5
Check(Near(Remaining(player), baseCooldown / 8), "a stronger stack cuts by the ratio of multipliers");
Remove(player, quarter); // back to 0.5: no change
Check(Near(Remaining(player), baseCooldown / 8), "dropping a stack changes nothing");
Remove(player, half);

// 4. Elapsed progress is preserved: a buff gained late only rescales what is left.
var late = CreateHero();
late.AbilityUsed();
late.TimeTillNextAbilityCharge = DateTimeOffset.Now.AddSeconds(4); // pretend 16 of 20 seconds elapsed
Apply(late, half);
Check(Near(Remaining(late), 2), "only the remaining 4s is halved when the buff arrives late");
Remove(late, half);
Check(Near(Remaining(late), 2), "removing it late keeps the 2s that were left");

// 5. A buff already active when the ability is used sets the shortened cooldown and records the rate.
var prebuffed = CreateHero();
Apply(prebuffed, half);
cooldownUpdates.Clear();
prebuffed.AbilityUsed();
Check(Near(Remaining(prebuffed), baseCooldown / 2), "ability used under the buff starts the halved cooldown");
Remove(prebuffed, half);
Check(Near(Remaining(prebuffed), baseCooldown / 2), "buff expiring after that leaves the halved cooldown alone");

// 6. A total reduction ends the running cooldown at once.
var cheat = CreateHero();
cheat.AbilityUsed();
Apply(cheat, instant);
Check(Remaining(cheat) <= 0 && cheat.IsNewAbilityChargeReady, "a full reduction makes the next charge ready immediately");

// 7. No cooldown running, or a buff that does not touch cooldowns, sends nothing.
var idle = CreateHero();
cooldownUpdates.Clear();
Apply(idle, half);
Remove(idle, half);
Check(idle.TimeTillNextAbilityCharge is null && cooldownUpdates.Count == 0, "buff changes without a running cooldown send no cooldown update");
var busy = CreateHero();
busy.AbilityUsed();
cooldownUpdates.Clear();
Apply(busy, unrelated);
Check(Near(Remaining(busy), baseCooldown) && cooldownUpdates.Count == 0, "an unrelated buff leaves the cooldown and the client alone");

// 8. A second use during a running cooldown keeps the current end; the next charge reschedules under the live buff.
var chain = CreateHero();
chain.AbilityUsed();
Apply(chain, half);
var before = chain.TimeTillNextAbilityCharge;
chain.AbilityUsed();
Check(chain.AbilityCharges == 0 && chain.TimeTillNextAbilityCharge == before, "second use keeps the already-rescaled cooldown end");
chain.AbilityChargeGained();
Check(chain.AbilityCharges == 1 && Near(Remaining(chain), baseCooldown / 2), "the following charge cooldown starts at the buffed rate");
Remove(chain, half);
Check(Near(Remaining(chain), baseCooldown / 2), "and is untouched when the buff ends");

// 9. Kill perk: a brief 80% buff is a one-shot cut of the remaining cooldown, once per kill.
var perk = new CardEffect { Id = "fixture_cdr_kill", Positive = true, Duration = 0.5f, Effect = new ConstEffectBuff { Targeting = everyone, Buffs = new() { [BuffType.AbilityCooldownReduction] = 0.8f } } };
catalogue.Replicate(catalogue.All.Append(perk).ToList());
var killer = CreateHero();
killer.AbilityUsed();
Apply(killer, perk);
Check(Near(Remaining(killer), baseCooldown * 0.2), "a kill cuts 80% of the remaining cooldown at once");
killer.PurgeEffects(true, false); // timed effects only leave through expiry; purge stands in for the tick
Check(killer.ActiveEffects.Count == 0 && Near(Remaining(killer), baseCooldown * 0.2), "the cut survives the buff expiring");
Apply(killer, perk);
Check(Near(Remaining(killer), baseCooldown * 0.04, 0.05), "a second kill cuts 80% of what is left again");
killer.PurgeEffects(true, false);

Console.WriteLine($"Ability cooldown fixture passed: {checks} checks.");
