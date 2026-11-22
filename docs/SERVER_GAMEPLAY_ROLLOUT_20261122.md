# Combined server gameplay rollout — 2026-11-22

The owner explicitly authorized deployment of all server changes discussed in this session. Integrate the Time Assault restart guard (`618d11b`), Tony/Second Wind (`f1724e7`) and final balance catalogue (`e90473b`) over live `54a8a3b`. The source branches remain intact. No client/feed release is included; smooth local prestart freezing and the shared 30% teal colour stop remain prepared for a later signed client.

## Player-facing notes

- Time Assault: holding movement while restarting cannot alter the start before GO. Preserve the already-live original course traps (Acid Trap radius 2.0m).
- Roly: Goo points 10 to 8; cannon secondary total damage 36 to 32 (18 to 16 per shell).
- Rolling Wild: shield 40% to 30%, cooldown 52s to the normal 45s; movement benefit retained.
- Doc: allied weapon healing 10 to 8 HP; Beautiful Bubbles 5 to 4 HP.
- Trondson, including Echo Location: primary falloff begins 15m to 12m, retained damage at 30m 70% to 50%.
- Nigel, including Spot Shot: rifle falloff begins 80m to 70m, retained damage at 100m 70% to 55%.
- Sweet Science Kinetic Shield: 20% to 30%. All prepared teal-tier shields use 30%.
- Vander: beam healing 2 to 2.25 HP per pulse.
- Tony: F grants 20s native buffs to recipients at activation rather than carrying an aura; 5 base receipt points once per accepted cast per player, including Tony. Normal/Wider range 5m/11m preserved; old turret bonus retained as a timed activation buff.
- Second Wind: radar/spotting reveal and living enemy players within 8m continuously reset the existing 5s quiet period; rate stays 10% maximum health per eligible second.

## Rollout artifacts and checks

`catalogue/gameplay-rollout-20261122/combined.patch.json` combines the 13 balance and six Tony/perk documents with source revisions and complete before/after content. Seventeen existing documents retain metadata; two new native timed wrappers are installed first. Fresh public snapshot SHA256 `e605f641b1cf3a8f03799dc0db298238c60e3eb6bfa5e5fc4d69c4366e7afd83`. Inputs, combined candidate and six hashed course-map copies: client `extracted/work/server-gameplay-rollout-20261122/` and `source-manifest.json`.

Combined validation: Time Assault movement 6,900; fixed spawn 504; original traps 37,539 (784 proximity units and 9,789 ground cells); Tony 34; balance 1,209; Second Wind 68; action scoring 308; class-perk catalogue 31; ability cooldown 21; team features 170; authentication 4; accounting 19. Total 46,807 assertions. Guarded new/existing-document writer passes six isolated scenarios, including lost-response rollback and concurrent-edit preservation. Warnings-as-errors Release build and linux-x64 publish pass.

`tools/deploy_gameplay_rollout.py` defaults to read-only preflight on the game host. Verify uploaded hashes, current release, unchanged dependency libraries and all 19 catalogue preconditions. Explicit --apply stages a release cloned from current, takes a private CDB preimage and SQLite backup after clean stop, installs the catalogue and binary, atomically swaps the release pointer and starts the service. Startup gates include listeners, signed auth, public status and empty error journal. Failure restores CDB before the prior binary; concurrent edits are never overwritten. Keep the previous release and all backup files.

`tools/verify_gameplay_rollout.py` reads back all 19 raw/runtime documents, all 30 isolated trap cards, binary hashes, startup/listeners, public auth and anonymous CouchDB denial. It does not write live gameplay state. Commit and push the integrated server revision before deployment; update main/production only after live verification. Deployment results and exact hashes/backup path go into client reports/server-gameplay-rollout/.


## Deployment result

Deployed and live-verified revision `0fee0bc8001fa02a59ac4e1b950bddc4a1fd5ac5`; main, production and release refs match. Release `/opt/bnlreloaded/releases/0fee0bc8001f`; DLL SHA256 `3f9f032a68566bab577c2d07e3b1e3ddb50208ae08318d79e8ca9467a76f92ef`. Applied 19/19 CDB documents. Raw CDB content is exact; runtime verification checks changed fields because the runtime expands unrelated defaults/perk lists and omits optional nulls. All 30 historical trap cards match. Auth/listeners/status and service journal health pass. Rollback snapshot `/root/config-backups/gameplay-rollout-20261122T142156Z` includes private document preimages and consistent SQLite backup; prior release remains available. Client reports/server-gameplay-rollout/ contains validation, hashes, sanitized logs and public card readback. No client release or live multiplayer acceptance is claimed.
