# Yeti Avalanche slow and hero perk removal

This is a catalogue-only change. No server or client code changes.

## Avalanche

Yeti's active ability keeps its marker throw and 30-second cooldown. The marker no
longer summons the snow-block shower. When it lands, every enemy player within 5
blocks of the landing point receives `effect_hero_abe_avalanche_slow` for 2 seconds:

| Buff | Value |
| --- | --- |
| run, sprint, swim speed | -30% |
| jump height | -30% |
| dash distance / dash time | -15% / +15% |

The slow is a copy of the Snow Thrower slow scaled from 40% to 30%, dash penalty
included, and it reuses the same debuff visual and slow icon. The landing plays the
existing `impact_abe_snow_thrower_splash` frost burst. Allies and the Yeti who threw
the marker are not affected.

The hit effect is `all_units_bunch` with `range: 5`. The server centres that range on
the landing point and applies the constant effect to each filtered unit with the
effect card's own duration. Speed slows from different effects stack
multiplicatively, so an enemy hit by both the Snow Thrower and Avalanche moves at
0.6 x 0.7 = 42% speed for the overlap.

The Avalanche ability description and its loading tip are rewritten in English. Their
stale translations, which described the snow blocks, are dropped.

`unit_dummy_abe_avalanche` stays in the catalogue unreferenced, so the shower can be
restored by putting the old hit effect back (it is in the backup).

## Removed hero perks

Facewash and Yuri 'n Ice are unlisted from `global_logic.perks.heroes.unit_hero_abe`
and `shop_logic`, and these cards are deleted:

- `shop_item_perk_hero_abe_facewash`, `perk_hero_abe_facewash`,
  `gear_abe_snow_thrower_facewash`, `effect_hero_abe_snow_thrower_slow_face_wash`
- `shop_item_perk_hero_abe_yuri_n_ice`, `perk_hero_abe_yuri_n_ice`,
  `ability_abe_avalanche_yuri_n_ice`, `unit_dummy_abe_avalanche_yuri_n_ice`

Chill, Dude is Yeti's only remaining hero perk. Player inventory is derived from
`global_logic` on fetch, and `PlayerDataSanitizer` strips the deleted perk keys from
saved loadouts at each player's next login. `YuriNIceIgloo` becomes dormant because
its unit card no longer exists.

## Verification

`tests/BNLReloadedServer.AvalancheSlowFixture` loads the real migrated cards and
applies Avalanche's hit effect in a real `GameZone`. It checks that enemies 3 and
4.5 blocks away get the 30% / 2 s slow, that an enemy 8 blocks away, an ally and
the caster are untouched, that the splash impact plays once, and that no shower unit
spawns. Run against the pre-migration cards, it fails on the first check.

```bash
python3 tools/rebalance_yeti_avalanche_and_perks.py --from-file catalogue.json  # writes the plan
dotnet run --project tests/BNLReloadedServer.AvalancheSlowFixture -- cards.json
```

`cards.json` is a catalogue array holding `ability_abe_avalanche`,
`effect_hero_abe_avalanche_slow` and `impact_abe_snow_thrower_splash`.

## Publication

On the production host, from the checked-out server release:

```bash
sudo python3 tools/rebalance_yeti_avalanche_and_perks.py
sudo python3 tools/rebalance_yeti_avalanche_and_perks.py --apply
```

The first command is read-only. It reads the whole catalogue and refuses to plan if
any remaining card would reference a deleted one. `--apply` creates the slow effect
before the ability references it, writes with the current CouchDB revisions as
concurrency guards, deletes with the current revision, saves every original card
under `/root/config-backups` and reads everything back. The catalogue watcher loads
the change without a server restart. Apply it while no match is running.
