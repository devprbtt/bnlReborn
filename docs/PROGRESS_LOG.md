## 2026-10-29 - Radar and ammo score cooldowns; kills and damage pay more

- Replay award breakdown (20 matches, client repo tools/action_score_breakdown.py): Radar spot was 47% and Ammo supplied 16% of all action score since the 26 Oct device-scoring deploy; one Sarge scored 41,729, 85% Radar spot. ReportScoreSpots runs on the 200 ms buff check and neither award had a cooldown (AmmoCooldown existed unread).
- Radar spot: once per owner and enemy every radar_cooldown (15 s), shared across that owner's radars. Ammo supplied: once per crate owner and ally every ammo_cooldown (15 s).
- Compensation: kill 75 (was 50) and every accolade +25 (85/100/125/150/175/200). New "Damage dealt": enemy-hero health removed with hero damage credit (own weapons/abilities) is banked across targets and paid 20 per full 100 (hero_damage_chunk/hero_damage_points), so single hits raise no notification. Simulated on the live rows, the median match moves from 3,592 to about 1,960 (progression curves assume ~1,840).
- ActionScore fixture 285 checks (cooldown inside/expiry for both, chunk banking and carry-over, friendly and device-credited damage excluded); Progression 19, DebugDummy 51, WeaponSkin 29, ReplayControls 65; warning-as-error build passes.



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


## 2026-10-27 — Hero milestone rewards and persisted wallets (not deployed)

Implemented the user-approved schedule: Gold 10=250, 20=500, 30=750, 50=1,250, 60=1,500, 70=1,750, 80=2,000, 90=2,250, 100=3,000, 150=5,000, 200=10,000; Platinum 50=100, 100=500, 200=1,000. Totals per hero 28,250 Gold / 1,600 Platinum. Existing heroes receive all previously earned milestones once. No level-40 reward.

Added persistent Users gold/platinum columns, fresh wallet snapshots, server-internal PlayerData wallet replication, and online client currency updates. HeroMilestoneClaims primary key is player + hero hash + level. Startup backfills in a transaction. Match settlement writes XP, claims, wallet and HeroProgressSettlements retry marker atomically; includes switched-away heroes. Profile sanitization now re-reads under its lock so it cannot overwrite a wallet/XP settlement while waiting. Existing fake currency is not imported. New accounts still start at zero.

Validation: Release build warnings-as-errors clean; HeroMilestoneFixture 50 checks includes old-schema migration, all thresholds, backfill/restart idempotency, rollback, separate heroes, database/wire round trips, real master settlement with switched hero, online cache/notification, duplicate match replay, and profile-load race. ProgressionFixture 19, CurrencyFixture 8, MmrFixture 3,603 checks pass. Only isolated temporary databases used; no deployment or production migration run. Four canonical notes prepared locally, not live. Coordinated master/region update required for internal player-data extension. Match Gold/stat-leader bonuses/end-game Gold column remain pending separately.


## 2026-10-27 — Casual class bans and randomized snake draft (prepared)

- Standard `game_mode_friendly` now uses a 10-second entry wait, three 15-second class ballots (Skill, Brawn, Brain), 30-second individual snake picks and 5-second prestart. Other Friendly cards, custom, ranked and clan modes retain their policies. Casual map voting is retained.
- One authenticated, final vote per player per phase; plurality bans one hero globally, random ties including an empty ballot. Everyone voting advances immediately; offline seats remain until timeout. Stale phase packets/timer callbacks cannot advance a later phase.
- Starting side and within-team order are randomized independently of MMR. 4v4/5v5 require all three classes per team; smaller teams have no class minimum. Ranked's global uniqueness and two-per-class cap are retained. A feasibility check protects both teams' remaining picks and ownership, including automatic defaults. New backfill players get a legal editable default and cannot bypass bans. Reconnects retain the ballot, bans and draft order.
- Client adds a native-canvas ballot with portraits/counts/locked-vote feedback and a draft-order/status panel. The original hero picker and Block In button handle draft selection. LobbyUpdate optional bit 9 carries a JSON snapshot (unchanged two-byte mask); lobby request 20 submits phase + hero. Publish matching clients before enabling this server change; old clients cannot vote or present these phases.
- Validation: server Release warning-as-error build clean; CasualDraftFixture 463 checks, PublicQueueRulesFixture 64, ClanFightFixture 217, RandomHeroesFixture 4018 (one existing CS9216 fixture warning). Unity compiled; actual offline lobby validation and screenshots verify voting, locking, draft transition and clearing. Live multiplayer and signed builds have not been tested for this change.
- All four player-facing note surfaces prepared: versioned CHANGELOG-2026-10-27-casual-draft-v1.md, launcher, in-game JSON and website. Nothing deployed or published. Launcher/in-game entries await a signed client release.


## 2026-10-27 - Casual hero picks capped at 20 seconds (prepared)

