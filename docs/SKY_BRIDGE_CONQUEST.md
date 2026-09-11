# Sky Bridge Don Edit - Conquest

Custom-match experiment: `map_sr2_sky_bridge_don_edit_conquest`, independently
selectable alongside the unchanged `map_sr2_sky_bridge_don_edit`. Registration
adds only the experimental custom-list entry when both its payload and the
original catalogue card exist. No ranked/friendly pool changes.

Three 12x12 squares centered on the original BB drop markers use a +/-3 vertical
range and 10-second majority capture. Ties/empty zones pause capture and retain
ownership. Changing capturing teams resets capture progress. Two owned zones
earn one scoring second per second. Both clocks start at zero, target 600 seconds.

At threshold, Light → Classic → Uber BB attacks last 90 seconds. Only defender
objectives become vulnerable. BB restores after respawn at the original deadline.
The last living attacker dying ends the attack immediately; disconnects also
end attacks when none remain alive. End resets both clocks/ownership and restores
all cube shields. Following the first Uber attack, repeat Uber at a 180-second
scoring target. Existing objective destruction wins the match. Normal initial
build phase remains; ordinary resource supplies continue but BB crates are blocked.

`SkyBridgeConquest` contains deterministic rules. `GameZoneConquest` binds them
to authoritative units, effects, damage protection, spawn/death and snapshots.
The per-unit objective guard rejects both damage overloads, including effects
with ignore-invincibility/defences flags. Shield visuals use existing CDB effects.

Protocol: optional bit 9 of `ZoneUpdate` (ten bits total, still two bytes) adds a
BinaryWriter string with a JSON snapshot after ResourceCap. Full snapshots include
round, attacker, attackRemaining, target, team1, team2, tier, halfWidth, capturing,
and zones (x/y/z, owner, capturing, progress, contested). Send at 5 Hz and in initial
snapshots. Ordinary matches omit this field. Use the matching updated Unity client
for experimental matches; no live deployment is included in this change.

Map geometry is unchanged in a separate payload; see CONQUEST_MAP_PROVENANCE.json.
The new payload must accompany any later deployment, or the entry is not registered.

Validation, from repository root:

```sh
dotnet run --project tests/BNLReloadedServer.ConquestFixture
dotnet run --project tests/BNLReloadedServer.BlockbusterFixture
dotnet build BNLReloadedServer/BNLReloadedServer.csproj -c Release -warnaserror
```

Conquest fixture: 38 checks, including actual map loading, both entries, unchanged
matchmaking pools, timing, majority/ties, elevation, round transitions, damage
guard, BB reapplication/deadline and optional wire payload. Multiplayer placement,
presentation and balancing still require a playtest with the updated client.

## Spawn health correction (pending deployment)

Conquest's extra spawn update must contain only Effects/Buffs. UnitCreated is
called before Unit.Respawn resets health, forcefield and ammunition. A full
snapshot captured there contains zero/previous health; its buffered delivery can
overwrite the later unbuffered full-health reset on the client. The correction
uses an effects-only packet. The fixture now exercises real Unit.Respawn with
wire serialization and delayed Conquest delivery, for first spawn and respawn:
46 checks pass. The regression fails with the former full snapshot.

User requested no server restart while players are online. This correction is
source-only until a separately authorized deployment window.
