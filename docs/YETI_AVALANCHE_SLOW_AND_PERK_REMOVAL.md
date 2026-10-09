# Yeti Avalanche slow and hero perk removal

The October 8 change below is historical. The October 9 follow-up requires both server and client code; see the final section.

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


## 2026-10-09: persistent Avalanche and Permafrost (prepared, not deployed)

Both Avalanche variants now create a replicated Common unit for four seconds.
Normal radius is five blocks; Permafrost is four (20% smaller). A dedicated,
source-counted permanent effect maintains the existing 30% slow while enemies are
inside and removes it on exit or area expiry. Overlapping areas do not multiply
the slow, and one area's exit cannot remove another area's effect. Snow Thrower's
separate slow remains independent. Cooldown stays 30 seconds.

Permafrost tracks continuous exposure per enemy unit and per cast on the zone
thread. Leaving, dying or becoming inactive clears the dwell timer. Two seconds
inside attempts the existing Root buff for 0.5 seconds, once per enemy per cast;
normal control immunity still applies. Weapons remain available. Root already
applied expires independently even if the area ends during its half-second.
The inclusive spherical boundary uses the hero midpoint, not capsule overlap;
there is no line-of-sight restriction, matching the previous area slow.

The new hero perk is `perk_hero_abe_permafrost`, using the retired Avalanche
(Yuri 'n Ice) art `shop_perk_hero_abe_yuri_n_ice`. Its shop card inherits the
existing Yeti hero-perk price (11,000) and level (III). The old retired perks stay
retired. `PerkModAbility` selects the dedicated ability card normally.

Client integration in blocknload-unity-upgrade attaches `AvalancheAreaVisual` to
the replicated area unit, with a translucent frost boundary and swirling snow.
The sphere and server test share radii 5/4. Units created during reconnect also
get the VFX; server UnitDrop hides it immediately, with no new local four-second
clock. Cards intentionally have no new prefab path, avoiding unknown-prefab
exceptions on older clients, but those clients cannot show the area. Release the
VFX client and require that version before activating these cards.

`tools/add_yeti_permafrost.py` plans read-only by default, accepts `--from-file`
and optional `--output` offline, and asserts idempotence. On-host `--apply` backs
up all eleven affected documents and uses revision guards. Deploy this server
code and the matching client before applying the catalogue migration. Do not
rerun the historical October 8 migration after this follow-up.

Validation: `dotnet run --project tests/BNLReloadedServer.PermafrostFixture -c Release`
uses included real migrated cards and passes 31 assertions: ability selection,
geometry/teams, dwell reset, late entry, one root, 0.5-second duration, weapon
availability, overlaps, expiry, and native replicated UnitDrop. Client batch
validation and a synthetic preview are in the client repository's
`reports/permafrost`. No live-match playtest or production deployment yet.
