# Leaderboard trophy artwork

17 transparent 512 x 512 PNGs, packaged into the server build/publish output. Source artwork and generation provenance live in the private Unity upgrade repository under `developer-inputs/trophies/leaderboard-stats-v2`; delivery export hashes are recorded in its `game/manifest.json`. PNGs use Git LFS.

`LeaderboardTrophies.InstallArt` validates the whole package before installing catalogue definitions and images. It changes image versions only when bytes change. The master reconciles ownership on startup, after completed-match archives and on the existing one-minute season tick. It reads all 16 all-time stat rankings from one archive aggregation, including tied leaders beyond the public top-100 limit. League follows the current season's native rank, not a new all-time MMR calculation. Empty boards award nothing; existing eligibility/minimum matches remain unchanged.

These are transferable holdings, not historical awards. Losing the lead revokes the holding and removes its showcase pin. Regaining it creates a new grant and notification. Other trophy IDs are not touched. Retiring a definition suppresses its automated award. Players choose their existing five showcase slots; nothing is auto-pinned.

Capability 5 clients receive service-16 message 10 after login: player ID, byte count, then Int64 grant ID/string trophy ID/string name per entry. Request 23 acknowledges explicit grant IDs on the authenticated connection. Only current, unacknowledged leaderboard holdings are sent. Existing clients retain capability-4 trophy-room behavior and receive no new messages. Pending state is stored in `PlayerTrophies.notified`; revoked grants disappear from the pending queue.

Deployment: publish the complete server directory including `LeaderboardTrophies/*.png`, with Git LFS files hydrated. This branch includes the all-time stats additions and best-streak correction. Server deployment is separate from code push; ship the matching capability-5 client in the next signed patch. No live database edits or deployment were performed while implementing this feature.
