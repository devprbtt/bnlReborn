

## 2026-10-19 - Replay seek flow control

- Forward/skip-build continue in place; rewind reconstructs the recorded world. Capability 10 gates ordered zone 113 barriers, acknowledged through replay command 5. Batches stop at 128 records/32 KiB, await actual client consumption, and time out after 30 seconds. Final target/resume waits for the last ACK. Legacy clients retain paced batches.
- Replay controls fixture: 37 checks (300-record burst, bounded queue, forged/stale ACKs, repeated seeks, pause preservation, cancellation); TeamFeatures: 170 assertions. Companion Unity client fixture: 24 checks including frame-budget behavior and compact UI. No deployment yet.


## 2026-10-19 - Replay loading clock and bounded seek pipeline

- Replaced stop-and-wait per catch-up batch with at most eight outstanding batches, preserving 128-record/32KiB batch limits and ordered acknowledgements. Final state still waits for all consumed data. Invalid/stale/foreign ACKs cannot advance the window.
- Capability 11 adds negative final barriers: client waits for the Zone spectator HUD to finish loading before acknowledging. Initial playback now reconstructs only through the first non-waiting phase and holds the clock until ready; rewinds clamp to that playable start to avoid a waiting-loader deadlock. Legacy clients retain compatible behavior.
- Validation: 41 server functional checks; optional benchmark adds 12 checks. Same fixture path, 1500 records, injected 100ms ACK delay, three samples: window 1 = 1309/1317/1319 ms (median 1317); window 8 = 227/227/229 ms (median 227). Reproduce with dotnet run --project tests/BNLReloadedServer.ReplayControlsFixture -c Release -- --seek-window-benchmark. This isolates network pacing, excludes client map loading, and is not an end-to-end rewind speed claim. TeamFeatures 170 assertions passed.
- Client compiled successfully using Unity's Roslyn compiler and existing editor response file, with outputs redirected into artifacts/replay-seek-window so the user's running editor was not interrupted. Real replay test still needed. Rewind still reloads/reconstructs because old recordings contain no intermediate snapshots; checkpoint seeking remains a larger change, not implemented here.

## 2026-10-21 - Action scoring deployment preparation

- Owner authorized deploying action scoring for the Unity custom-game sound test. Integrated feature/player-action-scores with current main, preserving replay pipeline/readiness changes.
- Linux x64 Release publish passed with warnings as errors. ActionScore 55, ReplayControls 41, KillAccolade 49, HealAttribution 17, Reconnect 10 and Auth 4 checks passed; HealAttribution retains an existing nullable fixture warning.
- Published dependency and runtime JSON match live 381a2c5 semantically (line-ending differences only). Existing live configuration and data are retained by the code-hotfix deployer, with rollback on startup health failure.
- Scoring uses built-in defaults when global_logic.action_scores is absent; catalogue values remain optional and editable. Deployment outcome is recorded in the client workspace progress log.

## 2026-10-21 - Hero blocks earn build and destruction action scores

- Include DeviceType.Hero alongside Device in both confirmed placement paths and enemy unit destruction. Reuse CDB build points, cost-based destruction values, objective bonuses and rebuild cooldown. Ordinary terrain, friendly and neutral destruction remain excluded.
- ActionScoreFixture passes 60 checks including hero build/destruction and exclusions; Linux x64 Release publish passes warnings-as-errors. Applying as follow-up to the authorized action-scoring deployment.

## 2026-10-21 - Hero-specific action scoring

