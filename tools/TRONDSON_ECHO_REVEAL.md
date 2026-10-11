# Echo-Location perk reveal

Adds a seven-second enemy-player reveal to the perk staff right-click within its existing 11-meter radius. Base staff, primary fire, ally healing and 26-ammo cost are preserved. Updates the perk and perk weapon descriptions; stale translated descriptions fall back to the updated English text.

Run `python3 tools/add_trondson_echo_perk_reveal.py` on the server for a read-only plan. Add `--apply` only for an authorized CDB deployment. The tool reads credentials privately from the current server configuration, saves before-images under `/root/config-backups/trondson-echo-perk-reveal-*`, uses CouchDB revision preconditions, verifies readback and attempts revision-guarded rollback if the write fails. For a later rollback, review the before-images against current revisions before restoring them; preserve unrelated later edits.

No server restart or binary release is required. Clients should re-login and re-equip/rejoin to pick up changed cards. The old marker card is reused without modification. Do not reintroduce reveal to the base staff.

Validation: `python -m unittest discover -s tools/tests -p test_trondson_echo_perk_reveal.py`. Includes a public-card fixture read from the live CDB on 2026-10-11. A live read-only dry run planned exactly the perk staff and perk description cards. Deployed 2026-10-11 at 20:44 UTC without restarting the server. Both changed cards passed readback; a second read-only run found no pending changes. Backup: `/root/config-backups/trondson-echo-perk-reveal-20261011T204429Z`.
