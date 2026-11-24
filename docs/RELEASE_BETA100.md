# Beta.100 server rollout

Combines the pending Roly trigger-death cooldown, replay rewind/roster repair,
Blockbuster drop-location exclusion, Tony caster-owned F points, and map-pool
preferences. Base production revision: 8a592077ffc807d9cbc704e08fed24c073b48ee7.
Only documentation conflicts required resolution when integrating the fixes;
both branches' records were retained.

Vander Ball Lightning RMB, including Ball to the Wall, now accepts partial
magazines. The signed client produces one projectile per loaded round, capped at
the native five-ball burst. The server verifies 1-5 unique projectile IDs against
its own magazine before broadcasting/recording the cast, and consumes the actual
accepted count. Zero ammo rejects firing; the existing InfiniteAmmo buff retains
five projectiles without consumption. A six-round perk magazine fires five and
keeps one. Each ball retains 12 splash player/world damage, native radius, spread,
range and timings. Primary fire and other weapons keep their native ammo rules.
Client prediction and partial-magazine reload gates are included in beta.100.

The reviewed three-document catalogue plan is
`catalogue-patches/beta100.patch.json`: Friendly pool 18 -> 26 and both Vander
weapon descriptions. It preserves all other fields/documents. Deploy with the
existing `deploy_gameplay_rollout.py` and `apply_gameplay_rollout.py`, using
`catalogue_card_count: 3` and matching manifest hashes. Default preflight is
read-only; the authorized --apply path cleanly stops, privately backs up SQLite
and documents, checks revisions, applies data, swaps the binary, health-gates
startup and guards rollback against concurrent catalogue edits.

VanderBurstFixture covers actual GameZone cast acceptance and ammo updates for
magazines 0-6 in both variants, forged excess projectiles, duplicate IDs and
unchanged per-ball damage. Shared count math matches the client byte-for-byte.
The client Unity fixture checks partial-use/reload gates and InfiniteAmmo.

This document records release scope. Actual deployed revision, signatures and
publication evidence are maintained in the client release-beta100 report.
