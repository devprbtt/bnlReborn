# Map pool preferences (prepared 2026-11-24)

Automatic Casual and Ranked matches snapshot the popped roster's individual map
choices before making their map ballot. A strict yes majority includes a map in
the random draw; a strict no majority excludes it. The server flips tied maps.
If fewer than three candidates remain, it fills from remaining ties, then rejected
maps, preserving the existing weighted draw and mode eligibility. The result is
three distinct maps whenever the eligible rotation contains at least three.

Missing choices abstain. A player with no checked **eligible** maps asks the server
to choose and abstains entirely. Older clients therefore retain the original
draw. Preferences never split queues, affect team balancing, or bypass the 5v5
Casual exclusion of one-cube maps. Custom/clan map selection stays unchanged.
Backfills and later edits cannot change the snapshot used for an existing match.

The client persists per-account unchecked overrides locally, defaults new maps to
checked, and publishes complete Casual/Ranked choices before queue entry. The
server keeps choices in process memory; reconnecting clients resend. Casual 4v4
and 5v5 use one preference set, filtered against that match's eligible pool.

## Wire

Service 11 command 36 is one-way: version byte 1, mode byte (0 Casual, 1 Ranked),
little-endian ushort count (maximum 512), followed by uint key hash + Boolean byte
for each map. Keys must be nonzero and distinct and values 0/1. Identity comes
only from the authenticated sender, and unknown map hashes are discarded. The
codec consumes exactly its declared records because the existing transport can
supply several coalesced frames in one reader. Public play JSON advertises
`map_preferences_version: 1`; clients send no new command without that capability.
No preferences or new personal information appear in the public endpoint.

## Prepared rotation change

`catalogue-patches/map-pool-expansion-20261124.json` expands Friendly from 18 to 26:

| Cubes per team | Maps |
| --- | --- |
| 1 | Aztec Temple, Citadel, Insandity, Parapet |
| 2 | Cog's Grog, Fort Argent, Front Lines, Gate House |

The eight live installed payload hashes match this repository; both teams have
spawns, matching objective counts and placements within map bounds. They already
have catalogue cards. Ranked's configured rotation is unchanged. Existing map
weights are retained. No live catalogue write has been performed.

On the game host, `python3 tools/apply_map_pool_expansion.py <patch>` performs a
read-only preflight. After deployment authorization, add `--apply` to make the
guarded write. It requires the reviewed document revision and prior Friendly
list, makes a private backup, preserves all unrelated fields, verifies readback,
and restores on failure only if no concurrent author has changed the document.
If the revision changes, re-audit and review a refreshed patch before applying.
Publish the server update first, then the signed client with matching release
notes. This branch and patch are prepared; neither is deployed.

## Validation

`dotnet run --project tests/BNLReloadedServer.MapPreferencesFixture -c Release`
checks codec bounds, coalesced framing followed by queue entry, legacy draws,
majorities, ties, minimum-three recovery, mode/account isolation, unknown maps,
actual-roster snapshots, and immutability after edits/backfill.

Also run MapVoteFixture (with a CDB JSON argument), CasualMapPoolFixture,
BackfillReentryFixture, AutomaticPublicQueueFixture and the warnings-as-errors
Release build. Client source and native UI validation live in the client repo.
