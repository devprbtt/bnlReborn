# Tony activation buff and Second Wind threat detection — 2026-11-22

Prepared and tested; not deployed. Based on production `54a8a3b287de8beb6b00ed566b079067261dca72`. This branch does not include the separate pending Time Assault movement or six-hero tuning branches.

## Player-facing notes

- Tony's F now grants its buff to players in range when activated. The buff lasts 20 seconds and stays with you when you leave; entering range afterwards does not grant it.
- Each player receiving Tony's building buff earns 5 base points once per activation, including Tony. Ordinary objective score modifiers still apply.
- Second Wind pauses while revealed by radar or spotting, or while a living enemy player is within 8m. Its full 5-second out-of-combat delay must pass after those threats end.

## Implementation

The reviewed six-card patch is `catalogue/tony-second-wind-20261122/tony-second-wind.patch.json`. `tools/prepare_tony_second_wind.py` regenerates it and a full test catalogue from a captured public CDB:

```powershell
python tools/prepare_tony_second_wind.py <public-catalogue.json> <output-folder>
dotnet run --project tests/BNLReloadedServer.TonyCastBuffFixture -c Release -- <output-folder>/candidate-catalogue.json
dotnet run --project tests/BNLReloadedServer.SkillRecoveryFixture -c Release
```

Both normal and Wider Tony F use native one-shot friendly-target bunches and two new native timed self-effect wrappers instead of a carried aura. Preserve build bonus, purge, 20-second duration, normal 5m / Wider 11m range and 60 / 67-second cooldowns. Nearby friendly Lil' Buddy turrets receive their existing damage bonus as a 20-second activation snapshot as well. Original aura cards remain available for unrelated references.

The server selects recipients immediately when accepting F, using its authoritative position, rather than registering a later client hit. Only the two exact Tony ability keys with the new marker use this route. Delayed/repeated client hit reports cannot select new recipients or duplicate score. The source impact carries a server-only recipient set shared through clones, omitted from the native wire record. Award receipt only after native targeting/immunity checks succeed; dead/inactive/dropped/enemy recipients cannot earn it. New `global_logic.action_scores.tony_build_buff_received` defaults to 5 if omitted. The existing disabled building-assist score remains disabled. No global_logic card change is required, so the pending Roly Goo scoring patch does not conflict.

Second Wind checks the existing authoritative `VisionMark` buff, used by radar's `effect_device_radar_spot` and spotting abilities. It also checks living, active, connected enemy players by midpoint distance, inclusive at 8m, using the existing combat relationship helper. Walls do not exempt nearby enemies; allies, devices, dead or dropped players do not block recovery. Threat checks continuously reset the existing quiet-period deadline, preventing stored healing during a reveal or proximity threat. Existing attacks, enemy/fall damage, death, channel and respawn rules remain in force. Healing rate stays 10% maximum health per eligible second.

`perk_class_skill_1.class_perk.enemy_proximity_radius` defaults to 8 and accepts finite 0..100 (0 disables proximity only). Descriptions resolve `{enemy_proximity_radius}` using the existing native perk wire record. No protocol/client binary change is required. Updated English hero/perk descriptions travel in the CDB.

## Validation

- Tony full-world, actual-catalogue fixture: 34 checks across normal/Wider acceptance, spoofed client origin, immediate selection, team/death filtering, native build/turret buffs, movement, expiration, score ownership, ticks, delayed/repeated hits and rejected/new casts.
- Second Wind actual-zone fixture: 68 checks, including inclusive 8m boundary, full delay after range/reveal ends, interruption during active recovery, native reveal effect application/expiry, CDB radius tuning/invalid values and existing damage/respawn/healing behavior.
- Existing action-score fixture: 308 checks; class-perk catalogue/wire: 31; ability cooldown: 21.
- Release build with warnings as errors: zero warnings/errors.
- Read-only production preflight: all four existing card contents/revisions match the captured baseline; both new effect IDs absent. No CDB writes or restart performed.

## Deployment and rollback

Deployment remains separate. Commit/push this server branch before rollout. Deploy the server binary first, then apply the six reviewed CDB documents with revision/content preconditions. Back up all existing documents and record missing new IDs privately on the host. Create the two new effect wrappers before updating the two abilities; preserve live revisions and unrelated metadata when replacing the four existing documents. Re-read every saved card and reconnect a client for the new catalogue. Abort and regenerate/review the plan if any expected revision or before-content changed. Public client/feed publication is not part of this change.

The server can start against the old catalogue: only new-marker Tony abilities use immediate selection; Second Wind's default 8m/reveal rule is active with the new server. After applying the CDB, validate both F variants with two players, score breakdown, turret expiration and Second Wind against a real radar. Automated world fixtures do not constitute a live client acceptance test.

Rollback the four existing cards to backed-up content, guarded against later concurrent edits; restore old ability references before deleting new effect cards. Restore the previous server release if needed. Do not deploy the old server while the new Tony abilities are still active, because receipt scoring and immediate server selection require this branch.

Source/provenance and logs are in the client workspace `reports/tony-second-wind/`; raw public inputs and untouched baseline copies are under `extracted/work/tony-second-wind-20261122/`. Source roots were not changed.