Reduced standard casual draft SelectionTime from 30 to 20 seconds. Lock-in still advances immediately and timeout locks a legal default. The three ban phases remain 15 seconds each. Updated the existing fixture expectation, editor preview configuration and all four prepared note surfaces in the client repository. Not deployed.


## 2026-10-27 - Catalogue navigation compatibility hotfix

Beta.83 exposed a client contract violation: RebornShopBlocks enumerates every UnitDataPlayer and resolves its class, but the new admin dummy advertised Key.None. The client threw Invalid card id 0 and stranded its menu transition. Give the dummy a dedicated public class card with an empty AvailableDevices list while retaining no special devices; it contributes no block availability and is absent from GlobalLogic.AvailableHeroes. The target remains Sarge-shaped/Brawn, receives player damage, has configurable HP and no respawn. DebugDummyFixture now checks class resolution, block traversal after wire serialization, and non-playable roster membership in addition to existing damage/death checks.
Validation: 35 debug-dummy checks, 463 casual-draft checks, TeamFeatures/Auth/Accounting fixtures, warning-as-error Release build/publish and vulnerability audit passed. The actual Unity Catalogue UI consumed the server-exported binary dummy/class cards and passed 24 shop checks: opens, all 15 heroes, block browser, details and return navigation. No new UI exceptions.

Deployed server revision 6535d79a0d41c07723f92310d6cb86284df9385e; guarded health gate and live DLL hash verified. Zero active matches at restart. Server rollback snapshot: /root/config-backups/bnl-server-hotfix-20261027T153704Z. Existing beta.83 clients load the fix on restart; no replacement client feed.

## 2026-11-07 - Time Assault map selection

Added selected-course Matchmaker RPC 35 while preserving the legacy featured-course request 17. TimeAssaultCourses validates an unambiguous configured course, hero and TimeTrial map card/payload before queue removal. Solo launch rejects disconnected users, squad members and active-instance replacement. Restart uses the current instance MapInfoCard and validates before teardown; in-flight/loading restarts and removed courses leave current state intact. No catalogue migration: all six current maps already have course definitions.

Release warning-as-error compilation and self-contained linux-x64 publish passed. TimeAssaultFixture passes 48 checks using the local PTR catalogue and actual six map payloads, including course-specific hero/devices/goals/objectives, unknown/duplicate rejection, selected-map RPC and boolean reply, region launch guards and restart guard/teardown boundary. No live zone creation or live multiplayer playthrough in the fixture. Deployment not performed; server must precede the matching client. docs/TIME_ASSAULT_COURSES.md contains the protocol/deployment contract. Independent of the pending casual free-pick branch.

## 2026-11-07 - Time Assault loading acknowledgement

Capability 14 adds bidirectional zone message 114 with a per-connection 64-bit challenge. Send the challenge after the complete TA snapshot, hold countdown start until its matching response, and reject stale, duplicate, pre-snapshot or replaced-connection acknowledgements. Load timeout cannot start a TA before readiness. TA player creation/state is sent during loading so the client can finish presentation before acknowledging. Legacy clients retain their existing handshake; matching new client is required for the final presentation guarantee.

Validation: 65 TimeAssaultFixture checks pass, including the actual start guard, token acceptance and wire encoding. This is not a live end-to-end course playthrough. Deployment remains separate from the pushed changes.

## 2026-11-07 - Completion/leaderboard value parity regression

Added six fractional/boundary samples through actual MasterServerDatabase.ApplyTimeTrialResult, saved TimeTrialData byte serialization, completion-result wire serialization and leaderboard-record wire serialization. The values retain identical float bits; slower subsequent finishes retain the prior best. Exported payload pairs are read by Unity to verify finish-screen and leaderboard formatting parity. Server fixture now passes 77 checks. Runtime behavior is unchanged by this validation-only follow-up.

## 2026-11-07 - Native TA picker review data

Export each course hero's default skin card with the existing offline review fixtures so the native map selector can show the correct existing hero portrait when a course has no thumbnail. This changes test data only; server gameplay is unchanged. All 77 server checks pass.
## 2026-11-07 - Prepare server-only casual free pick

Changed game_mode_friendly registration to FreePick (60s selection, 5s prestart, 60s requeue) and gated casual draft state on an actual DraftPick mode. Players choose simultaneously; duplicate heroes remain allowed and draft class minimums are removed. Ranked unchanged. Full and incremental catalogue registration share this normalization; no client/CDB migration needed.

Validated Release build and self-contained linux-x64 publish with warnings as errors; CasualDraftFixture 767 checks (33 new free-pick checks plus retained dormant draft coverage); PublicQueueRulesFixture 64; BackfillReentryFixture 13; RandomHeroesFixture 4018; MapVoteFixture 19 using the local PTR catalogue export. RandomHeroesFixture emits its pre-existing CS9216 Lock-cast warning; server build/publish have zero warnings. Initial test setup failures were corrected (drain queued prestart action before synthetic backfill; test single-card normalization without invoking production minimum-catalogue-size guard). No live service changes or multiplayer acceptance test. Deployment/rollback and player note: docs/CASUAL_FREE_PICK.md.


