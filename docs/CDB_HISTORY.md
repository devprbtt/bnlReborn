# Hercules history retention

Hercules displays CouchDB `_revs_info`: compaction keeps ancestor IDs but removes
their bodies, so entries turn Missing. Raising `_revs_limit` does not prevent this.

`tools/preserve_cdb_history.py protect --apply` excludes only the `bnl` database
and its actual local shard names from automatic smoosh compaction. Both logical
and physical names matter: CouchDB 3.5.2 checks the shard name before compacting.
The config API persists settings in the existing mounted local.d configuration;
no CouchDB/game restart is needed. Previous settings are backed up under
`/root/config-backups/cdb-history-*`. Other databases retain their compaction policy.
Re-run protection if the database is recreated or resharded. The tool refuses
multi-node clusters and already-running compaction rather than claiming protection.

Install the script at `/opt/bnl-cdb-history/preserve_cdb_history.py` and the service
and timer in `/etc/systemd/system/`. Run `snapshot` once, then enable/start the
timer. Each pass archives every available ancestor of changed documents, including
intermediate edits between polls and deleted revisions, into
`/var/lib/bnl-cdb-history/revisions.sqlite3`. Pages and checkpoints commit together;
failure retries the page. Bodies are immutable and SHA-256 checked. Credentials
are read privately from the existing server config. The service only reads CDB.

Normal Hercules Load/View continues to use retained database revisions. The
separate archive is an additional recovery path:

```sh
sudo python3 /opt/bnl-cdb-history/preserve_cdb_history.py list gear_card_name
sudo python3 /opt/bnl-cdb-history/preserve_cdb_history.py export gear_card_name 12-revisionhash
```

Export never changes live cards. To revert, review the exported body and save its
content as a new revision using the current `_rev` (optimistic concurrency), rather
than inserting an old revision into the live tree. Preserve editor authorship and
`prev_rev` metadata. Back up the current document before applying any restore.

Already-missing bodies cannot be reconstructed from revision IDs. A matching
historical backup is required. `import-backup BACKUP.json gear_card_name` can save
a complete gear-card backup into the archive, recording its source path and file
SHA-256. It does not write to CouchDB or turn existing Missing entries green.
Manual compaction, purge, replication-only migration and history beyond the
database revision-tree limit can still remove native history. Do not compact `bnl`
without first running a successful archive pass and arranging an archive restore
path. A one-minute archive poll cannot guarantee edits purged before its next pass.

Retaining revisions grows the database; monitor filesystem space and archive size.
Include the archive in off-host backups (use SQLite backup API/a stopped writer for
a consistent copy). The local archive protects against compaction, not VPS loss.

References: Apache CouchDB compaction docs and tagged 3.5.2
`src/smoosh/src/smoosh_utils.erl` / `smoosh_channel.erl`.
