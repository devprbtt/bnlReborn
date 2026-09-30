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