- Added CDB-tunable support/action rewards for Tony, Trondson, Genie, Doc, Vander, Sweet Science, Yeti, Sarge, Roly, Cog, Astarella, Kira and Kreepy. Nigel/Ninja unchanged. Confirmed-build and destruction hooks resolve active aura sources; applied effects resolve owning players and throttle repeated applications; actual repair/heal amounts bank into chunks. Doc F healing replaces generic healing for the same amount.
- Lamp loot carries score-only builder provenance without changing gameplay ownership; collected pickup credits another friendly once. Hero block destruction also covers the voxel path.
- ActionScoreFixture: 92 checks. HealAttribution:17, live-catalogue JinxSurge:9, Permafrost:31 and IgnoreCaster:12 checks pass. The older AvalancheSlow fixture expects the obsolete immediate-slow ability and is incompatible with the current area-spawn catalogue; current Permafrost fixture validates the deployed area behavior instead. Initial missing fixture arguments were corrected.
- Companion client adds persistent Interface > Action score HUD, disabling both popup/audio without suppressing totals; Unity 18 popup/state +12 audio checks pass. Deployment is the authorized continuation of action scoring; details recorded in client progress log.

## 2026-10-21 - Prevent Tony repair XP farming

- Server tracks outstanding enemy health damage on device units and hero/device voxels. All actual healing consumes it before scoring, including passive/other-player repairs. Direct/periodic unit damage and both voxel splash routes covered; shield absorption excluded and removed/replaced blocks cleared. CDB rates unchanged.
- ActionScore 114 checks and HealAttribution 17 passed. Linux x64 Release publish passed with warnings as errors.

## 2026-10-21 - Cost-proportional build scores

- Replace flat build points with ceil(actual charged cost / build_cost_per_point), bounded by build_min/build_max (defaults 20, 1, 50). Device units and hero/device voxels use confirmed TotalCost. Free/invalid builds give zero; objective bonuses and rebuild cooldown remain. Update CDB example and README; legacy build field ignored.
- ActionScore fixture 127 checks passed, including cost tiers, rounding/caps, free builds, CDB roundtrip/legacy field, both eligible device types, objective bonus and cooldown. Linux x64 publish passed with warnings as errors.

## 2026-10-21 - Exclude cheap trap/pad build rewards

- Exclude block_fast_movement, block_bounce, block_thorn_bush, block_glue, block_caltrops, block_bear_trap and block_force_gate families from direct build XP. Match exact IDs and underscore variants; both confirmed placement routes pass the built card ID. Other rewards remain unchanged.
- ActionScore fixture 147 checks passed, including all 18 current catalogue variants, future variant/hero typing and a positive turret control.

## 2026-10-21 - Reward enemy trap effects instead of placement

- Exclude block_ice, unit_device_generic_landmine and unit_device_generic_fire_trap families from build points. Add owner-attributed mine/fire/glue/bush rewards (10/5/5/5), with independent trap_effect_cooldown default 15 seconds per owner/reason/enemy; all CDB-tunable. Mines use confirmed damage, glue admitted slow, bush/fire damage or admitted status with shared deduplication.
- ActionScore 174 checks passed, including actual BlockSource ownership from MapBinary, repeated status/damage, multiple enemies, friendly and environmental rejection, new build exclusions and CDB roundtrip.

## 2026-10-23 — Restore Discord player count; retire China integration

Old bot credential rejected by Discord; main service had over 32,000 restarts. New application 1555383788865982544 authenticated and joined Block N Load Community (283986382660173824). Prepared single-service deployment with exact previously verified Rust binary, tracked recovered source/provenance, and identity/membership preflight. Six Python regression tests pass. Deployment verification will be recorded after rollout. Game service is not part of this deployment.

## 2026-10-23 — Discord player-count bot restored; Chinese integration retired

