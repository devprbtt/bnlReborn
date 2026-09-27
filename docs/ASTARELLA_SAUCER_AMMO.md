# Astarella saucer ammo balance

This is a catalogue-only change covering both live Saucer Launcher cards:

- `gear_astro_saucer_launcher`
- `gear_astro_saucer_launcher_fly_away_bad_guy`

Both currently have a four-round magazine, an 18-round pool, and `0.9` base
ammo regeneration. The magazine becomes three rounds and regeneration becomes:

```text
0.9 * 1.30 = 1.17
```

Pool size, reload time, damage, fire timing, projectiles, and every other field
remain unchanged. No client code or asset update is required.

## Publication

On the production host, from the checked-out server release:

```bash
sudo python3 tools/rebalance_astarella_saucer_ammo.py
sudo python3 tools/rebalance_astarella_saucer_ammo.py --apply
```

The first command is read-only. `--apply` carries both current CouchDB
revisions as concurrency guards, saves the original cards under
`/root/config-backups`, and verifies the saved documents. The catalogue watcher
loads the change without a server restart.

For an offline audit against a catalogue array:

```bash
python3 tools/rebalance_astarella_saucer_ammo.py --from-file catalogue.json
```
