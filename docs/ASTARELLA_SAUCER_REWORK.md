# Astarella saucer rework (prepared)

Owner confirmed 50% faster perk saucers and corrected the damage request to 24 -> 21 player damage. Base player damage remains 24; block damage remains 26.

| Setting | Default | Fly Away Bad Guy |
| --- | --- | --- |
| Player damage | 24 | 21 |
| Block damage | 26 | 26 |
| Knockback force / midair | 8.5 / 8.5 | 8.5 / 8.5 |
| Launch speed | 1 | 1.5 |
| Acceleration / speed cap | 100 / 100 | 150 / 150 |
| Reload | 2 seconds | 2 seconds |

The former perk's +70% knockback becomes baseline (force 5 -> 8.5). Its old 15% reload penalty is replaced by the damage tradeoff. Magazine 3, reserve 18, ammo regeneration 1.17, 0.85-second movement timeout, explosion radius, manual detonation and other fields remain unchanged. Scaling only max speed would not produce the requested flight-speed increase; initial speed and acceleration are scaled too.

`python tools/rework_astarella_saucers.py` runs read-only on the host. `--from-file catalogue.json` plans offline. Only explicit `--apply` writes: guarded by current CouchDB revisions, all-card preflight, private original/planned backups and readback; partial failures attempt reverse-order revision-guarded rollback. Catalogue reload handles activation without a server restart. This is not a multi-document atomic database transaction; apply with no active matches to avoid mixed catalogue reads.

Validation: three Python tests with confirmed damage, idempotence, unexpected-input refusal, preserved ammo/detonation and 1.5x velocity at multiple flight times. Live read-only dry run proposes exactly five changed cards. No production writes or deployment yet.