- Discord rejected the old credentials; the main service had exceeded 32,000 restarts. Stopped the failed loops. User completed Discord MFA; replacement token transferred over SSH stdin into root-only runtime storage, then installed as root:bnldiscord 0640 main.env. No credentials added to source or logs.
- Application/bot 1555383788865982544 joined Block N Load Community (283986382660173824). Existing application name, icon and game Rich Presence assets preserved. Verified bot identity and guild membership through Discord API.
- Server implementation committed/pushed to devprbtt/bnlReborn branch fix/discord-presence-single-server (initial implementation 2923881). Tracks exact recovered Rust reconnect source/lockfile/provenance, deployment, unit, preflight and tests. Six regression tests pass on Windows and production Linux. Permanent invalid-token/missing-membership errors exit 78 without automatic restart; transient network failures retry after 30 seconds.
- Deployed /opt/bnl-discordbot/releases/single-server-20261023 at 22:35 Sao Paulo (2026-10-24 01:35 UTC), retaining verified Rust binary SHA256 3c3e37d96b43ea2123ea8c0ca2800e3f5bd32880e2b8b3d0b21b2a33252b2e09. Added Python preflight; no Rust rebuild. Protected prior main configuration at /root/config-backups/discord-single-server-20261024T013511Z.
- Deleted retired Chinese unit and /etc/bnl/discordbot/china.env; unit is not-found/inactive. Deployment/reconnect tooling now targets only the Reborn bot. Temporary token staging file removed. Historical source archives and log entries retained as provenance.
- READY and local status count verified; Discord member list visibly displays BlockNLoad Reborn online with “0 players online”, matching the local endpoint. Targeted bot-only socket interruption produced RESUMED and immediate presence refresh, unchanged PID 1588486, NRestarts=0. Game service remained PID 1467174, NRestarts=0 throughout; no game restart or client release.
- Final check: minute refresh observed at 01:36:37 UTC (poll 7); bot enabled at boot, RestartPreventExitStatus=78, zero restarts.

## 2026-10-24 - Per-match leaderboards, casual MMR board, weather from map load (not deployed)

- Count boards (kills, headshots, assists, objective damage, kill-chain accolades) rank the average per counted match; average DPS, win rate and KDR stay rates; hours, matches and best streak stay totals/the record. Every board needs 20 counted Ranked/Casual rounds (`MinMatchesPlayed`); accolade averages divide by, and need 20 of, the rounds tracked since accolades began.
- New `casual_mmr` board: account casual rating (LeagueRules.Points) for ladder accounts with 20 counted casual rounds; no trophy (LeaderboardTrophies.Names unchanged). Trophy descriptions reworded for averages and checked against the 240-character limit (the first draft overflowed for Unbelievable and would have failed trophy install at startup).
- `WeatherStartTime` is now the zone creation time instead of the first assault, so clients can start the day/night cycle at a seeded random point from map load (client beta.81). Current clients run their existing schedule from map load.
- PlayerLeaderboardFixture 36 checks, TrophyFixture 64, ClanFightFixture pass; Release build -warnaserror clean.

## 2026-10-24 - Key assist, kill objective bonus, no ice destruction score (not deployed)

- `Key assist` (KeyAssist 35, CDB-tunable; must be >= Assist) replaces the assist for assisters in TopDamagers (the Accomplice rule: most enemy damage within the assist window, ties share). The killer leading on damage keeps only the kill. Client shows the reason text verbatim, so no client change is needed.
- Kills (regular and accolades) near an objective get max(+25%, KillObjectiveBonus 20) as the objective bonus; other actions keep +25%.
- `block_ice` family gives no "Device destroyed" and no Trondson demolition assist, in the voxel path (MapBinary.DamageBlock, the real ice path) and the unit paths. Fixture drives a real MapBinary voxel destruction with a hero-block positive control; disabling the guard makes the ice check fail.
- Live CDB global_logic.action_scores is null, so code defaults are live. ActionScoreFixture 193, ProgressionFixture 19, ScoreboardMetadataFixture 20, PlayerLeaderboardFixture 36 pass; Release -warnaserror clean.

## 2026-10-26 — Revised device action scoring (not deployed)

