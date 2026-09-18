// Ability cooldown reduction must act on a cooldown that is already running: gaining the buff
// shortens the remaining wait, losing it stretches the remainder back out, and the client is told
// the new end time each time. Elapsed progress is always kept.
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

// 2. Losing the buff stretches the remainder back to the unbuffed rate.
cooldownUpdates.Clear();
Remove(player, half);
Check(Near(Remaining(player), baseCooldown), "losing the buff restores the unbuffed remaining time");
Check(cooldownUpdates.Count == 1, "client is told about the stretched cooldown");

// 3. Stacking and partial changes scale by the ratio of multipliers, not from the base cooldown.
Apply(player, half);
Apply(player, quarter); // 0.75 reduction -> multiplier 0.25
Check(Near(Remaining(player), baseCooldown * 0.25), "stacked reductions rescale the remainder by the combined multiplier");
Remove(player, quarter); // back to 0.5
Check(Near(Remaining(player), baseCooldown * 0.5), "dropping one stack rescales relative to the surviving buff");
Remove(player, half);

// 4. Elapsed progress is preserved: a buff gained late only rescales what is left.
var late = CreateHero();
late.AbilityUsed();
late.TimeTillNextAbilityCharge = DateTimeOffset.Now.AddSeconds(4); // pretend 16 of 20 seconds elapsed
Apply(late, half);
Check(Near(Remaining(late), 2), "only the remaining 4s is halved when the buff arrives late");
Remove(late, half);
Check(Near(Remaining(late), 4), "removing it late restores the remaining 4s, not the full cooldown");

// 5. A buff already active when the ability is used sets the shortened cooldown and records the rate.
var prebuffed = CreateHero();
Apply(prebuffed, half);
cooldownUpdates.Clear();
prebuffed.AbilityUsed();
Check(Near(Remaining(prebuffed), baseCooldown / 2), "ability used under the buff starts the halved cooldown");
Remove(prebuffed, half);
Check(Near(Remaining(prebuffed), baseCooldown), "buff expiring after that stretches the remainder to the unbuffed rate");

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
Check(Near(Remaining(chain), baseCooldown), "and stretches back when the buff ends");

Console.WriteLine($"Ability cooldown fixture passed: {checks} checks.");