## 2026-11-08 - Supply drop schedule stalls (Wilson's Bay casual report)

- Investigated live beta.91/aadbcce replay `6d4033f5-6bd7-4d40-a5b2-a7c1804057c7:1794156202288`, map `map_sr2_wilsons_bay_mid_edit`. All three BB markers exist. At replay +240.012s assault begins and a resource supply is announced; +540.013s it spawns. No subsequent SupplyInfo announcement appears. A lone extreme BB appears at +2465.297s without a preceding schedule announcement; its trigger is not established. The replay proves the normal schedule stalled, not the exact exception that caused it.
- Found unsafe timer-thread access: OnSupplyTimerElapsed enqueued only creation, then enumerated the mutable zone unit dictionary, published updates and restarted the one-shot timer on the timer thread. System.Timers can silently swallow exceptions there, leaving the schedule permanently stopped. Move the entire operation onto the serialized zone queue. Ignore stale timer callbacks; stop after match end, rejected queue or finite sequence completion. Keep existing map positions, tier ordering, delays and Conquest filtering.
- Added SupplyScheduleFixture: 34 assertions cover queue ownership, actual resource/BB unit creation and height, next-drop announcements/rearming, initial/repeating/repeat-only sequences, absent markers, stale callbacks, end-match and closed queue. The pre-fix implementation fails the queue-ownership regression at GetSupplyPosition; fixed build passes with zero warnings/errors under warnings-as-errors. This deterministic ownership test is not a reproduction of the unknown live exception.
- Server-only fix; deployment pending. No live restart performed.



## 2026-11-08 - Public player reputation (prepared, not deployed)

- User approved 100 initial reputation, -10/-15/-20 abandonment penalties and +1 qualifying clean completion, casual/ranked only, no automatic chat deductions. Follow-up explicitly replaces daily escalation with consecutive abandonment; qualifying completion resets the streak. Added persistent transactional ReputationEvents ledger, rollout epoch (no retroactive penalties), public read-only API and deterministic out-of-order settlement.
- Recovery requires 180 active-phase seconds, positive recorded score and remaining until result. Match completion settles changes. 120-second accumulated disconnect grace; reconnect window and personal MMR return allowance aligned, existing shorter config respected. Timer expiry retains Disconnect instead of masquerading as explicit Quit. No new reputation-driven bans/cooldowns.
- ReputationFixture 37 checks; MmrFixture 3656; BackfillReentry suite; ReconnectFixture 10; ZoneLeaveOrderFixture 11 pass. ZoneLeaveOrder fixture repaired to wrap the production RecordingSender instead of assigning an obsolete SessionSender type (expected friend-lookup stub errors remain). Release build with warnings-as-errors passes. See PLAYER_REPUTATION.md for policy and limitations.
- Client implementation/visual review lives in blocknload-unity-upgrade. Feature branch only; not deployed.


## 2026-11-09 — Retire country-flag crates (next update; not deployed)

All 13 national crate devices are excluded from ownership, including persistent grants, and rejected as direct loadout choices. Saved/submitted loadouts normalize them to the stock crate before ordinary class/slot validation. Catalogue replication and incremental updates remove group entries, disable shop entries and replace legacy block visuals/device presentation with stock crates while preserving block IDs. No player database, grant history or production catalogue was modified.

Validation: Release build with warnings as errors; CountryCrateFixture 510 checks (all 13 countries, all applicable heroes, inventory/grant exclusion, legacy numeric IDs, real incremental device updates, shop exclusion, 16 unrelated crate variants); existing LoadoutRulesFixture 13 checks. Fixture accepts a catalogue JSON path; it supplements Faroe Islands/Georgia when using the archived pre-beta91 snapshot. Deploy with the next update, independently of pending reputation changes.


## 2026-11-09 — Retire four hero skins for next update

Retired Brown Bear (`skin_ai3d_abe_bear`), Prime Commander (`skin_ai3d_cogwheel_prime`), Solid Gold (`skin_ai3d_roly_gold`), and Clockwork Vander (`skin_ai3d_magnus_steampunk`). Authoritative ownership denies them even with old grants; catalogue replication/incremental updates hide shop offers and remove hero picker entries. Existing SafeSkin paths select an available same-hero skin. Login cleanup replaces retired skins before stale-loadout validation so valid devices/perks are preserved. Legacy cards, art, purchase/grant records and separate weapon skins remain intact.

Validation: 269 targeted checks across four removals and 213 other skins, including real incremental updates and login migration; previous country-crate fixture 510 checks; Release build with warnings as errors passed. No production deployment. Include alongside country retirement commit 8346a62 in next combined release.


