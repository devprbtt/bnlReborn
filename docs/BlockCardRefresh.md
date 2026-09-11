# Block-card refresh deployment

Runtime commit a50c85bfcb743e7cd756eac8ba10a7af0e43ac22 was deployed on
2026-09-11 UTC. BlockCardsCache now reads the current catalogue snapshot,
so catalogue updates, removals and replication refresh block-ID lookups.
Existing derived unit state and client rendering caches are outside this fix.

Regression fixture:
`dotnet run --project tests/BNLReloadedServer.BlockCardRefreshFixture -- <catalogue.json>`

Passed all three caltrops variants, existing block health/resistance and cost,
removal/re-add, ID reassignment, replication and concurrent reads.

Deployment initially failed because live map_testing had a newly introduced
empty data payload (size={}, blocks_data=null). The previous release failed on
that document too. After backing up the document, restored only the data field
to its previous revision's absent state; all other fields remained unchanged.
Recovery CouchDB revision: 7-9e336af4c43983e9b5f1ab5b1a6fd00c.

The new release passed signed-authentication/startup checks. Follow-up confirmed
active/running, zero restarts, all three listeners and no error-priority journal
entries. In-game live editing remains to be checked.

The deployment helper uses an EXIT trap so explicit health-gate failure exits
also restore the previous release. Every server fix must be committed and pushed
to this repository; deployment follows the user's authorization.
