# Action-driven device scoring — pending server deployment

Placement and construction-boost assists award zero points, including near objectives. Legacy catalogue build values cannot re-enable them. Bricks/resource refunds and existing kill/assist statistics are unchanged.

| Action | Default points | Repeat rule |
|---|---:|---|
| Heal allies (stations or heroes) | 5 per 50 effective HP | Only enemy-caused missing health; source shares split, no self credit |
| Ammo station actually refills an ally | 5 | Per owner/ally every 15 seconds; full ammo and self-use excluded |
| Radar reveals enemy hero | 5 | Per owner/target every 15 seconds, shared with ability spotting |
| Radar reveals enemy device unit | 5 | Once per owner/target device lifetime; leaving/re-entering or rebuilding radar does not reset it |
| Ally uses spawn / Rally Point | 10 | Per owner/ally every 30 seconds, only successful respawns |
| Ally uses portal | 10 | Per owner/ally every 60 seconds across all owned portals; owner receives entry-side credit |
| Bomb damages enemy hero/device | 10 | Per owner/target every 15 seconds (voxel targets use their cell) |
| Tiki hits enemy hero | 5 | Per owner/target every second, suppresses multiple darts |
| Turret/cannon/Tesla damages enemy hero | 5 per 50 HP | Actual damage accumulates across turrets/targets; completing hit determines objective bonus |
| Mortar hits enemy hero | 10 | Per owner/target every second |
| Mortar/C4 destroys enemy device | +10 | On destruction, alongside normal cost-based device destruction award |
| C4 damages enemy hero | 10 | Per owner/target every 15 seconds |
| Roly barrel hits/slows/poisons enemy | 10 | Existing shared 15-second effect cooldown |
| Bear trap / mine affects enemy hero | 10 | Per owner/target/type every 15 seconds |
| Caltrops / glue / bush / fire trap affects enemy hero | 5 | Per owner/target/type every 15 seconds |

Kills still receive the normal kill/accolade award once. Specific device-hit rewards replace generic device-damage points for the same damage. Other damaging devices retain 5 per 100 enemy-hero HP. Generic device destruction remains ceil(base cost / 20), min 5/max 50; ice remains excluded. Unowned, self, friendly and neutral targets cannot earn combat device points.

The existing 12-unit objective area around living cubes and Block Buster spots still grants +25% (floor, minimum +1); kills get at least +20. No placement award means no placement objective bonus. Shared cube destruction is still exempt from the extra bonus.

Implementation uses confirmed damage, applied status effects, credited healing, actual ammo changes, completed teleport/respawn callbacks and active radar marks. Both raycast and flood explosion paths now credit destruction of voxel devices, with source ownership preserved. Radar device rewards refer to device units actually marked by the radar, not unmarked terrain blocks.

No client protocol change is needed: existing clients show server award reason strings. New rule fields default when absent from live global_logic.action_scores. Legacy placement fields remain parseable but inert. Prepared notes and scoring guide are not live yet.

Validation: ActionScoreFixture exercises production callbacks, supply, source attribution, cooldowns, no-double-counting, both explosion paths and objective bonuses. HealAttribution, BrawnRally, ClassPerksExpanded and KillerAmmo regression fixtures also pass. No live multiplayer match validation in this change.
