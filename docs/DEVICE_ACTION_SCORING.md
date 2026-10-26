# Action-driven device scoring — pending server deployment

Placement and construction-boost assists award zero points, including near objectives. Existing kills, assists and brick/resource refunds are unchanged. Cosmetic variants use the same family rules.

| Action | Default points | Repeat rule |
|---|---:|---|
| Heal allies | 5 per 50 effective HP | Enemy-caused missing health only; no self credit |
| Ammo station refills ally | 5 | Every actual refill; full ammo and self-use excluded |
| Radar scans enemy hero | 5 | Each server scan tick (currently 200 ms), once per owner/target/tick across overlapping radars |
| Radar scans enemy device | 5 | Once per owner/target device lifetime |
| Ally uses spawn / Rally Point | 10 friendly side; 20 enemy side | Every successful respawn; enemy side is beyond the map X midpoint relative to the owner's team |
| Ally uses portal | 10 | Once per ally per owner every 60 seconds across all owned portals |
| Bomb damages enemy hero/device | 10 | Every confirmed damage event, no repeat cooldown |
| Tiki hits enemy hero | 5 | Existing one-second per-target limit retained |
| Turret/cannon/Tesla damages enemy hero | 5 per 100 HP | Accumulated across hits and targets |
| Beacon / Miasma Mine / Torpedo damages enemy hero | 5 per 50 HP | Separate accumulated damage per family |
| Gravity Well explosion hits enemy hero | 5 | Every confirmed hit; pulling alone gives none |
| Kreepy in the Box hits enemy hero | 5 | Every confirmed hit |
| Mortar / C4 damages enemy hero | 10 | Every confirmed hit, no repeat cooldown |
| Roly barrel/goo damages or newly slows enemy | 10 | No repeat cooldown; refreshing an unchanged slow is not a new slow |
| Bear trap / mine damages enemy hero | 10 | No repeat cooldown |
| Caltrops / bush / fire trap damages enemy hero | 5 | No repeat cooldown; damage and its status application are not both paid |
| Glue applies slow to enemy hero | 5 | No repeat cooldown; unchanged effect refresh does not score again |
| Enemy triggers fake block | 10 | Actual entry trigger; lingering is not another trigger; block normally destroys itself |
| Genie lamp allied pickup | 10 | Once per pickup, no self credit |
| Genie lamp gas damages enemy hero | 5 per 100 HP | Builder ownership preserved when cloud spawns |
| Destroy enemy device | ceil(base cost / 20), min 5 / max 50 | All attributed sources, including beacon, mortar and C4; no additional flat +10; ice excluded |

Other damaging devices keep 5 per 100 enemy-hero HP. Kills retain normal kill/accolade awards. Specific hit rewards replace generic damage points for the same damage. Ordinary digging gives no points. Self, friendly, neutral and unowned sources cannot earn combat device points.

Objective-area bonuses remain: within 12 units of a living cube or Block Buster spot, +25% (floor, minimum +1), kills at least +20; shared cube destruction exempt. Enemy-side spawn bonuses can also receive the objective bonus.

Only requested device cooldowns changed. Portal remains 60 seconds; Tiki remains one second; unrelated hero abilities retain existing repeat rules. Tiki's reveal still earns ability-spot credit separately. Legacy ammo/spawn/trap interval and flat mortar/C4 destruction fields are accepted but inert.

Validation: 278 action-score checks, HealAttribution 22, BrawnRally 47, GenieOrbHealing 21, Transport 6, ClassPerksExpanded 70 and KillerAmmo ordering suite. Release build treats warnings as errors. No live multiplayer validation or deployment yet.
