# Deathmatch no-build zones removed

Deathmatch no longer rejects construction inside the former six-block-radius spawn columns. Its map initialization also clears locked/no-build cells (block 59) and removes units labelled NoBuildZone from the instance. This runs only for WaitingArenaInitiator; normal maps retain their build restrictions. Existing occupancy, resource, map-boundary and placement validation still apply.

The terrain change happens before stability and initial/late-join snapshots. Shared source map bytes are unchanged. The matching Unity client disables its local spawn-radius veto. Both updates are required, and a new deathmatch instance must be created after deployment to regenerate terrain.

Validation: TeamFeaturesFixture 170 assertions; DeathmatchFollowupFixture 28 checks with the actual Search and Destroy map and live-cards-20261017 catalogue. The map contains 348 locked cells, all cleared in deathmatch; other terrain is preserved apart from the existing 60 forcefield removals. Late-join snapshot checked for remaining locked cells. Source only, not deployed.
