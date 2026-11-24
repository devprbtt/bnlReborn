# Beta100 one-cube map protection repairs - 2026-11-24

Player-facing note: Fixed missing cube foundation metal on Insandity and gaps in
respawn no-build protection on Aztec Temple, Citadel, Insandity and Parapet.

Status: source repair, not deployed. No client protocol or CDB changes required.

The production map payload hashes were checked read-only against revision
`ed896f6bd255c3ce4a5e9e700135b487ab335f0d`. They matched all four Git inputs.
The earlier pool-expansion audit checked objective counts and spawn presence,
but did not check cube foundation completeness or respawn air gaps.

## Existing behavior copied

Search and Destroy has a 4x4 standard metal foundation directly below each
4x4 base cube. Its enclosed main spawn interior contains 108 cells, entirely
filled with `block_air_locked` (ID 59). Skull Block uses the same native invisible
block throughout its spawn interiors, with two remaining air gaps per main room.
The native CDB describes this block as the spawn-room fill that prevents building:
non-solid, passable by everyone, not replaceable and indestructible.
`block_metal` is ID 10, solid and indestructible; the server treats it as
self-supporting through `CardBlock.CanFloat`.

The four added maps have room walls, forcefields and existing no-build cells
already defining enclosed respawns. Air connected to each spawn through air or
existing no-build cells was filled with ID 59. Solid walls and forcefields stop
the fill. No arbitrary radius or restriction outside those rooms was introduced.

| Map | Spawn rooms | Interior cells per room | Air gaps filled | Metal added |
| --- | ---: | ---: | ---: | ---: |
| Aztec Temple | 4 | 108 | 200 | 0 |
| Citadel | 2 | 1102 | 532 | 0 |
| Insandity | 2 | 276 | 232 | 16 |
| Parapet | 2 | 90 | 6 | 0 |

Insandity team 2's base starts at (150,22,23). Its supporting layer at Y=21
contained 12 air and four dirt cells. It now has metal at X=150..153,
Z=23..26, Y=21. Team 1 and both bases on the other three maps already had
complete foundations. Unit positions were preserved.

## Reproduction and validation

From the server repository root:

```text
python tools/repair_one_cube_protection.py --check
dotnet run --project tests/BNLReloadedServer.MapProtectionFixture -- <public-catalogue.json>
```

Omit `--check` to regenerate. Inputs come from the immutable baseline Git commit,
then are copied into ignored `extracted/one-cube-protection-inputs/` with source
paths, revision and SHA256 provenance before transformation. The generator
refuses unrelated changes, unexpected room sizes, leaks or support materials.
`one-cube-protection-validation.json` records exact input/output hashes.

Validation passed with the recovered public catalogue used in beta100:
2,580,130 checks through actual `MapBinary` decoding, stability initialization,
native block properties and client map snapshot serialization/deserialization.
Every cell survives the snapshot round trip; all 970 newly locked cells remain
passable and non-replaceable, all 16 new foundation cells are solid indestructible
metal, and all eight cube foundations are complete. The generator separately
compares every cell against the original and verifies that all non-terrain
metadata (including units, spawns, cameras, properties and colors) is unchanged.
No live match was played as part of this validation.

Deploy all four `Maps/*.bnlbin` payloads together. Existing game instances retain
their loaded terrain; only newly loaded matches receive the repairs. Preserve
the four prior files for rollback and verify their current hashes against the
recorded before hashes before replacing them. A binary-only server rollout that
preserves the shared Maps directory will not install these map edits by itself.