## 2026-11-09 - Beta.92 server follow-up prepared for authorized deployment

Combined reputation (286012c), 13 country crate retirement (8346a62) and four hero retirements (b42b4ca) on live 968bff1, preserving both TA fixes. Only append-only documentation conflicted; retained every feature entry. Release warnings-as-errors build passed. Reputation 37, MMR 3656, country crates 510, retired heroes 269, Time Assault 106, authentication 4, team features 170, currency 8, plus BackfillReentry, Reconnect, ZoneLeaveOrder, MatchHistory, LobbyJoinOrder and BuildPhase fixtures pass. Transitive dependency audit reports no vulnerable packages. Framework-dependent linux-x64 hotfix publish matches production dependency libraries exactly; self-contained publish was also successful but is not the deployment artifact. User explicitly authorized client and pending server changes. Recheck empty player count and live revision immediately before guarded deployment.


## 2026-11-15 - Nine Discord topic server fixes prepared

Added authenticated Practice Grounds start using the full terrain map from MapDatabase (catalogue map summaries omit BlocksData), native tutorial mode and Sarge loadout; rejects unavailable maps and occupied/squad sessions before leaving queues. Actual signed Unity local PTR test reached a spawned tutorial player after clicking the native Practice Grounds button. Reconcile idle matchmaker state on main-menu return without clearing a real queue/pop. Initial matchmade ShieldCapture/ShieldRush2 build phases now use actual map objective counts: 180/240/360 seconds for 1/2/3 cubes as approved; custom timing, intermediate build rounds and other modes retain their existing rules. Added read-only replay-ID metadata resolution through the existing public match archive. Eleven focused server checks, 48 replay recorder assertions and 11 live spectating checks pass; Release warnings-as-errors build passed. No production deployment. Client/review evidence lives in the private Unity repository docs/DISCORD-NINE-TOPICS-REPAIR.md.


## 2026-11-16 — Beta.96 integrated server release

Integrated leaderboard score-per-minute/account levels and avatar/rating metadata, partial case-insensitive player search, GUI archive/queue/presence support, late spectator results and reconnects, Practice Grounds terrain/start, idle-queue reconciliation, 3/4/6-minute initial objective build timing and shared replay lookup. Release branch descends from live 11e7db5150f1; all five feature merges are preserved.

Warning-as-error Release build and linux-x64 publish passed. TeamFeatures, Auth, Accounting, PlayerLeaderboard, PlayerSearch, MatchHistory, PublicQueueRules, MatchChat, LiveSpectate, NineTopics and ReplayRecorder fixtures passed; transitive dependency audit reported no vulnerable packages. Deployment is authorized by the owner and tracked with client beta.96 release evidence.


## 2026-11-18 — Fast Restock station-only gains and editable CDB text

- Removed the runtime Fast Restock description rewrite from full/incremental catalogue updates. Descriptions and translations stay CDB-owned through wire serialization.
- Excluded Fast Restock ammo gain from global buffs, matching its healing treatment. Direct gains and mixed/shared-key regeneration use source-aware station contributions; attribution follows the effective ammo weights. Existing CDB percentages and brick penalty remain authoritative.
- Added a guarded, idempotent one-time description migration with offline candidate mode, read-only online default, revision-checked writes, private backup and readback. No live CDB writes or deployment performed.
- Validation: HealingPerkSources 128 checks; HealAttribution 22; BrainsRecovery 24; HealBane 36; GenieStationHealing 29; KillerAmmo ordering suite; three Python migration tests. Release warnings-as-errors build passed, zero warnings/errors. No live client/match test.
- Details and rollout steps: docs/FAST_RESTOCK_STATIONS.md. Prepared on production base bf49715; no client/feed publication.


## 2026-11-18 — Experimental admin gravity tool, flight and health

Implemented capability-15 admin funny mode for every match and both teams, server-owned holding/charge, normal client launch physics, swept terrain checks, and lifecycle/role/lease cleanup. Added flight support via the companion client and exact 2000 health while enabled, restoring normal buff limits on exit. Release build and AdminFunny 45 checks, TeleportPlacement, DashMiss 330794 checks and Auth 4 assertions passed. Client builds/checks are tracked in the client repository. Not deployed. Details: docs/ADMIN_FUNNY_MODE.md.


## 2026-11-18 — Admin-mode overhead name suppression

Added a server-owned presentation marker (BuffType.AdminHideNameplate, wire value 60) to the mode's ordinary replicated unit buffs. It reaches teammates, enemies, spectators and late/reconnecting clients through the existing update/snapshot route, and is removed on mode cleanup. Catalogue effects cannot supply the marker. The updated client suppresses the name Text and its title alpha during binding, Update and LateUpdate while preserving native health-bar content/fill/visibility; normal title state returns when the marker clears.

