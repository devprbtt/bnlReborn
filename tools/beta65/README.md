# Protocol 65 local menu compatibility

This standalone service supports the March 27, 2015 beta client. It does not
modify the current server, connect to its database, or implement matches. It binds
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
Gear switching has protocol tests; manual input validation is pending. Combat,
damage, ammunition consumption/reload, building/destruction, respawning, objectives,
match completion and multiplayer are NOT implemented. This is not match ready.
The service remains loopback-only and does not change or deploy the live server.
