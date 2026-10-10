# Block placement beneath portals

The reported clip shows a green block preview beneath a Kira portal followed by
build effects without a persistent block. The live `unit_device_kira_portal`
revision `6-f75db5c497826e7d8072da8939b26325` has size 1x2x1 and Center pivot.
Devices spawn at their first grid cell's center. The server's centered unit AABB
therefore extends half a cell down into the supporting block; `CanPlaceBlock`
treated that combat bound as occupied build cells. Client `UnitSizeHelper`
instead reserves Size cells starting at floor(device position).

For block placement only, portals now use that client grid footprint. All other
unit bounds, player movement prediction, combat collision, support stability and
attachment checks retain their existing behavior. This applies both to direct
placement and the shared block-spawn validation; slope attachment occupancy uses
the same corrected portal footprint.

Validation:

- New PortalBuildFixture reproduced the support-cell rejection before the fix.
- All 12 final checks pass through production MapBinary.AddBlock and octree
  collision: support replacement, ordinary supported brick, unsupported brick
  rejection, both occupied portal cells blocked, four adjacent cells allowed,
  player self-embedding and ordinary device overlap rejected. Original centered
  bounds remain unchanged outside build occupancy.
- Existing BuildCorrectionFixture: 4 checks pass.
- Existing TeleportPlacementFixture: 356,096 casts/landings; zero server-rule
  violations, capsule overlaps or cameras in force fields.

Server-only code fix, built/tested locally and pushed. Not deployed: no server
restart or live card change performed. No live multiplayer acceptance capture.