Validation: Release server build zero warnings/errors; 52 admin-mode server checks, including marker serialization, late-observer snapshots, effect recomputation, cleanup and catalogue injection rejection. Unity nameplate checks consume actual server-generated on/off packets and verify both teams, observer independence, native health content, late-frame suppression and restoration. Requires updated observer clients; no public release or deployment performed. Client evidence: reports/admin-nameplate. Server feature branch remains feat/admin-funny-mode.

## 2026-11-18 — Shared admin transformation storm epoch

Publish the latest authorized transformation's Unix millisecond epoch through exact high/low buff fields 61/62 on every active admin. Full snapshots cover late observers; existing admins receive a new epoch on another activation, keep it when that admin exits, and duplicate enable requests preserve it. Normal mode cleanup removes weather fields. Catalogue effects cannot inject them. The companion client adds match-wide fog/rain/thunder, three-second arrival, four-second exit and reversible map lighting/sky changes.

Validation: Release warnings-as-errors build passed with zero warnings/errors; AdminFunny fixture passed 59 checks, including exact epoch serialization, multi-admin lifetime, duplicate enable, effect recomputation/injection and cleanup. Companion client passed 40 graphics-enabled Unity Play Mode checks. No deployment. Details: docs/ADMIN_FUNNY_MODE.md; companion client evidence: reports/admin-storm.

## 2026-11-18 — Entity beam and homing fireballs

Added capability-16 combat input intent in the existing admin request envelope and server-owned primary charge/release beam plus secondary homing fireballs. Preserve dig-tool gravity controls, native terrain/health/scoring/team/protection rules, and existing mode cleanup. New per-connection observer messages 117/118 support all loaded upgraded players and spectators through GameInstance callbacks; the shared buffered service cannot be used for capability-gated events. Primary full charge 2.5 seconds, four-second cooldown, 160 metre range and 2–7 metre/5000–25000 block-damage blast. Secondary .8 second cooldown, 24 m/s bounded homing/swept collision, five-second lifetime and native 2.5-metre blast.

Validation: zero-warning warnings-as-errors Release build and 101 server fixture checks, including actual terrain destruction, native enemy/friendly/spawn-protection/occlusion behavior, charge/cancel/cooldown, homing selection/steering/collision/expiration/cleanup, capability gates and production observer connection selection. Companion Unity VFX/protocol/render checks and preview artifacts: client reports/admin-beam. No deployment. See docs/ADMIN_FUNNY_MODE.md.

## 2026-11-18 — Secondary weapon gravity well and repulsion

Added gear-index-2 attraction and charged repulsion while retaining beam/fireballs in the first combat weapon and targeted grab in dig. Use source-owned synthetic entries in native pull reconciliation so each admin's well releases independently and ordinary effects survive. Pull both teams within 28 metres at force 22; block acquisition through terrain and release departing/near/occluded targets. Two-second charged repulsion scales 12–32 metre radius and 18–75 force with edge falloff and five-second cooldown, using native knockback without direct health/terrain damage. Existing lifecycle and slot checks clean up sources/charges. Capability 17 gates actions 11–15 and observer message 119; GameInstance relays to loaded individual observer connections. Updated client flight retains native forces and collision.

Validation: warnings-as-errors Release build zero warnings/errors; extended admin fixture 139 checks; existing catalogue-backed PullFixture all 19 assertions. Companion Unity 19 graphics-enabled gravity/protocol/collision/render checks plus 15 original gravity/flight checks. No deployment. Details: docs/ADMIN_FUNNY_MODE.md; client evidence: reports/admin-gravity.

## 2026-11-18 — No-cooldown F invisibility

Added capability-18 action 16 to toggle server-owned AdminInvisible state in living admin mode. Publish reserved buff key 63 through ordinary unit updates/snapshots, reject CDB injection and preserve it during ordinary effect recomputation/duplicate enable. Clear on mode/death/disconnect/role/lease cleanup without changing health or granting immunity. Companion client intercepts native F only in supported admin mode and hides world model/gear, radar outline and health/minimap indicators, retaining physics and restoring prior presentation on reveal.

Validation: warnings-as-errors Release build zero warnings/errors; 164 admin server checks; companion Unity 22 graphics-enabled visibility/protocol/render/collider/UI checks and 20 prior nameplate/palette checks. No deployment. Details: docs/ADMIN_FUNNY_MODE.md; client evidence: reports/admin-invisibility.


## 2026-11-22 — Restore original Time Assault course trap stats

Prepared scoped historical trap variants from the original downloaded CDB's actual Time Assault map unit references. The course Acid Trap uses base radius 2.0, not the anomalous player lvl01 radius 3.0. Added the full 29-card gameplay dependency closure, seven unit overrides and ground-trap remapping in the TimeTrial runtime terrain copy. Native CDB distribution carries all variant cards; normal mode cards and map files remain unchanged. No live CDB write or client rebuild required.

