

## 2026-10-19 - Replay seek back-pressure

Use capability-gated ordered barriers and viewer acknowledgements so slow clients bound catch-up traffic. Sequence numbers survive rewind generations; only the owner and current pending sequence may acknowledge. Forward seek uses the existing reader; only rewind reloads the scene. Live games and legacy capability negotiation remain compatible.

## 2026-10-21 - Hero score attribution and notification opt-out

- Resolve hero-effect score credit from validated effect sources rather than hero proximity or client reports. Support for building/destruction is awarded on confirmed work while the relevant boost is active. Cooldowns use owner/reason/target; repairs and F healing use actual health restored. Doc F replaces generic healing awards to avoid duplicate credit.
- Preserve Genie lamp builder in a server-only ScoreLampOwner field on created loot. Do not transfer gameplay ownership to award support points.
- Keep hero point values and thresholds in the existing per-match CDB rules snapshot; client notification visibility is a local persistent preference, independent of authoritative totals.

## 2026-10-21 - Repair score eligibility

Track enemy health damage per repairable device, rather than excluding owned devices or checking only the last attacker. Every actual heal consumes this budget; Tony scores only the consumed portion. Voxel budgets use damage bytes converted to HP at repair. Preserve defensive repair rewards without reusable friendly-damage credit.

## 2026-10-21 - Build reward follows resources spent

Use the same confirmed TotalCost charged by construction for device build XP, rather than base catalogue price or flat points. Keep build scaling independent of destruction scaling through dedicated CDB fields. Build time has no additional multiplier. Both placement routes feed one score function; preserve rebuild cooldown and objective bonuses.

## 2026-10-21 - Build score exclusions by catalogue family

Use built card family IDs for requested build-point exclusions, covering hero/cosmetic variants without matching unrelated effects. Keep exclusions local to direct build rewards; preserve independent support and destruction rewards.

## 2026-10-21 - Trap engagement scores

Award confirmed enemy impact rather than placement for requested traps. Use admitted status application for slowing and damaging status, and actual damage for mine/fire/bush hits. Share per-owner/reason/enemy cooldown between damage and status so a single effect cannot pay twice. Retain source ownership on spawned fire and periodic effects; no reward for unowned map hazards. Yeti ice only loses build rewards; no new ice effect award requested.

## 2026-10-23 — One Discord presence bot owned by server repository

Retire the China service and credential; poll only the local Reborn player-count endpoint. Track the recovered Rust reconnect implementation and lockfile with provenance in infrastructure/discord-presence. Preserve the application shared with game Rich Presence. Validate bot identity/membership on startup; permanent credential/configuration failures exit 78 without restart, transient failures retry after 30 seconds. Retain the verified Rust executable and add a Python standard-library preflight, avoiding an unnecessary live-server rebuild. Credentials stay outside Git in root:bnldiscord 0640 runtime configuration.

## 2026-10-26 — Device rewards follow confirmed events

Use existing MapBinary.OnEnemySide boundary for the user-confirmed doubled spawn reward (20 vs10). Replace flat mortar/C4 destruction extras with shared cost-based destruction, including Astarella skybeam. Remove requested device timers while retaining portal limits, target ownership checks and objective bonuses. Damaging traps score actual damage; non-damaging slows score newly admitted applications. Preserve lamp builder ownership before gas initial effects run; pickup behavior stays separate. Radar scores at the existing server scan cadence, once per owner/target/tick across overlapping radars, and once per device lifetime. Legacy interval/flat-bonus properties remain parseable but inert. No client protocol change or deployment.

## 2026-10-26 — Mobile accuracy through catalogue modifiers

Set both movement and jump cone modifiers to zero, since the client applies jump instead of movement while airborne. Removing only jump would make jumping more accurate than walking. Keep the client formula and normal spread/bloom/recoil/crouch settings intact. Exempt all Nigel weapons and variants, not just his scoped rifle, per user instruction. Use a dry-run-first, revision-guarded catalogue migration with private backups and rollback; no server binary or client build is needed for the balance settings themselves.


## 2026-10-26 — Test targets reuse player damage without player identity

