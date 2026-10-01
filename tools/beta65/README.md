# Protocol 65 local menu compatibility

This standalone service supports the March 27, 2015 beta client. It does not
modify the current server, connect to its database, or implement production matches. It binds
only `127.0.0.1:27065` (game protocol) and `127.0.0.1:27066` (local menu feed).
Do not expose it as a public server.

Implemented: version 65/0, debug master login, one-use expiring region handoff,
region login, externally supplied catalogue/player/menu packets, profile replies,
empty leaderboards, and clock sync. The fixed `BetaLocal` / `local-diagnostic-only`
profile is a local preview identity, not a real account. Credentials and handoff
tokens are not written to logs. No Steam or Jagex authentication is implemented.

The separate private `bnl-beta-recovery` project generates `catalogue.bin`,
`player.bin`, `profile.bin`, `server-update.bin` and `scene.bin` using its verified
historical client serializers. These files contain a synthetic, minimal menu
catalogue and profile, not a recovered historical gameplay database. Play and
ranked are disabled. No recovered proprietary client files belong in this repo.

```powershell
python tools/beta65/server.py --packets C:/github/bnl-beta-recovery/runtime/menu-packets --events C:/github/bnl-beta-recovery/artifacts/menu-server.jsonl
python -m unittest discover -s tools/beta65 -v
```

The recovery project's `Launch Beta.cmd` generates the packets, starts this service
if necessary, and opens the copied client. `/health` reports the service identity.

Verified with the original Unity 5.0.0f4 player: master and region login accepted,
catalogue deserialized, and `ServiceScene.EnterScene` received for MainMenu. The
original main-menu scene renders. Map loading, characters, inventory, matchmaking
and matches are outside this milestone. Unsupported requests are logged by service
and function only. Do not claim all menu tabs are functional.


Experimental terrain / hero preview (`--terrain-test`): the private generator can
supply `terrain-scene.bin`, `zone-init.bin`, and `zone-start.bin`. The service
issues a separate one-use instance token, initializes the map, and waits for
ZoneReady before starting the preview. Optional `hero-create.bin`, `hero-state.bin`
and matched `key-*.bin` / `equip-*.bin` files spawn and equip one local hero.
Only supplied equipment keys are accepted. Repeated ZoneReady is rejected.
`/health` identifies menu versus terrain mode and whether hero packets are loaded.

Verified in the original player: tutorial terrain loads, Sarge spawns with 160
health, and his recovered first-person M60 renders. The input catalogue/map are
adapted modern data, not an authenticated 2015 gameplay database. Movement packets
are observed (finite coordinates, local unit ID); there is no authoritative physics.
The user confirmed the initial interactive preview works. The subsequent practice
simulation adds M60/direct-hit damage against a stationary opposing Sarge, server
ammo counts, timed magazine reloads, target death/respawn, destructible voxel damage,
mining rewards, timed brick placement with cost, and player respawn after falling
below the map. These are deliberately limited local practice mechanics.

Hits must correspond to a recent accepted shot, satisfy range/trajectory checks,
and pass a voxel obstruction test. Voxel checking approximates each solid voxel
as a cube, so sloped/partial geometry is not exact. Movement remains client predicted;
this is not an authoritative multiplayer simulator. Critical hits, regular falling/drowning damage, opponent AI, objectives, results,
persistence and multiplayer remain unimplemented. This is not match ready.
The service remains loopback-only and does not change or deploy the live server.


Validation: `python -m unittest discover -s tools/beta65 -v` covers ammunition,
cooldowns, reload timing, replay rejection, damage/respawn, voxel obstruction,
mining, building cost/occupancy/timing, and login. `smoke_player.py` is an opt-in
scripted driver for a connected original player. It injects simulated cast/hit and
build actions; it does NOT test mouse/keyboard input. Run it with the same arguments
as server.py in an isolated test, then restore server.py for interactive use.
The launcher never invokes this scripted driver. `/health` includes a packet
revision so a newly generated catalogue does not silently reuse old loaded data.


Sarge loadout expansion: slots 1-6 contain crates, sandbags, landmines, respawn
point, radar and Sarge bomb, using supplied level-one definitions. Construction
uses each definition's cost/delay, with per-live-device cost increases. Mines trigger
near the opposing practice target; bombs use their supplied fuse; radar marks the
nearby target; the latest surviving spawn point is used after player death. Local
practice starts with 5,000 resources so the full set can be exercised.