Validation: 37,518 new assertions across six actual courses (784 proximity traps, 9,789 ground cells), original-card fidelity, actual zone/factory initialization, native map/CDB round trips, other-mode preservation, collision rejection and idempotence; fixed-spawn 504 and existing Time Assault 106 checks passed. Release warnings-as-errors build passed. Baseline generator reproduces the embedded hash. Full source hashes, player-facing note and rollout details: docs/TIME_ASSAULT_ORIGINAL_TRAPS.md. No deployment, client rebuild, public release or live client acceptance performed.


## 2026-11-22 — Complete original Caltrops presentation dependency

Live readback after the authorized Time Assault deployment exposed a missing material_caltrops target. Added it under an isolated material_time_assault_original_caltrops key, bringing the baseline to 30 cards; changed the generator and added resolution checks for every original card reference. All 37,537 current-live-catalogue trap assertions pass, covering the same 784 units and 9,789 ground cells. Baseline data/runtime stats otherwise unchanged. Deploy this corrective revision before considering the rollout complete.


## 2026-11-22 — Authoritative Time Assault prestart movement lock

Restarted zones begin in TutorialInit, which the client Waiting/Build predicate omitted, and the server accepted early movement. Consume runner movement during TutorialInit/Waiting/Build without moving or running terrain/trap/pickup callbacks; retain fresh look/heartbeat data and clear locomotion. Correct old clients through immediate native teleport, and reject countdown-sampled packets arriving after GO. Other modes/non-player units and fresh movement at GO keep their existing path. Client separately freezes before movement layers and clears queued motion. No map/timer/CDB/record edits.

Production-before reproduction fails at TutorialInit; fixed server passes 6,900 checks across six maps and three run lifecycles, fixed-spawn 504 checks, warning-as-error Release build. Paired client Unity guard passes 4,326 checks. No deployment or live keyboard acceptance; client smoothing requires a later signed build. Details: docs/TIME_ASSAULT_RESTART_MOVEMENT.md.


## 2026-11-22 — Tony cast recipients and Second Wind threats

Prepared Tony F as a one-time native timed buff for friendly players/turrets in activation range, preserving 20s duration and normal/Wider tuning. Select recipients at accepted cast using authoritative position; ignore subsequent client hit reports. Grant each player receiving the build buff 5 base points once per cast, including Tony, with existing score modifiers. Add clone-shared server-only receipt provenance and a tunable score default without editing global_logic.

Second Wind now continuously resets its quiet deadline while VisionMark-revealed or within the owner's specified inclusive 8m of a living enemy player. Preserve existing 5s quiet period, 10% max-health/sec rate and damage/channel/death/respawn rules. Add validated CDB radius and resolved description placeholder. Store a six-card revision-guarded patch, including two new native timed wrappers, and an offline generator.

Validation: native full-world Tony fixture 34 checks; Second Wind 68; existing scoring 308, class-perk catalogue/wire 31 and cooldown 21. Release warnings-as-errors build zero warnings/errors. Read-only live preflight matched all four existing card revisions/content and found both new effect IDs absent. No deployment, CDB write, client build or public feed release. See docs/TONY_SECOND_WIND.md. Commit/push the branch; deployment remains separate.


## 2026-11-22 — Prepare six-hero catalogue balance tuning

Use existing CDB fields for the requested Goo score10→8, Doc weapon allied heals−20%, Trondson primary falloff15→12m/70→50% endpoint, Sweet Science shield20→37.5% (the existing next teal tier), Vander ally beam2→2.25 HP per pulse and Nigel rifle falloff80→70m/70→55% endpoint. Cover perk variants, English stats and reticle full-damage previews. Preserve other heal sources, close damage/critical multipliers, ranges, Time Assault original traps and other scoring rules. Nine reviewed documents/26 field paths; no engine-default changes needed.

Added offline planner, revision/field-guarded dry-run-by-default apply tool with private backup/readback/rollback and concurrent-edit protection, checked-in field patch and fixture. 1,181 actual-engine assertions plus six fake-Couch deployment scenarios pass; Release build is clean. Live read-only preflight: nine pending, zero applied; no live writes. Source snapshot hash and full numbers/reproduction/rollout: docs/SIX_HERO_BALANCE_20261122.md. User did not request deployment in this turn; all values remain prepared, not live. The separately pending Time Assault restart fix is not merged or deployed here.


## 2026-11-22 — Rolling Wild shield downside

Owner requested that Roly's moving-F perk use less shielding, matching prepared Sweet Science's 37.5% instead of 40%. Replace Rolling Wild's cooldown downside: restore normal 45s from 52s and set only the perk variant shield to .375. Keep native 60% movement, all other buffs/duration, and standard Roly F's .4 shield. Update both English descriptions. Extend the pending balance plan from nine/26 to twelve documents/30 fields; all previous nine rows are identical.

