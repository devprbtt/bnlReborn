# Nigel headshot balance

Nellie's base card currently deals 40 player damage with a 1.5 critical
modifier: 60 damage per full-range headshot.

The requested damage is exactly 70 per full-range headshot:

```text
70 / 40 = 1.75 critical modifier
```

`tools/rebalance_nigel_headshots.py` changes only `gear_hunter_rifle`'s critical
modifier from 1.5 to 1.75. Body damage, block/objective damage, fire timing,
ammo, and range falloff remain unchanged. The headshot deals exactly 70 inside
the existing 80-unit full-damage range and continues to fall off beyond it.

## Publication

On the production host, from the checked-out server release:

```bash
sudo python3 tools/rebalance_nigel_headshots.py
sudo python3 tools/rebalance_nigel_headshots.py --apply
```

The first command is a read-only plan. `--apply` uses CouchDB's current `_rev`
as a concurrency guard, writes a rollback snapshot under
`/root/config-backups`, and verifies the saved rifle. The running catalogue
watcher picks up the change; a server restart is not required.

For an offline audit against a catalogue array:

```bash
python3 tools/rebalance_nigel_headshots.py --from-file catalogue.json
```
