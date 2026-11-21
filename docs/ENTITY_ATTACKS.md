# Entity player damage in CDB

Edit the `global_logic` CouchDB document, adding this server-only object:

```json
"entity_attacks": {
  "beam_player_damage_min": 20.833334,
  "beam_player_damage_max": 100,
  "fireball_player_damage": 40
}
```

The beam interpolates linearly between minimum and maximum over its existing
2.5-second charge. The new default minimum preserves the previous charge ratio.
Fireballs use the configured value per explosion. Missing fields use the defaults
above; zero disables that source of player damage. Values must be finite,
nonnegative and at most 100000, with minimum no greater than maximum.

Deploy the matching server first. Initialize the fields with
`python3 tools/configure_entity_attacks.py` (read-only preview), then
`python3 tools/configure_entity_attacks.py --apply` during an authorized CDB rollout.
The script reads credentials from the host config, backs up the original document,
uses revision guards and verifies readback. It preserves existing tuning and all
unrelated fields. Offline preparation accepts `--from-file global_logic.json
--output candidate.json`. `catalogue/entity-attacks.json` is a merge fragment,
not a replacement for the whole document.

After server catalogue replication receives a CDB edit, the next beam shot or
fireball explosion uses it, including in existing matches. No client update is
required. An in-flight fireball uses the setting at impact. Normal team,
invulnerability, spawn and terrain occlusion protections still apply.

Block damage remains 5000–25000 for the beam and 1500 for fireballs. Device and
objective damage, attack geometry, cooldowns and gravity actions are unchanged.
The new settings are omitted from legacy client binary catalogue serialization.

Validation: `dotnet run --project tests/BNLReloadedServer.AdminFunnyFixture -c Release`
and `dotnet build BNLReloadedServer -c Release -warnaserror`.