Validation: combined actual-engine fixture 1,188 checks, six fake-Couch guard/apply scenarios, zero-warning/error Release build and read-only live preflight 12 pending/0 applied. No CDB write, deployment, client/feed or website release. Branch `balance/roly-rolling-wild-20261122`; rollout remains separate. See docs/SIX_HERO_BALANCE_20261122.md.


## 2026-11-22 — Roly cannon RMB damage 36 to 32

Prepared cannon secondary player splash damage 18 to 16 per shell, for 32 instead of 36 across the simultaneous two-shell burst. Updated English per-shell stat. Primary fire, world/objective damage, splash radius, timing, ammo and projectiles remain unchanged. Added one document/two fields to the pending balance patch (13 documents/32 fields total); all prior twelve rows identical. Updated guarded writer count and server rollout notes.

Validation: actual-engine balance fixture 1,191 checks, six fake-Couch writer scenarios and read-only live preflight 13 pending/0 applied. No CDB write, deployment or public release. Worktree/branch remains `roly-rolling-wild-server` / `balance/roly-rolling-wild-20261122`. Evidence: client reports/roly-cannon-rmb/ and docs/ROLY_CANNON_RMB_20261122.md.


## 2026-11-22 — Shared teal shield tier revised to 30%

Owner revised the shared 37.5% tier to 30% for everyone. Set the prepared Sweet Science/Rolling Wild shield cards to .3 and update both perk descriptions; move the shared client ShieldStrength colour stop from .375 to .3 in canonical and migration files. Audit all nine native shield buff cards: no other current 37.5% users; seven other shield strengths remain unchanged. Keep strongest-shield/cap rules and previous cannon RMB change. Combined plan still 13 cards/32 fields; ten unrelated rows identical.

Validation: actual-engine balance fixture 1,207 checks, six fake-Couch writer scenarios, native Unity compilation and nine shared tier/badge checks, and read-only live preflight 13 pending/0 applied. No deployment, live CDB write or client/feed release. Client colour mapping is prepared and needs a later build; gameplay values remain compatible with existing clients. See docs/SHIELD_TIER_30_20261122.md and reports/shield-tier-30/ in the client workspace.


## 2026-11-22 — Authorized combined server rollout

Owner requested deployment of all server changes. Combined the three pending feature branches over live 54a8a3b, preserving the original trap correction. Prepared one guarded 19-card catalogue transaction with two new Tony wrappers, private CDB/SQLite backup, atomic release switch and rollback. Combined fixtures pass 46,807 assertions; six transaction scenarios pass; warning-free Release build and linux-x64 publish pass. Push this integrated revision before deployment; verify raw/runtime cards, historical traps and live health before advancing production refs. No client/feed publication is authorized or included. See docs/SERVER_GAMEPLAY_ROLLOUT_20261122.md. Final deployment evidence is recorded separately after execution.


## 2026-11-22 — Earlier Nigel rifle falloff, prepared

Owner clarified this follow-up concerns Nigel only. Prepared primary falloff onset70m to50m for rifle and Spot Shot, preserving55% damage at100m, close damage40 and critical multiplier1.625. Updated English stats and reticle threshold. Two cards/six fields; full scope comparison confirms all other fields/cards unchanged, including Trondson and revolver. Native body damage at60/70/80/90m becomes36.4/32.8/29.2/25.6.

Actual engine fixture50 checks; six writer scenarios for new two-card plan and six for existing13-card plan; warnings-as-errors Release build zero warnings/errors. Read-only live preflight2 pending/0 applied. Writer now supports an explicit expected-card count (default13) and rejects duplicate ids. No deployment, live writes or client release. Branch balance/nigel-earlier-falloff-20261122; pending Roly death cooldown fix remains separate. See docs/NIGEL_EARLIER_FALLOFF_20261122.md and client reports/nigel-earlier-falloff/.


## 2026-11-22 — Nigel earlier falloff deployed

Owner authorized deploying the Nigel-only follow-up. Applied both reviewed rifle documents (six fields), including Spot Shot, from source3fb706f. CDB/runtime readback confirms falloff start50m, unchanged55% at100m and matching English stats/reticle. Runtime watcher refreshed both cards; no restart or binary change. Running binary remains0fee0bc8001fa02a59ac4e1b950bddc4a1fd5ac5. Private catalogue rollback backup /root/config-backups/six-hero-balance-20261122T145720Z. Health/status/auth/listeners pass and no error-priority service journal entries since application. Evidence: client reports/nigel-earlier-falloff/. Roly F death-cooldown branch remains separate and undeployed.


## 2026-11-22 — Nigel falloff40m follow-up

Owner requested40m immediately after the authorized live50m tuning. Prepared a fresh guarded two-card/six-field50-to40 patch, preserving the earlier70-to50 artifact. English stats and reticle match40m. Native damage40/37/34/31/28/25/22 at40/50/60/70/80/90/100m; close damage, critical multiplier, endpoint and other fields/cards preserved. Actual engine56 checks and six guarded-writer scenarios pass. Continue authorized live tuning after source commit/push, with private backup and runtime readback. No binary or client publication required. See docs/NIGEL_FALLOFF_40_20261122.md and client reports/nigel-falloff-40/.


