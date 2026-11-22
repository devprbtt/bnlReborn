> **Deployed and live-verified:** server `8a59207`, all18 CDB documents applied. The preparation record below is historical.

# Kira and Trondson F activation buffs — 2026-11-22

Prepared and tested; **not deployed**. Extend Tony's accepted-cast recipient selection to Kira and Trondson. This branch is based on source `e48236d`; the running binary remains `0fee0bc`. The separately pending Roly death-cooldown fix is not included.

| Variant | Activation range | Duration | Cooldown | Preserved bonuses |
| --- | ---: | ---: | ---: | --- |
| Kira J-POP Star | 10m | 10s | 60s | Own unlimited ammo; self/allied reload +50% |
| Idoru | 16m | 10s | 67s | Own unlimited ammo; self/allied reload +100% total |
| Thrills for Days | 10m | 6s | 60s | Own unlimited ammo; self/allied reload +50%; kills refresh original recipients |
| Trondson Heigh-Ho | 10m | 12s | 50s | Existing digging, digging-health, allied blast and drill bonuses |
| Singin' in the Mine | 16m | 12s | 57s | Same bonuses plus existing +20% mining yield |

## Behavior

Select friendly recipients at server acceptance of F. Recipients retain their timed buffs after leaving range; entering later grants nothing. A new activation selects a new set. Delayed client hit reports cannot select or reward extra recipients. Keep native targeting, immunity, death cleanup, scoring rules and player-specific self bonuses. Trondson's negative-effect purge and caster self wrappers remain unchanged; nearby allied drills get their existing damage bonus as a timed activation snapshot. Later drill arrivals do not gain it.

The owner explicitly chose **Thrills for Days kills refresh the original recipients too**. Capture actual successful wrapper applications during the accepted cast using server-only impact metadata shared through clones. On a qualifying native kill, refresh those original living, active recipients even outside the original range. Check original unit ids so death/respawn cannot transfer the old grant into a new life. Do not restore purged/expired buffs, recruit new arrivals, or resurrect an expired cast. Prune cast state on expiry/caster death, disconnect, inactivity, team change or respawn. Kira's own ammo/on-kill chain retains its native refresh behavior.

## Implementation

`GameZoneActivationBuffs` replaces the Tony-only inline cast route with an exact-key/marker whitelist for seven abilities (Tony's two and these five). Require native self-cast application and the new marker before using immediate authoritative selection. Old CDB cards retain their previous route, supporting server-first rollout. Tony still uses its existing receipt deduplication metadata and scoring; no new receipt points are added to Kira or Trondson.

Native timed self wrappers carry the buffs; no new wire variant is introduced. Kira's shared legacy reload card has a ten-second duration, so clone its magnitude into a new untimed value card and let the 10s/6s wrapper own expiry. This prevents the child from outliving Thrills' six-second duration. Keep the original shared card and all original aura definitions intact for legacy references. Idoru retains its additional reload effect. Trondson reuses the existing player/drill child buffs, preserving source attribution and bonus strength. GUI icons reuse the existing cards/artwork.

The reviewed full before/after patch changes **18 cards: 11 existing and 7 new**. Five abilities, the Thrills kill callback, two hero descriptions and three perk descriptions change. All **2,048 other existing documents** remain identical, including current Nigel40m tuning and original Time Assault trap behavior. No catalogue writes occurred during preparation.

## Validation

- New actual-world fixture: **110 checks** across all five variants. Covers authoritative cast position, immediate and fixed recipients, native strengths/durations, dead/enemy filtering, leaving/entering range, rejected casts and delayed hits, repeat activations, caster death, expiry and child cleanup, drill bonuses, existing score attribution, original-recipient kill refresh, respawn exclusion and expired callback rejection.
- Existing Tony fixture34, action scoring308, cooldown21, native self-effect14, ignore-caster12 and Second Wind68 checks pass: **567 assertions total**.
- Guarded transaction writer: six scenarios pass for the new18-card plan and six for the original19-card plan, including lost-response rollback and concurrent-author protection.
- Release build with warnings as errors: zero warnings/errors. Deployment Python scripts compile.
- Actual host preflight is read-only: **18 pending, 7 new**; every existing source revision/content matches and every new id is absent.

No live multiplayer client acceptance, restart, CDB write or client/feed release is claimed.

## Reproduction and rollout

```powershell
python tools/prepare_kira_trondson_cast_buffs.py <raw-public-catalogue.json> <output>
dotnet run --project tests/BNLReloadedServer.ActivationBuffFixture -c Release -- <output>/candidate-catalogue.json
dotnet run --project tests/BNLReloadedServer.TonyCastBuffFixture -c Release -- <output>/candidate-catalogue.json
python tests/test_gameplay_rollout.py catalogue/kira-trondson-cast-buffs-20261122/kira-trondson-cast-buffs.patch.json
dotnet build BNLReloadedServer -c Release -warnaserror
```

Commit/push before rollout; deployment follows separate authorization. The existing deployment helper accepts manifest `catalogue_card_count: 18` while preserving19 by default. Stage the new binary and reviewed patch as `combined.patch.json`, with actual running binary revision as `expected_base`; require unchanged dependencies and uploaded hashes. In one maintenance window, stop the service, take private document/SQLite backups, apply the guarded18-card transaction (new effects first), switch to the new binary and restart with health gates. The writer also supports explicit `--expected-card-count 18 --required-revision <new-server-commit>` for an authorized manual rollout. Do not apply the new CDB to the old binary.

The runtime verifier carries forward the previously validated null/default-aware readback helper from operations commit `ea968d5`. Verify all18 raw/runtime changes, all30 original trap cards, service listeners/auth/status and journals. Reconnect clients for the refreshed native CDB; existing clients need no binary update. On failure, restore ability/on-kill references before deleting new effects and reverting the binary; preserve concurrent authors' changes. Retain the previous release for rollback.

Evidence: client `reports/kira-trondson-cast-buffs/`; raw inputs, untouched source baselines and full candidate in `extracted/work/kira-trondson-cast-buffs-20261122/`. Source roots were not changed.

Player-facing notes:

- Kira and Trondson F now grant timed buffs to players in range at activation. Leaving range keeps the buff; entering later does not grant it.
- Thrills for Days kills refresh Kira and the original living recipients, including teammates who have moved away.


## Deployment result

Exact live revision `8a592077ffc807d9cbc704e08fed24c073b48ee7`; main/production/feature refs match. Release `/opt/bnlreloaded/releases/8a592077ffc8`; DLL SHA256 `ce79eb227586c20de0fb6ade9b1cae83d4c7c372552fec078c96cdddf8efb5f9`. Rollback snapshot `/root/config-backups/gameplay-rollout-20261122T152505Z` contains catalogue preimages, a consistent SQLite backup and previous-release pointer. All18 CDB/runtime documents and30 original trap cards verified; service/auth/status/listeners and error-journal checks pass. Previously deployed balance values remain intact. 760 assertions passed across preparation/deployment fixtures, plus12 transaction scenarios. Reconnect existing clients for new native catalogue keys. No client release or live two-player acceptance is claimed. The separate Roly death-cooldown fix remains pending. Evidence: client reports/kira-trondson-cast-buffs/.