Generate a separate catalogue unit cloned from Sarge presentation, with player health type and static movement, no passive effects, capture, loot or respawn features. Each instance carries its requested max HP through the replicated HealthCap buff (base HP 1), including status-effect recalculation and late-join snapshots. Keep PlayerId/OwnerId unset so the dummy does not become a connected match participant or automatic respawn candidate. Route spawning through the existing Core/Admin-authorized debug RPC; load its hero presentation independently of the player roster on updated clients.


## 2026-10-27 — Atomic hero milestone claims and wallets

Persist integer Gold/Platinum balances on Users, initialized to zero, and use a separate unique claim row for each player/hero/milestone. Apply rewards to all earned but unclaimed levels, so multi-level gains and retroactive grants share the same idempotent path. Commit claims and balances with progression in one SQLite transaction. A per-player match-result guard prevents duplicate delivery from replaying switched-hero XP into additional milestones. Client balances use existing PlayerUpdate.Currency; internal master/region PlayerData adds a wallet field, so server components must update together. Do not derive real balances from the former hardcoded test values.


## 2026-10-27 — Casual draft authority and class feasibility

Use the existing server lobby queue and timers for casual bans and snake picks, with an explicit phase number in every vote. Reserve the accepted roster before loading, count one final vote per authenticated member, and reject stale/late inputs. Keep class minimums per team for initial 4v4/5v5 rosters; do not infer size from the four-player catalogue card because automatic casual can be 5v5. Preserve vacancy capacity during backfill. Require a complete feasible lineup for both teams before accepting any hero or exposing a ban candidate; public pools use class-capacity allocation and restricted inventories use exact ownership search. Keep ranked/clan draft ordering unchanged.

Use an optional LobbyUpdate JSON snapshot at bit 9 and lobby request 20 for phase-scoped ban votes, with coordinated client/server publication. Reconnects use the normal full lobby snapshot rather than reconstructing client state. Author UI adapters under compatibility/runtime-session and install through tools/Install-CasualDraft.ps1 with staged copies and hashes. No production deployment is part of this implementation request.

## 2026-11-07 - Explicit Time Assault course key

Keep global_logic.time_trial.courses as the authoritative course catalogue. Add a distinct selected-map RPC with a start-result reply, leaving the old featured-course request wire format intact. Do not mutate global course ordering per player. Resolve restarts from the running instance map and validate the replacement before releasing that instance. Reuse existing per-map goals/rewards/leaderboards and fixed hero/device loadouts. Deploy server support before exposing the picker in a client release.

## Time Assault presentation readiness

Use negotiated capability 14 and zone message 114 to acknowledge a completed TA snapshot only after the client loader, local player, HUD and loading overlay are ready. A fresh random challenge belongs to each connection/load; matching it releases the existing prestart countdown. Do not use a fixed loading delay or allow the lobby timeout to bypass this TA guard. Old clients keep their existing handshake, and other modes retain their normal loading behavior.
## 2026-11-07 - Casual uses existing FreePick protocol

Normalize the casual card to FreePick at catalogue registration instead of bypassing selected draft timers. This keeps server selection permissions and the existing client UI in agreement, including reconnect/backfill. Only initialize casual draft state for a DraftPick card. Keep the dormant draft implementation/tests available, but do not activate it for production casual registration. Ranked remains unchanged. Deploy through a server release/restart after draining matches; no client update or persistent catalogue edit required.



## 2026-11-08 - Reputation is a separate match-settled public score

Use an independent transactional event ledger and startup policy epoch rather than changing skill ratings or historical matches. Publish only the score/status through an additive HTTP read endpoint, keeping the game protocol compatible with existing clients. Consecutive abandonment escalates without a calendar reset; qualifying clean completion restores one point and resets the streak. No automatic chat classification or reputation-based access restrictions in v1. The two-minute reconnect budget is shared with personal MMR's return allowance; socket expiry retains its actual disconnect classification.


## 2026-11-09 — Retired national crates retain compatibility identities

Retire country crates through the authoritative catalogue/ownership/loadout rules. Keep their legacy card and numeric block identities, but use stock crate visuals and levels, to preserve map/replay references. Preserve historical grants instead of deleting player history; they no longer convey ownership. Catalogue replication and incremental updates enforce retirement even if the source CDB still contains the old cards. No schema or gameplay-stat change; deployment deferred to the next update.


