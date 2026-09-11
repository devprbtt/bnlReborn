# Sky Bridge Conquest pool activation

On 2026-09-11 the owner requested Conquest in the map pool in place of classic Don Edit.

- Persisted `map_sr2_sky_bridge_don_edit_conquest` as a public `map` card in CouchDB (previously runtime-generated only).
- Replaced `map_sr2_sky_bridge_don_edit` with Conquest in `map_list.friendly`, preserving position and the other 19 entries.
- Persisted Conquest in `map_list.custom`; classic remains there as a selectable alternate. Ranked, beginner pool, both map payloads and the original card are unchanged.
- Applied through revision-checked CouchDB writes, observed both change-watcher events and verified the control panel reports Conquest in friendly/custom and classic in custom only. No process restart.
- Rollback snapshot: `/root/config-backups/conquest-map-pool-20260911T230810Z`. Restore map_list using the current CouchDB revision if rollback is needed; the original map was never deleted.

`tools/activate_sky_bridge_conquest.py` reads local production credentials without printing them, defaults to read-only, and accepts `--apply` to perform the documented migration. Repeated application is idempotent. A revision conflict fails instead of overwriting concurrent pool edits.
