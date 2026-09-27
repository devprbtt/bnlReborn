# Nigel headshot balance

Nellie's base card currently deals 40 player damage with a 1.5 critical
modifier: 60 damage per full-range headshot and 120 from two. Standard Brawn
heroes have 160 health, so the old rifle leaves 40 health.

The requested breakpoint is 105% of standard Brawn health:

```text
160 * 1.05 = 168 total
168 / 2 = 84 per headshot
84 / 40 = 2.1 critical modifier
```

`tools/rebalance_nigel_headshots.py` changes only `gear_hunter_rifle`'s critical
modifier from 1.5 to 2.1. Body damage, block/objective damage, fire timing,
ammo, and range falloff remain unchanged. Two headshots inside the 80-unit
full-damage range therefore kill a 160-health Brawn with 8 damage to spare,
covering up to 8 health restored between the hits.

Cogwheel is the Brawn-class tank exception at 210 base health and is
intentionally not a two-headshot kill under this standard-Brawn target. Damage
also continues to fall off beyond 80 units. Historical catalogues that still
contain the 60-damage High Caliber Rounds variant are audited but not modified;
its existing 90-damage headshot already exceeds the requested breakpoint.

## Publication

On the production host, from the checked-out server release:

```bash
sudo python3 tools/rebalance_nigel_headshots.py
sudo python3 tools/rebalance_nigel_headshots.py --apply
```

The first command is a read-only plan. `--apply` uses CouchDB's current `_rev`
as a concurrency guard, writes a rollback snapshot under
`/root/config-backups`, and verifies the saved rifle plus the untouched Brawn
card (and the legacy High Caliber card when present). The running catalogue
watcher picks up the change; a server restart is not required.

For an offline audit against a catalogue array:

```bash
python3 tools/rebalance_nigel_headshots.py --from-file catalogue.json
```
