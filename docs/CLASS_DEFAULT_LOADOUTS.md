# Default blocks by hero class

Prepared 2026-10-19; not applied to production.

The hero's special block stays in slot 1. Regular slots 2 through 6 are:

| Class | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- |
| Brawn | Crates | Bricks | Mines | Spawn point | Radar |
| Skills | Crates | Dynamite | Bear trap | Bounce pad | Speed pad |
| Brains | Crates | Bricks | Fire trap | Healing station | Ammo station |

`tools/set_class_default_loadouts.py` changes only `data.default_devices` on
`unit_hero_*` player cards, resolving class through the hero-class card. This is
catalogue data, so normal server/client catalogue replication supplies the same
defaults to both. No server binary or client protocol change is needed.
Saved custom loadouts, special devices, perks, unlocks and test dummies are untouched.

Validation: five unittest cases cover all classes and exact order, idempotence,
independent lists, preservation of unrelated fields, and rejected missing devices
or classes. An offline catalogue audit changes all 15 heroes (five per class),
only their default-device fields, and produces no writes on the second pass.

```
python -m unittest discover -s tools/tests -p test_class_default_loadouts.py -v
python tools/set_class_default_loadouts.py --from-file catalogue.json --write-after candidate.json
```

For deployment, run the script on the production host without flags to review a
fresh plan from CouchDB, then with `--apply` under the authorized rollout. It backs
up all affected cards under `/root/config-backups`, retains revision guards, and
checks each saved document. Existing catalogue watching publishes the updates.
Writes are per document, not atomic across all heroes: a conflict stops the run;
rerun to plan only remaining changes. Do not upload the offline candidate over a
newer live catalogue. No player-profile reset is required or performed.