F equips the frag ability with three charges and the supplied recharge interval.
Ability RPCs validate the assigned key, origin, charge count and shot ID. Rocket
and grenade hit reports resolve nested explosion effects against units and voxels,
with recovered impact references supplied by the private catalogue. Grenade hits
allow bounded travel after bounces; they are not constrained to a straight ray.
Self-knockback uses the old ManeuverKnockback message. This remains a local,
client-predicted approximation: enemy knockback, precise curved/slope occlusion,
wall attachment orientation and production server-authoritative physics remain
outside the implementation. `smoke_loadout.py` explicitly exercises all six slots,
projectile rendering, explosions, recharge and deployed respawn in the old player.
It is a scripted test, never launched by the normal user launcher.

The private packet generator now supplies `lobby.json` and `lobby-scene.bin`.
Without `--terrain-test`, these enable Play -> Local Practice -> Go or Custom ->
Create Game -> Start Game, followed by the recovered hero/block/skin lobby.
The recovered roster now includes Sarge, Cogwheel, Nigel, O.P. Juan, Eliza and
Tony, with 21 skins verified against beta bundle paths and 23 block/device choices.
Hero-specific default loadouts, skin selection and add/remove/swap/recommendation
requests update the lobby. Selected model, FPS skin, weapons, health and loadout
carry into practice and respawn.
Six unique slots are required before Ready; their order carries into practice
and survives respawn. Selections live for the current region session only.

Lobby and zone reuse the same mediator connection. Region EnterScene after Ready
triggers ZoneInit on that connection; issuing a second instance token here leaves
this client stuck at 50%. Quit Match returns to the menu and stops simulation.
Each region session has its own room and loadout; initial instance tokens remain
expiring and one-use. This does not implement multiplayer: the browser returns an
empty list, create makes a local solo room, remote join/spectate are rejected,
and team/settings changes return the supported fixed values with an explanation.
Room passwords remain in memory only and are not logged or persisted.

41 automated tests cover login, gameplay and the lobby state machine, including
a socket-level menu/lobby/zone/exit handoff. Original-player mouse tests and
serializer fixture comparisons are recorded in the separate recovery project.

The lobby has a two-minute selection countdown and auto-starts at expiry; an
incomplete loadout then falls back to that hero's defaults. Block In starts early.
ServiceTime.SetOrigin and Sync use shared process-relative monotonic milliseconds,
also used for ability/device deadlines. Idle sockets are polled before framed reads,
so remaining in menus no longer triggers the old 600-second receive timeout.
Partial frames still time out. Pool-only ammo and multi-pellet casts are supported.

This expansion restores selection and spawning, not full hero mechanics. Non-Sarge
abilities, channel/melee tools, special-device behaviors and historical balance are
incomplete. All 23 devices are currently exposed to all heroes in this solo sandbox;
hero-specific restrictions are not yet enforced. No multiplayer or deployment.

Direct hero preview also binds the instance to its region room. ExitMatch stops
practice and returns to the menu; a later Play request enters the normal lobby.
The generator marks local zones as custom so the original Escape menu exposes
Quit Match. Preview uses the full lobby packet set so Play remains enabled.
Health reports the packet revision loaded at startup, not a regenerated disk
manifest. Regression suite: 42 tests including direct preview exit/reentry.

Additional solo maps: The Bridge, Stronghold and Stone Temple. The private generator
supplies maps.json and scene/zone-init/terrain packets for each map. Custom-room
map arrows send the original map-only settings update; only registered keys are
accepted, and map changes are restricted to the room before entering the lobby.
Scene, lobby label, terrain, player/target placement, kill plane and respawn all
use the selected map. Shared packet templates are never modified by a session.

These are copied later map files adapted for protocol 65, not verified 2015 map
revisions. Objective units, shield/base logic, pickups, map scripts and multiplayer
are not restored by this change. Rules remain solo practice. 45 tests pass,
including map validation, immutable snapshots and map-specific void respawn.


