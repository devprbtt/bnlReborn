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
