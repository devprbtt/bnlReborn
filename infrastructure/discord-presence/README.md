# Reborn Discord player-count bot

One service, `bnl-discordbot.service`, displays the local game server's
`player_count` in Discord activity. Application/bot ID `1555383788865982544`,
community guild `283986382660173824`. The application also supplies the game's
Rich Presence name/assets; do not replace those when maintaining the bot.

The Chinese server integration is retired. There is no second service or
remote status endpoint. The only runtime status URL is
`http://127.0.0.1:8080/api/public/status`.

## Source and deployment

The Rust source and lockfile are the exact September 8 reconnect repair,
copied from the extracted working copy with SHA-256 provenance in
`source-provenance.json`. Recovered originals were not edited. Its historical
fallback URL is not used: the startup wrapper requires the local status URL.
READY/RESUMED replace the poll task/context; unchanged counts refresh every
minute, changed counts at the next ten-second poll.

`run-bot.py` checks bot identity and community membership before execing the
Rust binary. Missing/wrong/revoked credentials or missing guild membership exit
78, which systemd does not restart. Network failures and rate limits retry after
30 seconds. The bot requires no administrator permissions or privileged intents.

`deploy.py` validates the new identity, verifies the previous binary hash,
retains the previous main-service configuration under root-only backups,
installs a new immutable release, and removes the retired service and its token.
It reuses the previously built/tested Rust binary rather than rebuilding it on
the live game server. Run from an uploaded copy with `sudo python3 deploy.py`.
First transfer the bot token over SSH stdin to root-only
`/run/bnl-discord-replacement-token`. Never place tokens in command arguments,
source, logs, build artifacts or Git. The temporary token is removed on success.
Runtime credentials live in `/etc/bnl/discordbot/main.env`, root:bnldiscord 0640.

Validate with `python3 -m unittest discover -s infrastructure/discord-presence
-p 'test_*.py'`. Rust rebuilds use the original pinned image
`rust@sha256:6ae102bdbf528294bc79ad6e1fae682f6f7c2a6e6621506ba959f9685b308a55`,
`cargo test --locked --release -j 2`, `cargo build --locked --release -j 2`.

Check `journalctl -u bnl-discordbot`, `systemctl show bnl-discordbot -p NRestarts`,
the local status JSON and visible activity in Discord. `presence_queued=true`
only means the gateway command was queued, not acknowledged by Discord.
`test-gateway-reconnect.py` intentionally drops only this bot's established
HTTPS sockets; use it during explicit maintenance, not as a background monitor.

Rollback: stop the bot, restore the prior main unit/env from the protected
backup, restore `current` to the recorded prior release, reload systemd and
start the main bot. Old revoked credentials cannot restore Discord access;
preserve the newly working credential. Do not restore the retired China unit.