Hero mechanics recovery (2026-09-30): the protocol-65 simulator now handles
optional self/hitscan ability fields, charge consumption/recharge, timed buffs,
aura/interval effects, melee hits, safe teleports, projectile throws, and caulk
channels with ammo/range/target checks. Nigel scans expire; Ninja F requires a
nest; Eliza heals and creates damaging gas; Tony grants a timed build-speed buff.
Cogwheel deployed mortars render a server-driven approximate arc and apply the
catalogue volley effect. Damaged/destroyed devices can spawn gas or loot; pickups
expire and apply their effects. Enemy glue/bear traps affect players, while own
traps ignore them. Block orientation and team metadata are preserved. Impact
packets now include hit units to drive the original hitmarker/audio callbacks.
Map objective units spawn once from the supplied placement metadata.

The private generator must supply definitions.json, buff-ids.json and unit-*.bin
alongside the existing packets. These definitions and visual substitutions are
adapted from later data, not an authenticated 2015 CDB. This remains local solo
practice: full objective shield progression/victory, multiplayer, AI opponents,
and the complete wider catalogue are not implemented. No production deployment.
53 unit tests pass; the private recovery project additionally runs 17 checks
against the generated catalogue and original-player injected rendering tests.

## Local/LAN menu services (2026-10-01)

`--state <private.sqlite>` enables persistent local profiles (first successful
login registers a name), player search, friend requests/accept/remove, online
presence and expiring squad invitations. Passwords use salted scrypt. State and
logs must stay outside Git. These are separate accounts from Reborn/Steam.

The original protocol sends debug-login credentials without TLS. This runner is
for a trusted LAN only; do not expose it to the public Internet or reuse an
online-account password. Host with `--bind 0.0.0.0 --advertise-host <LAN-IP>` plus
`--state`; redirects advertise that IP. Default binding remains loopback. HTTP
health/feed remains loopback-only. No firewall or production deployment changes
are performed automatically.

Friendly queue pairs equal-sized parties, keeps squads together, requires both
sides to confirm within 30 seconds and removes disconnected/cancelled entries.
Custom rooms are shared in memory: listing, password joins, host map selection,
team switching, kicks and host succession. The selection lobby shares heroes,
skins and six-block loadouts. Team selection carries into match spawns,
unit ownership, objective protection, devices and chat.

Shared LAN matches now start after every player blocks in and loads. One locked
world owns terrain, deployables, effects and ordered objectives; player unit IDs
are distinct from persistent profile IDs. Movement/casts/builds/damage/respawns
are replicated, with per-player RPC/spawn menus and team/all chat. Build phase
protects enemy players and objectives. An empty team aborts without rewards;
disconnected players cannot act, and remaining teammates retain the world.

Destroying the final enemy base completes the match. The server awards 500 XP
for Friendly completion plus 250 for winners exactly once; 1000 XP advances a
player level. Custom/practice/aborted games award none. This is a provisional
LAN rule, not a recovered historical formula. Native results and live profiles
receive updated progress. Results currently reuse player progress for the
beta hero-XP display; independent hero progression/history is not implemented.
Ranked, backfill and reconnect are not implemented. Movement is still client
predicted, without a public-server anti-cheat/lag-compensation implementation.

The private packet generator must now include beta_lan_match (ShieldRush2),
beta_lan_participant, native third-person gear paths, and the copied-runtime
results hook. The original executable/asset sources must remain unchanged.

Run all protocol, gameplay and LAN service tests:

```powershell
python -m unittest discover -s tools/beta65 -p test_*.py
```

Validation on 2026-10-01: 101 protocol/gameplay/LAN tests pass. The private
recovery harness verifies framed socket login -> queue -> confirm -> Block In
-> shared spawn/movement -> ordered objective completion -> results/XP ->
exit/requeue. Objective damage is injected in that integration test. An original
beta client with one scripted peer visually renders remote Sarge, Mountain
Express lava, and native Victory results with +750 XP. This is not a completed
two-physical-PC human playtest. No production deployment was performed.