## 2026-11-09 — Retire hero skins without resetting loadouts

For explicitly retired hero skins, preserve historical identities and grants but exclude ownership, hero selection and shop offers. Resolve equipped skins through SafeSkin and repair only SkinKey at login; avoid the generic invalid-skin path deleting a complete loadout. This policy applies to the four requested hero skins, not their separate weapon cosmetics.


## 2026-11-15 - Tutorial terrain, map build timing and replay sharing

Practice Grounds uses the existing authenticated StartTutorial wire message and native tutorial map/mode, loading terrain through MapDatabase rather than using CardMap.Data as a complete map. Replay sharing resolves an unsigned 64-bit replay ID to already-public archived match metadata; gameplay admission still uses the existing replay spectate checks and retained replay lifetime. Reconcile stale idle state when returning to the menu only if the player is absent from authoritative queues. Apply approved 3/4/6-minute initial build timing to matchmade objective maps based on their loaded objective counts; preserve host-selected custom timing and later build rounds. These changes are prepared for a later combined deployment.


## 2026-11-18 — Keep Fast Restock descriptions in CDB and scope both gains to stations

Fast Restock uses the existing effect health_gain/ammo_gain tuning only for station-origin restoration. Exclude both entries from global aggregation; split mixed regeneration by origin and use effective contributions for attribution. Keep existing station identity rules (supply labels/minimap types and station medkits), the wire protocol and brick-income effect. Remove runtime description/translation mutation; correct known old wording once through an explicit CDB migration so subsequent administrator edits remain authoritative. Deployment and production CDB writes are separate from preparing and pushing this fix.


## 2026-11-18 — Experimental admin funny-mode authority

Use negotiated zone capability 15 (115 request, 116 state), independently authorize each request and recheck after world queuing. Keep hold placement, charge timing, max health and lifecycle on the server; use the player's existing CharacterController/force physics for flight and the issued throw impulse. Enable per living admin per match, never persist. Require upgraded targets and suppress extension packets to older clients. Rebuild ordinary health buffs on exit, preserving effect ownership. Do not register a substitute character model until the later model work. See docs/ADMIN_FUNNY_MODE.md.


## 2026-11-18 — Replicated admin nameplate presentation

Carry nameplate suppression as a server-owned marker in existing UnitUpdate.Buffs, including full observer snapshots, instead of adding a second high-rate observer mode stream or inferring the mode from its 2000 health cap. Reserve marker 60 for this purpose and ignore it in catalogue effect extraction. Updated clients disable only the native overhead name Text/title canvas, preserving health content and existing line-of-sight/team display rules; restore its prior enabled state on exit. Keep account identity and other UI data intact. Roll out with the corresponding client change; legacy clients can parse the marker but do not implement suppression.

## 2026-11-18 — Shared transformation storm epoch in observer snapshots

Keep the storm's activation time server-owned and publish the latest zone activation epoch on every active admin in existing unit buffs. Split Unix milliseconds into exact integer high/low float values (base 1048576; buff keys 61/62) to retain wire precision without a new observer protocol. Reject catalogue contributions to these fields. Refresh all active admins on a new activation, preserve the epoch on duplicate enable and retain it on surviving admins when another exits. Existing cleanup removes ownership. Clients aggregate living marked units and use the synchronized clock for late-join thunder seeking; presentation and reversible map weather remain client-owned. Updated clients are required, and deployment remains separate.

## 2026-11-18 — Entity attack authority and observer delivery

Keep admin entity attacks independent of CDB hero replacement: weapon slots use beam charge/release and secondary homing fireballs; dig retains gravity. Negotiate capability 16 for actions 7–10 in existing fixed-size message 115 and VFX-only messages 117/118. Validate role/capability/mode/liveness/aim on the world thread, derive charge/origins/target/endpoint and cooldown there, and use native health/block damage. Bounded server projectile lifetime, steering and swept collision prevent client hit claims. Existing mode cleanup removes attack ownership.