- Enemy-side spawns 20 / friendly-side 10 per use; turret 5/100 HP; beacon, Miasma and Torpedo 5/50 HP; Gravity Well explosion and Kreepy box 5/hit; fake-block enemy trigger 10. Genie lamp gas inherits builder at creation and earns 5/100 HP.
- Removed device ammo/spawn/radar/bomb/mine/trap/Roly/C4/mortar repeat gates. Portals retain 60s per ally/owner; Tiki retains 1s; unrelated hero ability timers unchanged. Radar pays per 200ms scan tick with overlapping-owner deduplication, not also on effect admission. Damaging trap status applications do not double-pay damage; unchanged glue/goo refreshes do not score.
- Beacon/mortar/C4 destruction uses cost-based award exactly once (ceil base cost/20, min5/max50), without flat mortar/C4 extras. Objective bonuses unchanged. 278 scoring checks cover source resolution, gas ownership, fake triggers, boundaries and costs; six regression suites pass. No live gameplay validation or deployment.
- Scoring rules, full inventory, Discord guide and all four pending patch-note surfaces updated. Website notes prepared, not deployed; launcher/in-game notes await a later signed release.

## 2026-10-26 — Remove mobile weapon spread except Nigel (pending deployment)

- User requested removing movement and airborne inaccuracy for all heroes except Nigel. Added tools/remove_mobile_weapon_spread.py to the server repository: public hero gear families, including variants and aimed tool cones, get move_mod=jump_mod=0. Nigel's entire gear_hunter family and his hero gear references are protected. Normal angle, bloom, recoil, crouch and non-gear cards stay unchanged.
- Read-only live catalogue audit plans 37 changed cards / 43 cones. Source-derived fixture records provenance and hashes. Six tests pass: exact allowed-field diffs, protected/unrelated cards, aimed fire/variants, invalid source rejection, repeat-apply no-op, backups/read-back, preflight conflicts and rollback after failed writes.
- No production writes or restart. Deployment remains separate. All four notes surfaces prepared; website not published, launcher/in-game source awaits a later signed client release.


## 2026-10-26 — Admin test dummy (prepared, not deployed)

Added Admin Tools > Test Dummy with whole-number HP from 1 to 10,000,000 (default 1,000). The existing authorized debug command spawns a stationary opposing target three metres ahead, rejects blocked placement, and gives it player damage semantics. It has no connected-player identity and never schedules a respawn. Updated client hero-prefab loading for the target and guarded its voice handler against a missing player ID.

Validation: DebugDummyFixture 31 checks, ActionScoreFixture 278 checks, HealAttributionFixture 22 checks pass. Unity 6000.3.23f1 batch compile exits 0 (Logs/admin-dummy-compile.log). No live multiplayer/rendering test or deployment performed. Four canonical patch-note surfaces are prepared, not live; server and signed client releases are required.


## 2026-10-27 — Remove hardcoded currency grants (not deployed)

PlayerDatabase and DummyPlayerDatabase supplied a shared dictionary containing 10,000 Virtual/Gold and 1,000 Real/Platinum in both initial player replication and GetCurrency. Replaced it with a fresh zero-balance snapshot from PlayerCurrency.CreateStartingBalance for each read. UpdateCurrency is currently a no-op and shop purchases return false; there are no persisted wallets to reset. Awarding match Gold requires persistent per-player wallets and idempotent match settlement, to be implemented separately once the reward policy is chosen.

Validation: server Release build with warnings as errors passes, currency fixture 8 checks confirms both zeros and isolation between player snapshots/readbacks. Production provider constructed normally; dummy provider wallet reads isolated from unrelated XP catalogue-dependent construction. No deployment. Four canonical note surfaces prepared, not live.

Suggested initial economy (proposal only): 10 Gold per active minute, capped at 300 per match, +50 for winning; no AFK, abandonment or custom/testing rewards; backfill earns for active participation. A 15-minute match yields 150/200 Gold loss/win. Archived catalogue shop prices commonly include 5,000 and 11,000 Gold, equivalent to 25/55 wins at that example rate; shop pricing/unlocks need review before purchases are enabled. No raw performance-score multiplier to avoid hero-role and device-scoring bias.