## 2026-11-22 — Nigel40m falloff deployed

Applied the authorized50m-to40m revision to both Nigel rifle variants (six fields), source6368e1a. Raw CDB and running runtime readback verify40m onset and matching stats/reticle; server active/status/listeners healthy, no error-priority journal entries since apply. Private rollback backup /root/config-backups/six-hero-balance-20261122T150228Z. Binary remains0fee0bc; no restart or client/feed publication. Endpoint remains55% at100m. Evidence: client reports/nigel-falloff-40/.


## 2026-11-22 — Kira/Trondson F activation buffs prepared

Extended exact-key/marker accepted-cast selection from Tony to five Kira/Trondson variants. Preserve native ranges10m/16m, durations10s/6s/12s, cooldowns and strengths; retain timed buffs after leaving and reject late entrants. Owner chose Thrills kills refreshing original living recipients too. Track successful applications server-side, exclude new lives/expired or purged grants, and prune expired/caster lifecycle state. Preserve Kira own ammo, Idoru reload, Trondson purge/digging/mining/drill bonuses and native scoring. Keep legacy aura/shared effect cards intact.

Prepared18-card patch (11 existing,7 new);2048 other documents unchanged. New world fixture110 plus existing Tony34/scoring308/cooldown21/self-effect14/ignore-caster12/Second Wind68 pass:567 assertions. Twelve transaction scenarios pass across new18/default19 plans; Release warnings-as-errors build zero warnings/errors. Live read-only preflight18 pending/7 new. Generic deployment manifest supports explicit18 while retaining19 default; verifier reuses committed ops fixes. No deployment, CDB write or client/feed release. Source branch fix/kira-trondson-cast-buffs-20261122; Roly death-cooldown remains separate. See server docs/KIRA_TRONDSON_ACTIVATION_BUFFS.md and client reports/kira-trondson-cast-buffs/.
## 2026-11-22 — Roly F cooldown on death

Prepared native death cleanup of the active ability toggle and duration, using the existing AbilityUsed(update) to consume its charge and publish cooldown immediately. Normal Roly and Rolling Wild fast respawns retain the endpoint on the new unit id; duplicate death and death during an existing cooldown cannot reset it. No client or CDB change.

Actual-zone regression fails on live base0fee0bc and passes34 checks after the fix. Existing cooldown21, action-score308 and Second Wind68 pass (431 total); Release warnings-as-errors build zero warnings/errors. KillAccolade archive-layout assertion fails identically on unchanged live source and fixed branch; recorded without unrelated changes. Branch fix/roly-trigger-death-cooldown-20261122. No deployment or publication. See docs/ROLY_TRIGGER_DEATH_COOLDOWN.md in server source and reports/roly-trigger-death/ in client workspace.


## 2026-11-23 - Replay rewind and roster recovery prepared

Use the existing fresh-scene path for backwards seeks, including capability-12 clients; native client state cannot be reversed by removing units alone. Preserve forward seeking, requested time, pause and presentation acknowledgements. Fold PlayerInfo as complete membership replacement, with departure/empty-snapshot regression checks and matching recording oracle. Replay controls 70 checks and recorder 48 assertions pass; Release warnings-as-errors build zero warnings/errors. No live deployment. See docs/REPLAY_REWIND_RECOVERY.md.


## 2026-11-23 - Prevent consecutive BB drops at the same map point

Prepared an isolated server repair tracking the previous BB marker per GameZone. Exclude that coordinate from the next BB selection when alternatives exist, deduplicate BB marker coordinates, and apply the normal random offset afterwards. Resource drops do not overwrite BB history. Single-location maps retain their scheduled drop; absent markers retain history. Supply schedule 42 checks, Blockbuster deadline/landing suite and Conquest 100 checks pass; Release warnings-as-errors build clean. No client/catalogue changes or deployment. Source branch fix/bb-no-consecutive-drop-20261123; evidence reports/bb-no-consecutive-drop/.


## 2026-11-23 - Tony F credits the caster for other affected players

Owner clarified that Tony earns five base points per affected teammate and his self-buff must not count. Route the existing successful-buff hook through the caster Unit, reject self before deduplication, and use Build boost applied as the breakdown reason. Recipients receive no receipt points. Keep per-cast player deduplication, ranges/duration/native turret bonus, and legacy TonyBuildBuffReceived configuration key. Both variants: Tony fixture 38 checks; Kira/Trondson activation regression 115; action scoring 308; cooldown 21 (482 total). Release warnings-as-errors build zero warnings/errors. Source branch fix/tony-f-caster-points-20261123; no CDB/client change or deployment. Evidence in client reports/tony-f-caster-points/.