Deliver observer VFX through loaded GameInstance connection services, including spectators, each with its own negotiated capability. Use GameZone callbacks rather than the shared buffered ServiceZone (which has no connection capability flags). Keep original procedural client VFX/materials temporary and non-colliding. Preserve old gravity negotiation and ordinary weapon control when the server lacks capability 16. Deployment remains separate.

## 2026-11-18 — Source-owned secondary gravity set

Reserve native combat gear index 2 for hold-LMB attraction and hold-RMB/release repulsion, preserving first-weapon beam/fireballs and dig grab. Reconcile each admin's synthetic pull source together with native constant effects, preserving strongest-source/tie handoff and independent cleanup. Affect living players from both teams with LOS/range checks and native maneuvers; add movement force only, not health/terrain damage. Release conflicting targeted grabs. Validate slot, role, capability, mode and charge/cooldown on the world thread and clean up on source lifecycle.

Use capability 17 with fixed request actions 11–15 and observer VFX message 119, delivered through loaded connection services. Native movement keeps physics compatibility; matching upgraded admin flight must retain external pull/decaying knockback instead of discarding native movement forces. Deployment remains separate.

## 2026-11-18 — Replicated invisibility presentation state

Keep F invisibility as an immediate no-cooldown toggle owned by authorized living admin mode. Capability 18/action 16 reuses the fixed request body. Reserve buff key 63 for server-owned state in ordinary observer updates/full snapshots rather than adding an observer packet or modifying catalogue hero abilities. Reject catalogue injection, preserve state during recomputation and clear it with mode lifecycle. Preserve health, combat rules and hitboxes; hiding is implemented by matching observer clients. Updated rollout and deployment remain separate.


## 2026-11-22 — Isolate original course trap cards for Time Assault

Use map-authored trap unit keys from the preserved original installed CDB, rather than treating the anomalous player level-1 Acid Trap card as the course baseline. Embed the 29-card original gameplay dependency closure under distinct keys, with checked 16-bit block IDs, and replicate it through the existing catalogue. Select historical map units and remap ground traps only in TimeTrial runtime copies before snapshots; preserve normal gameplay cards, source map files and player loadouts. This keeps linked clouds/burning/poison/root effects faithful without changing other modes or requiring a client build/live CDB edit. Existing records remain untouched. See TIME_ASSAULT_ORIGINAL_TRAPS.md for provenance, reproduction and rollout limits.


## 2026-11-22 — Complete original Caltrops presentation dependency

Live readback after the authorized Time Assault deployment exposed a missing material_caltrops target. Added it under an isolated material_time_assault_original_caltrops key, bringing the baseline to 30 cards; changed the generator and added resolution checks for every original card reference. All 37,537 current-live-catalogue trap assertions pass, covering the same 784 units and 9,789 ground cells. Baseline data/runtime stats otherwise unchanged. Deploy this corrective revision before considering the rollout complete.


## 2026-11-22 — Authoritative Time Assault prestart movement lock

Restarted zones begin in TutorialInit, which the client Waiting/Build predicate omitted, and the server accepted early movement. Consume runner movement during TutorialInit/Waiting/Build without moving or running terrain/trap/pickup callbacks; retain fresh look/heartbeat data and clear locomotion. Correct old clients through immediate native teleport, and reject countdown-sampled packets arriving after GO. Other modes/non-player units and fresh movement at GO keep their existing path. Client separately freezes before movement layers and clears queued motion. No map/timer/CDB/record edits.

Production-before reproduction fails at TutorialInit; fixed server passes 6,900 checks across six maps and three run lifecycles, fixed-spawn 504 checks, warning-as-error Release build. Paired client Unity guard passes 4,326 checks. No deployment or live keyboard acceptance; client smoothing requires a later signed build. Details: docs/TIME_ASSAULT_RESTART_MOVEMENT.md.


## 2026-11-22 — Snapshot Tony F and reset recovery under authoritative threats

Use existing native one-shot effect bunches and timed self wrappers to select Tony F recipients on server acceptance, rather than maintaining a caster aura or accepting later client hit geometry. Gate the immediate path on the two exact abilities carrying the new CDB marker to support server-first rollout. Carry the recipient deduplication set through server-only impact clones, preserving wire compatibility; receipt points belong to each affected player once per cast. Keep original turret bonus as an activation snapshot. No new global_logic document edit is needed.

