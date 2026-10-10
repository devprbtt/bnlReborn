# Hercules history preservation deployed October 10, 2026

- Confirmed CouchDB 3.5.2 automatic bnl-shard compaction in container logs on
  October 8 and 9. Native history reported missing bodies while retaining IDs.
- Applied persistent `smoosh.ignore` entries for `bnl` and both actual local shard
  names, `shards/00000000-7fffffff/bnl.1788190168` and
  `shards/80000000-ffffffff/bnl.1788190168`. Read back API settings and mounted
  `/etc/bnl/couchdb/local.d/docker.ini`; other database policies untouched.
  Prior setting backup: `/root/config-backups/cdb-history-20261010T215317Z`.
- Installed `/opt/bnl-cdb-history/preserve_cdb_history.py` and enabled the minute
  timer. Archived 3,834 available revisions including deleted cards. Independently
  checked all 1,897 available revisions of current live cards and every archive
  SHA-256; compared exported prior Eliza revision 4 with its actual database body.
- Initial live test found CouchDB ignores `revs_info` with an explicit `rev`.
  Corrected traversal to use `revs=true`, reset the initial leaf-only checkpoint,
  and verified older versions were archived. Later timer pass processed zero new
  changes successfully. Regression tests cover this upgrade, transient failure,
  burst edits, deleted cards, missing ancestors, integrity, and backup recovery.
- Matched screenshot to `gear_abe_snow_thrower`. Revisions 22–26 were available.
  Recovered missing revisions 8, 9 and 10 from September 18 config backups into
  the archive only, recording original paths and SHA-256. Final archive: 3,837
  revisions. No live card edits and no fabricated historical bodies. These three
  recovered versions are available via archive export, not native Hercules Load.
- No game-server or CouchDB restart. Game PID 752343, NRestarts=0; container
  start time remains 2026-08-31T13:58:26.719045418Z, RestartCount=0.
- Known limits: already-compacted native entries stay Missing; archive is on the
  same VPS and not integrated into Hercules UI. Manual purge/compaction, migration,
  or revision-tree pruning still require the independent archive recovery path.