Supply Drops and Block Buster (2026-10-01)
----------------------------------------
The map importer now passes original drop markers. SupplySystems starts the
copied Shield Rush supply sequence after construction: five-minute intervals,
resources, resources, Lite, resources, Classic, resources, Extreme, then Uber
repeats. Positions are selected once by the shared authority. Native SupplyInfo
provides the warning countdown; units use their recovered descent parameters.
A resource pickup awards 500 bricks to its team, including dead teammates, once.
Closed Block Busters open on destruction or their 150-second lifetime; unclaimed
pickups expire after 29 seconds. Recovered effects last 90 seconds. World and
objective damage bonuses are 50/100/150/250 percent; player damage is unchanged.
Classic/Extreme/Uber also grant a shield with damage reduction capped at 50%.
Extreme/Uber team-zone effects resume after respawn until the original deadline.
A new Block Buster replaces its team's previous tier, rather than stacking.
All tiers use the collecting player's team, never the neutral crate's owner.
Effect icons, timers and shield visuals use protocol-65 UnitUpdate.Effects.
The private client adapter preserves server-only objective damage and zone reward
metadata while emitting compatible old client cards and recovered prefab names.
The provisional LAN timeout is now 60 minutes (formerly 30) so the late supply
sequence is reachable. A missing marker skips that event without stopping later
supplies. Ended/aborted worlds stop scheduling. These are compatible later
catalogue rules, not an authenticated December balance reconstruction.
Validation: 116 server tests; private recovery fixtures verify both new packet
types against the original assembly and all five actual reward tiers. Native
client checks use an accelerated diagnostic sequence and injected crate damage;
production timings and profile database are untouched. No production deployment.

Grounding follow-up: settled falling units now retain their collision support
cell and emit no repeated downward/snap-back updates while it remains solid.
Removing/replacing support with fall-passable material re-enables gravity. The
private asset adapter measures pickup colliders too: both Block Buster models
require a 0.5-unit origin-to-bottom offset. 119 server tests pass, including
stationary small-timestep landing and falling again after support removal.

Friendly map voting now uses the native two-map lobby panel. After queue acceptance,
two distinct maps are sampled from the common pool (including Workshop recreations).
Each player has one final vote; totals are broadcast to all lobby members. Voting
ends after 30 seconds or when everyone votes. Highest tally wins; ties and zero-vote
ballots use a random choice among the tied offered maps. One-map pools skip voting.
Hero, skin and block selection remain available during voting; Block In is rejected
until the ballot resolves. Then all players receive the same map and a fresh shared
120-second selection timer. Custom rooms retain host map selection. Leaving removes
the player's vote; a Friendly lobby with an empty team returns to the menu.
Validation: 128 beta65 tests, including duplicate/invalid/late votes, abstentions,
ties, shared timers, early readiness, disconnects and unchanged custom selection.
Native beta UI verified with one client vote and a scripted peer vote; not a two-human
LAN test. Client compatibility patch v6 grays out Block In while two maps are offered.
The separate beta client supplies cached local Workshop previews and recovered map artwork.
Winter variants reuse their base-map preview; no thumbnail HTTP access is needed in game.
This is the local/LAN beta service; no production deployment performed.

Beta loadouts now advertise and validate per-hero available_devices from the Reborn class lists, intersected with recovered beta content. Cosmetic variants share a base-device family; duplicate families cannot fill multiple slots. The native beta signature slot is slot 1, restricted to that hero and its recovered variants. Removal, swapping and cross-class packet writes are rejected; timer expiry repairs invalid loadouts. Cogwheel radar cosmetics retain detection behavior. Validation: 135 beta65 tests, including class filtering, variant replacement, duplicate-family rejection, hero switching, corrupted-ready rejection and radar-skin behavior.

When the final player Blocks In, the lobby publishes the native Start timer with a shared five-second deadline. World creation and scene handoff occur only after that deadline, once per group. Solo lobbies also wait five seconds; selection timeout still gives the full countdown. Duplicate Ready messages cannot extend or skip it. A departure cancels the countdown and resets preparation; Friendly lobbies with an empty opposing team return to the menu. Validation: 142 tests passed, including a real socket handoff delayed at least five seconds, synchronized timer packets, duplicate messages, timeout, departure and invalid-loadout cancellation.

Ninja wall climbing: practice snapshots retain the selected hero ID. Buff replication includes the WallClimb capability from that hero's passive effect card (protocol-65 ID 19, speed 6 in the copied catalogue). It remains innate across timed effect expiry, purge and respawn, and shared-world replication resolves each recipient's own hero. No client movement replacement. Validation: 147 tests plus real generated Ninja packet/buff integration; physical wall-climb input has not been manually verified.

Respawning refills every weapon magazine and reserve pool to its configured spawn capacity, including reserve-only weapons, and cancels any pending reload before sending the ammo update. Shared matches refill only the respawning player. Validation: 149 tests, including solo deadline/refill/reload and LAN recipient isolation/replication.