Treat the server's existing VisionMark and living enemy players within the owner's specified 8m as continuous Skill I combat conditions, resetting the same quiet deadline used for shots/damage. Use the existing relationship helper and inclusive midpoint distance without LOS; keep the current rate and delay tunable in class_perk. Data and runtime changes form one rollout, with server first and guarded six-card CDB application after backups. See docs/TONY_SECOND_WIND.md.


## 2026-11-22 — Prepare six-hero catalogue balance tuning

Use existing CDB fields for the requested Goo score10→8, Doc weapon allied heals−20%, Trondson primary falloff15→12m/70→50% endpoint, Sweet Science shield20→37.5% (the existing next teal tier), Vander ally beam2→2.25 HP per pulse and Nigel rifle falloff80→70m/70→55% endpoint. Cover perk variants, English stats and reticle full-damage previews. Preserve other heal sources, close damage/critical multipliers, ranges, Time Assault original traps and other scoring rules. Nine reviewed documents/26 field paths; no engine-default changes needed.

Added offline planner, revision/field-guarded dry-run-by-default apply tool with private backup/readback/rollback and concurrent-edit protection, checked-in field patch and fixture. 1,181 actual-engine assertions plus six fake-Couch deployment scenarios pass; Release build is clean. Live read-only preflight: nine pending, zero applied; no live writes. Source snapshot hash and full numbers/reproduction/rollout: docs/SIX_HERO_BALANCE_20261122.md. User did not request deployment in this turn; all values remain prepared, not live. The separately pending Time Assault restart fix is not merged or deployed here.


## 2026-11-22 — Rolling Wild trades shielding for movement

Replace the moving-F perk's extra cooldown with 37.5% shielding, matching prepared Sweet Science's existing teal tier. Restore base F cooldown of 45s, preserve normal Roly F shielding at 40%, and keep the movement benefit/duration/other immunities. Use only the existing perk effect, ability charge cooldown and English descriptions. Extend the pending catalogue patch without changing its earlier nine rows; the twelve-card plan supersedes the previous nine-card plan. No runtime or protocol change required. Deployment remains separate.


## 2026-11-22 — Shared teal shield tier is 30%

Use 30% for the common teal tier, replacing the earlier prepared 37.5% for both Sweet Science and Rolling Wild and the shared client colour stop. Audit actual shield effect cards rather than globally scaling all damage/shields; keep the other tiers, strongest-source selection and cap. All consumers of ShieldStrength.ColorFor share the new colour meaning. Apply gameplay via the existing guarded catalogue plan; deliver the matching colour stop in a later client release. Deployment remains separate.


## 2026-11-22 — Deploy binary and catalogue in one maintenance window

Apply the integrated Time Assault/Tony/Second Wind runtime and reviewed 19-document catalogue while the game service is stopped. Stage from the existing release and require identical dependency libraries. Back up player data and document preimages privately; order new wrappers before references, retain source revision guards, and roll back document changes before reverting the binary. Verify live runtime cards as well as stored CDB content. This avoids exposing half-applied catalogue data or starting new zones with old scoring settings. Client visual changes remain for a separate client release.


## 2026-11-22 — Shared activation buffs with original-recipient refresh

Use native one-shot target bunches and timed self wrappers for Kira/Trondson, selecting on accepted F with exact ability/marker whitelist. Retain self-only bonuses and source attribution; new untimed Kira reload value lets the wrapper own10s/6s expiry without changing legacy effect data. Record successful original applications in server-only cloned impact metadata. Thrills kills refresh that set, gated by cast lifetime and original unit ids; never rescan nearby players. Keep other aura systems and wire format unchanged. Roll out binary and18 CDB documents together after separate authorization.


## 2026-11-23 - Exclude the previous BB anchor before landing offsets

Remember the last selected Blockbuster map drop-point coordinate per zone, shared across BB tiers and retained through resource drops. Exclude that coordinate uniformly from distinct valid BB anchors when another location exists; apply the existing randomized landing offset after selection. Use coordinates so duplicate markers cannot bypass exclusion. Preserve single-location-map drops rather than stall the supply schedule, and retain history when no marker is available. No wire format, CDB or client change; deployment remains separate.
