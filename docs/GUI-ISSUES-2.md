# GUI ISSUES 2.0 server support — 2026-11-15

Prepared on fix/discord-gui-issues-2; not deployed.

- /api/matches accepts player_id to page through global Matches/MatchPlayers history rather than the ten legacy profile summaries. Limit remains clamped to 1–100, before remains the existing millisecond cursor. Queries bind player ID, cursor and limit; another player’s match is never returned unless the selected player participated.
- Public queue projection adds nullable mmr (display points) and has_mmr, preserving real zero versus an unknown rating. Ranked queue/backfill behavior is unchanged.
- /api/public/home?presence=id,id returns only the requested IDs, current activity and last_online in milliseconds. One to fifty nonzero IDs per request, no-store; normal Home snapshots remain unchanged. No Steam IDs, names, addresses or sessions are exposed by this projection.

Validation: warnings-as-errors Release build (zero warnings/errors), MatchHistoryFixture 22 checks (including >10 archive records, participant isolation, pagination and zero/missing queue MMR), PublicQueueRulesFixture passes. Client changes and Discord replies are tracked in the private client repository. No production data or services changed.

Player-facing text for the eventual combined release: “Browse older matches from the shared match archive, see player ratings in the queue, and see when offline friends last played.” Publish through all four client patch-note surfaces with the release.
