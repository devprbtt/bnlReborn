# Six-hero balance pass — 2026-11-22

Prepared; **not applied to the live CDB or deployed**. Uses existing server
catalogue fields supported by production base `54a8a3b287de8beb6b00ed566b079067261dca72`.
No new server binary is required for these tuning values. The companion client
shared colour stop is prepared at 30%; it reaches players in a later client build.

| Request | Current | Prepared |
| --- | --- | --- |
| Roly Goo affected score | 10 per credited event | 8, a 20% reduction |
| Roly Rolling Wild F perk | 40% shielding; 52s cooldown | 30% shielding; normal 45s cooldown |
| Doc Globe Gun allied healing, both fire modes | 10 HP; Beautiful Bubbles 5 HP | 8 HP; Beautiful Bubbles 4 HP, a 20% reduction |
| Trondson Sonic Staff primary falloff | Full damage through 15m; 70% at 30m | Full damage through 12m; 50% at 30m |
| Sweet Science Kinetic Shield | 20% reduction, green | 30% reduction, existing teal tier |
| Vander secondary beam allied healing | 2 HP per pulse | 2.25 HP per pulse, +12.5% |
| Nigel Nellie rifle falloff | Full damage through 80m; 70% at 100m | Full damage through 70m; 55% at 100m |

The user requested modest Goo/healing adjustments, increased falloff for
Trondson/Nigel, and the next shield colour tier. The owner revised the shared teal tier to 30%, replacing the previously
prepared 37.5% tier in both gameplay cards and the shared client colour stop. Shield activation and cap
are unchanged. Damage is still reduced by the strongest shield, capped at 50%.

Trondson's Echo Location and Nigel's Spot Shot variants get the same primary
falloff. Close-range damage, critical multipliers, weapon range, ammo and fire
rate are unchanged. Trondson's base staff reaches 27m and Echo Location reaches
20m, so 30m is the curve endpoint, not a newly reachable firing distance.
Vander's Tesla repair remains 2 HP per pulse. Doc self-healing, enemy damage,
Miracle Cure and other heal sources are unchanged; friendly player-only
targeting and IgnoreCaster are preserved. Nigel's Revolver is unchanged.

The Goo change uses `global_logic.action_scores.roly_goo`. Other scoring values
keep their existing default/configured values; the old snapshot omitted
action_scores altogether. This reduces points per credited damage/slow event,
not Goo damage or its application frequency. Objective bonuses retain their
existing calculation. Scoring rules are loaded per zone, so new matches are
needed to observe the updated award.

Thirteen documents change at 32 explicit field paths, including matching English
weapon-stat text and damage-range reticle previews. Other translations, card
metadata and all unrelated fields remain intact. Time Assault's isolated
original trap cards and authored course data are unchanged.

## Evidence and validation

Fresh raw live catalogue: 2,057 public documents, SHA256
`e605f641b1cf3a8f03799dc0db298238c60e3eb6bfa5e5fc4d69c4366e7afd83`.
The copied input/candidate remain under the client repo's ignored
`extracted/work/shield-tier-30-20261122/`. Only the field patch is checked in here.

1,207 checks pass against the production server's actual ConvertToDamageData,
healing/unit and shield math: monotonic range curves, no close-range damage or
critical changes, both gear variants, actual ally health gains, exclusion of
enemy healing, unchanged other scoring defaults and original TA trap radius.
The guard/apply tool passes six fake-Couch scenarios: read-only preview,
idempotence, revision drift, successful apply, rollback after a lost committed
PUT response, and protection of concurrent authors' edits. Release build has
zero warnings/errors. An actual read-only production preflight finds exactly
thirteen pending documents and no already-applied documents. No live PUT occurred.

Examples from the actual damage conversion without hero buffs or shields:

| Weapon/distance | Before body damage | Prepared body damage |
| --- | ---: | ---: |
| Trondson base staff, 20m | 7.2 | 6.2222 |
| Trondson base staff, 27m | 6.08 | 4.6667 |
| Nigel rifle, 80m | 40 | 34 |
| Nigel rifle, 90m | 34 | 28 |
| Nigel rifle, 100m | 28 | 22 |

These are engine calculations, not live gameplay balance acceptance. Live
playtesting is still needed to assess whether these magnitudes feel right.

## Reproduction and rollout

