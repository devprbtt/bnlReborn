# Inflicted healing reductions do not stack

2026-11-25. Requested balance change: Ninja's Bloody Bleeding healing cut (20%) must not stack with Heal Bane (60%).

## Before

`health_gain` buffs from all active effects were summed (`BuffHelper.CombineBuffs`, floor -1). A Bloody Bleeding
Ninja with Heal Bane, or a Heal Bane teammate, left the victim at 1 - 0.6 - 0.2 = 20% healing.

## Rule

In `Unit.ExtractBuffs`, a negative `health_gain` from an effect that is not one of the unit's own starting (perk)
effects is an inflicted healing reduction. Only the strongest active one applies. The unit's own perk penalties
(Just a Scratch, Killer Shield, Last Stand Momentum) and positive healing bonuses still add as before; the
strongest inflicted reduction is then added to that sum, floored at -1.

The values are unchanged and still live on the cards:

| Card | `health_gain` | Duration |
| --- | --- | --- |
| `effect_heal_bane_debuff` | -0.6 | 3 s |
| `effect_status_bleed_bloody_bleeding_heal` | -0.2 | 5 s |

Live catalogue read on 2026-11-25 (2,126 cards, release 1285834b54b1): these are the only two inflicted healing
reductions, so this rule changes only their interaction today. No CDB write and no client change are needed;
clients receive the combined buff value as before.

| Victim state | Healing received |
| --- | --- |
| Bloody Bleeding only | 80% |
| Heal Bane only | 40% |
| Both (either order) | 40% (was 20%) |
| Heal Bane expires, bleed remains | 80% |
| Own -25% perk + Bloody Bleeding | 55% (unchanged) |
| Own -25% perk + both | 15% (was 0%) |

## Validation

- `tests/BNLReloadedServer.HealBaneFixture`: 44 checks, including 8 new no-stacking checks. Run against the
  unchanged `Unit.cs` it fails at "together they cut healing by 60%, not 80%".
- Live-catalogue fixtures: HealingPerkSources 128, GenieStationHealing 29, HealAttribution 22, GenieOrbHealing 21,
  ActivationBuff 115.
- `BNLReloadedServer` Release `-warnaserror --no-incremental`: 0 warnings, 0 errors.

Requires a server deployment (restart); not deployed.
