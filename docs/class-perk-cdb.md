# Class perks in CouchDB

Each hero class has three class perks: public `CardPerk` documents `perk_class_<brawn|brains|skill>_<1|2|3>` with `slot_type: "class"` (wire enum 4). A hero always plays exactly one class perk of its own class. They are not shop purchases or ordinary inventory perks.

## Selection

The chosen class perk is stored as a regular key in the hero loadout's `perks` list, beside the offensive/defensive/hero perks. The lobby's native select-perk message replaces it by slot type, and the Your Heroes loadout save carries it like any other perk. The server accepts a class key in the lobby only for the player's current hero class. At spawn, `ClassPerkCatalogue.EffectivePerks` strips every class key that does not belong to the hero and adds the class default (I) when none is selected, so loadouts saved before II/III existed play class I and its `perk_mods` always apply. `Unit.ActiveClassPerk` is what gameplay checks.

## Perks and where their numbers live

Everything tunable is in CDB. Perks that are pure catalogue effects keep their numbers on effect cards; the server-side rules read `class_perk` settings. Descriptions on effect-card perks state the numbers in text, as regular perks do: edit both together.

| Perk | Behaviour | Tune in |
| --- | --- | --- |
| Brawn I `perk_class_brawn_1` Rally Point | Mobile team respawn | `class_perk.out_of_combat_seconds` (0..60), `spawn_distance` (1..3) |
| Brawn II `perk_class_brawn_2` Bulwark | `effect_class_brawn_2_aura`: friendly heroes within `outer_radius` (4) receive `effect_class_brawn_2_guard`, a `shield` buff (0.15). One effect key, so several Bulwarks do not stack; `shield` combines by maximum, so it does not stack with other shields either. | aura `outer_radius`, guard `buffs.shield` |
| Brawn III `perk_class_brawn_3` Demolisher | `effect_class_brawn_3_on_kill` gives `effect_class_brawn_3_demolish` on an enemy hero kill: `tool_world_damage` 0.5 (dig tool vs blocks) and `objective_damage` 0.5, `duration` 10 | demolish card |
| Brains I `perk_class_brains_1` Shared Recovery | Healer regains a share of ally healing | `class_perk.healing_return_percent` (0..100) |
| Brains II `perk_class_brains_2` Supply Line | Whenever the hero's healing restores an ally's health, the ally receives `class_perk.healed_ally_effect` (`effect_class_brains_2_ammo_regen`: `interval` 0.5 s of `add_ammo_percent` 0.025, `duration` 2, refreshed by each heal). Same eligibility as Shared Recovery: allies only, no self-heal, no health stations. | `class_perk.healed_ally_effect`, effect card |
| Brains III `perk_class_brains_3` Quick Builder | `effect_class_brains_3_build_speed`: `build_speed` 0.3 | effect card |
| Skill I `perk_class_skill_1` Second Wind | Out-of-combat regeneration | `class_perk.out_of_combat_seconds`, `regen_max_health_percent_per_second` |
| Skill II `perk_class_skill_2` Accomplice | A kill assist runs the assister's `on_kill` effects (Killer Shield, Killer Ammo, Rapid Reset...) as if they had made the kill, only when the assister dealt the victim the most enemy damage within the 30 s assist window, killing blow included (ties count) | no numbers |
| Skill III `perk_class_skill_3` Reinforcements | An enemy hero kill removes `respawn_reduction_percent` (70) of the remaining respawn time of every dead teammate and of the killer; the new times are published in `RespawnInfo` | `class_perk.respawn_reduction_percent` (0..100) |

All three Brawn cards carry `effect_class_brawn_guard_immunity` in `perk_mods`, so no Brawn receives Bulwark. Description templates: `{out_of_combat_seconds}`, `{healing_return_percent}`, `{regen_max_health_percent_per_second}`, `{spawn_distance}`, `{respawn_reduction_percent}`. Do not type a percent sign into numeric fields. Invalid values are rejected before replacing the server catalogue; missing cards/fields keep defaults. Players refresh the catalogue on reconnect.

## Activation

1. Deploy the server revision first (normal DLL/PDB hotfix). **Do not restart while players are in a casual match**: read `/api/public/home` and inspect `play.matches` immediately before any restart. An older binary rejects the II/III cards (unknown class perk ids fail validation).
2. Stage `tools/register_class_perks.py` with `catalogue/class-perks` and `catalogue/class-perk-effects`. Run with sudo as a dry run, then `--apply`. It creates missing effect cards before the perks that use them, creates missing perks, fills Brawn I's empty `perk_mods` with the guard immunity, never changes tuned `class_perk` values, and writes a before/after snapshot under `/root/config-backups/class-perks-*`.
3. Verify the change-watcher events and CDB readback. Clients without class perk II/III support keep playing class I (they never send a II/III key); updated clients show and select all three.

Rollback before reverting the server binary: delete only the cards this migration created (revisions in the snapshot) and restore Brawn I from `before.json`, then verify the catalogue watcher processed the changes.

## Validation

`tests/BNLReloadedServer.ClassPerksExpandedFixture` loads these JSON cards into a real `GameZone` and checks selection/spawn resolution, Supply Line ammo regeneration and refresh, Quick Builder, Bulwark radius/exclusions/non-stacking/damage, Demolisher, Accomplice and Reinforcements including a live CDB edit. Also run the BrawnRally, BrainsRecovery, SkillRecovery and ClassPerkCatalogue fixtures.
