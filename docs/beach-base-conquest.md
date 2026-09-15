# Beach Base Mid Edit - Conquest

Separate map ID: `map_sr2_beach_base_new_conquest`; source: `map_sr2_beach_base_new`.
The dedicated map payload changes only its name, description and identity. Geometry, units, spawn points, barriers, environment, cameras and triggers are preserved. Registration adds the variant to custom matches and retains the original and all matchmaking pools.

| Zone | Marker centre (X, Y, Z) | Floor block Y | Walkable surface Y |
| --- | --- | --- | --- |
| A | 128, 7, 9 | 1 | 2 |
| B | 128, 9, 48 | 3 | 4 |
| C | 128, 9, 76.5 | 3 | 4 |

Zones follow ascending Z rather than the source file's marker ordering. All capture areas are 12 by 12 world units, extending five below and four above their markers. The reduced depth keeps every floor above the map's water (Y=1.5) and kill plane (Y=0.5). Complete metal floors contain 444 blocks: A/B use 12 by 12 cells, while C covers 12 by 13 cells to fully cover its fractional Z footprint. Floors are added only to the runtime Conquest copy.

Capture takes 10 seconds with player majority. Two owned zones score 1x and three score 2x; Light/Classic/Uber thresholds are 7/5/3 minutes. Attacks last 90 seconds, with existing wipe termination, objective shields, respawn effects, resource supplies, round reset and Conquest result statistics. Initial bricks are 2,000 with a 3,000 cap. Existing client HUD/minimap/zone-wall code reads the server snapshot and supports this map without map-specific client changes.

Validation: `dotnet run -c Release --project tests/BNLReloadedServer.ConquestFixture` from the server repository root (100 checks). This includes original and variant map equality, custom-only idempotent registration, all safe floor positions, capture from floor surfaces, original Sky Bridge regressions and the actual GameZone initialization path. A multiplayer playtest is still needed to assess balance and terrain sightlines.

Deployment must include `Maps/map_sr2_beach_base_new_conquest.bnlbin` alongside the server code before restarting. Map registration checks for its presence during catalogue load. A code-only hotfix will not make the map appear without that payload. No queue rotation change or live deployment has been made.

`tools/create_beach_base_conquest.py` regenerates the payload from a verified source copy and refuses to overwrite a differing existing variant. The root client repository also records the recovered source copy/hash in `docs/BEACH_BASE_CONQUEST_INPUTS.json`.
