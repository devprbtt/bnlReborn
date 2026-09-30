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
this is not an authoritative multiplayer simulator. Critical hits, rocket splash,
grenades, regular falling/drowning damage, opponent AI, objectives, results,
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