```powershell
python tools/prepare_six_hero_balance.py <raw-live-catalogue.json> <output-directory>
dotnet run --project tests/BNLReloadedServer.SixHeroBalanceFixture -c Release -- <before.json> <candidate-catalogue.json> <damage-curves.csv>
python tests/test_six_hero_balance_apply.py <raw-live-catalogue.json>
dotnet build BNLReloadedServer -c Release -warnaserror
```

After separate rollout authorization, run on the game host:

```sh
sudo python3 apply_six_hero_balance.py six-hero-balance.patch.json
sudo python3 apply_six_hero_balance.py six-hero-balance.patch.json --apply
```

The first command is read-only. Both commands check every target revision and
field baseline. The apply command backs up privately, preserves other fields,
uses Couch revision guards, verifies readback and attempts rollback on failure.
Concurrent later changes are left for manual review rather than overwritten.
Refresh the reviewed patch if preflight detects catalogue drift. Verify all
changed values through the running server after its Couch watcher refreshes,
then reconnect clients for matching cached stats and start new matches for
Goo scoring. No production writes, service restart, client/feed/site release or
changes to the separately pending Time Assault movement branch occurred here.

Player-facing note: Reduced Roly Goo score and Doc's Globe Gun ally healing;
increased ranged damage falloff for Trondson's staff and Nigel's rifle; raised
Sweet Science's Kinetic Shield to the teal 30% tier; slightly increased Vander
Graaf's beam healing. Perk variants follow the same tuning.


## Rolling Wild follow-up

Replace the moving-F perk's +7-second cooldown downside with a shield downside: 30% damage reduction instead of 40%, matching the prepared Sweet Science teal tier. Restore the normal 45-second cooldown. Keep 60% movement speed, jump/swim/sprint tuning, knockback/pull immunity, 10-second duration and normal F's 40% shielding. Update both English perk descriptions. The three added documents/four fields extend the earlier nine-document patch; all original nine patch rows are unchanged. This 12-document plan supersedes the earlier nine-document deployment plan; do not apply both independently against stale revisions.

Validation now includes actual native direct/splash mitigation at 70 damage per 100, equal shield strength, preserved movement and ordinary Roly F, restored cooldown and correct perk text. The combined fixture passes 1,207 checks, writer passes all six fake-Couch scenarios, and read-only production preflight reports 12 pending/0 applied. Release build has zero warnings/errors. No live writes, deployment or client publication occurred. Prepared on `balance/roly-rolling-wild-20261122`, based on prior balance commit `1977e3b`.

Additional player-facing note: Rolling Wild now trades reduced shielding (30%) for movement during F, replacing its longer cooldown.


## Roly cannon RMB follow-up

Reduce secondary cannon damage from 36 to 32 for the simultaneous two-shell burst: player splash damage per shell changes 18 to 16. Update the English per-shell stat from 18 to 16. Primary fire, terrain/objective damage, explosion radius, ammunition, burst timing and projectile tuning remain unchanged. This adds one card/two fields; all prior twelve rows are identical. The current combined plan is thirteen documents/32 field changes and supersedes earlier nine/twelve-document drafts. The guarded writer expects this current plan.

Validation: 1,207 actual-engine checks, including native two-shell total 32 and primary/terrain damage preservation; six fake-Couch writer scenarios; live read-only preflight 13 pending/0 applied. No live PUT or deployment. Player note: Roly cannon secondary-fire damage reduced from 36 to 32.


## Shared shield tier follow-up

The owner changed the shared teal tier from 37.5% to 30% for all users. Update both prepared Sweet Science and Rolling Wild effects and the two Rolling Wild descriptions. The current public catalogue has no other 37.5% shield sources; the candidate audit covers all nine native shield-buff cards, preserves other strengths and leaves no 37.5% effects. The combined deployment plan remains thirteen documents/32 fields; ten unrelated rows, including the RMB 32-damage change, are identical to the preceding draft.

The companion client's shared ShieldStrength colour stop is 30% in both canonical and migration sources, covering badges, shield chips and first-person edge colours. This visual change needs a later client release; existing clients still read actual 30% gameplay/percentages from the CDB but retain the older colour interpolation. No client/feed release occurred here.

Validation: 1,207 actual-engine checks, six fake-Couch writer scenarios, native Unity compilation plus nine shared-tier/percentage/badge tests, and live read-only preflight 13 pending/0 applied. Evidence: client reports/shield-tier-30/. No deployment or live CDB writes.

Current player-facing note: The shared teal shield tier is now 30% instead of the previously planned 37.5%, including Sweet Science and Rolling Wild.
