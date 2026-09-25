# On-hit effects (Heal Bane)

`on_hit` is a perk trigger like `on_kill`, `on_damage_taken` or `on_reload`. It fires when the owner's own weapon
directly hits an enemy player, and runs its `effect` on the player who was hit, with the owner as the source.

It fires on:

- damage from the owner's gear, or from a projectile that gear launched.

It never fires on:

- turrets, devices, trap blocks or abilities;
- bleed or burn ticks (periodic damage);
- allies, the owner, blocks or devices;
- dead targets.

## Heal Bane

The whole perk is catalogue data; no server code names any of these ids.

```
perk_off_heal_bane                      perk_mods: effect_perk_heal_bane_pos, effect_perk_heal_bane_neg
effect_perk_heal_bane_pos               { "type": "on_hit", "effect": { "type": "bunch",
                                           "targeting": { "affected_units": ["player"], "affected_team": "opponent" },
                                           "constant": ["effect_heal_bane_debuff"] } }
effect_heal_bane_debuff                 duration 5, buffs { "health_gain": -0.6 }   <- strength and duration
effect_perk_heal_bane_neg               buffs { "weapon_reload": -0.15 }            <- the perk's drawback
```

- **Change strength or duration:** edit `effect_heal_bane_debuff` (`health_gain`, `duration`).
- **Change the drawback:** edit `effect_perk_heal_bane_neg`.
- **Make a variant** (another perk with a different strength): add a new debuff card and a new `on_hit` effect whose
  `constant` names it, then point the new perk's `perk_mods` at it.
- **Other on-hit perks** (slow on hit, mark on hit, …): the same shape, naming a different effect. An `instant` list
  runs one-off effects instead of timed ones.

Each hit refreshes the debuff rather than stacking it. The catalogue validator refuses an `on_hit` whose effect is
missing or names an effect card that does not exist, so a typo stops the catalogue load instead of silently
disabling the perk.

## Clients

The client has no `on_hit` type and throws on effect types it does not know. It never runs effect logic, so the
server sends every `on_hit` effect to clients as an empty `buff` (`ConstEffect.ClientView`). No client release is
needed. The debuff the victim receives is an ordinary effect card, so its icon shows as before.

## History

Until 2026-09-25 the perk effect was the flag buff `{"heal_bane": 1}`, and the server applied a debuff whose key was
hardcoded in C#. `BuffType.HealBane` remains only so that a catalogue still carrying `heal_bane` parses; it does
nothing. The card was migrated with `tools/migrate_heal_bane_on_hit.py`, which must run after a server that
understands `on_hit` is live, because an older server cannot parse it.

Validation: `tests/BNLReloadedServer.HealBaneFixture` (16 checks) drives the real GameZone damage handler.
