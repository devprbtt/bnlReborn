# Ninja attack and shuriken reload balance

This is a catalogue-only change.

The base katana and its Bloody Bleeding and Mystical Teleport variants use the
same 0.65-second primary melee attack time. Increasing that duration by 15%
makes the attack slower:

```text
0.65 * 1.15 = 0.7475 seconds
```

The base and Bloody Bleeding shuriken cards currently use a regenerating
20-star pool with no magazine or reload. They are changed to a three-star
magazine and a one-second full-clip reload. Primary fire consumes one star;
alternate fire consumes all three. Firing the last available star starts the
reload, and attempting to fire while empty also starts it. Pool size and its
regeneration rate remain unchanged.

No client change is required. All six recovered first-person Ninja skin bundles
already contain a one-second `Shuriken_reload` clip, and every
`NinjaShurikenPlayer` handler has `ReloadType: 1` with that clip assigned.
The server already implements the same full-clip magazine path used by other
weapons.

## Publication

On the production host, from the checked-out server release:

```bash
sudo python3 tools/rebalance_ninja_attack_and_shuriken_reload.py
sudo python3 tools/rebalance_ninja_attack_and_shuriken_reload.py --apply
```

The first command is read-only. `--apply` carries the current CouchDB revisions
as concurrency guards, saves all five original cards under
`/root/config-backups`, and verifies the saved documents. The catalogue watcher
loads the change without a server restart.

For an offline audit against a catalogue array:

```bash
python3 tools/rebalance_ninja_attack_and_shuriken_reload.py --from-file catalogue.json
```
