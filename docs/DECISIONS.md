

## 2026-10-19 - Replay seek back-pressure

Use capability-gated ordered barriers and viewer acknowledgements so slow clients bound catch-up traffic. Sequence numbers survive rewind generations; only the owner and current pending sequence may acknowledge. Forward seek uses the existing reader; only rewind reloads the scene. Live games and legacy capability negotiation remain compatible.

## 2026-10-21 - Hero score attribution and notification opt-out

- Resolve hero-effect score credit from validated effect sources rather than hero proximity or client reports. Support for building/destruction is awarded on confirmed work while the relevant boost is active. Cooldowns use owner/reason/target; repairs and F healing use actual health restored. Doc F replaces generic healing awards to avoid duplicate credit.
- Preserve Genie lamp builder in a server-only ScoreLampOwner field on created loot. Do not transfer gameplay ownership to award support points.
- Keep hero point values and thresholds in the existing per-match CDB rules snapshot; client notification visibility is a local persistent preference, independent of authoritative totals.

## 2026-10-21 - Repair score eligibility

Track enemy health damage per repairable device, rather than excluding owned devices or checking only the last attacker. Every actual heal consumes this budget; Tony scores only the consumed portion. Voxel budgets use damage bytes converted to HP at repair. Preserve defensive repair rewards without reusable friendly-damage credit.

## 2026-10-21 - Build reward follows resources spent

Use the same confirmed TotalCost charged by construction for device build XP, rather than base catalogue price or flat points. Keep build scaling independent of destruction scaling through dedicated CDB fields. Build time has no additional multiplier. Both placement routes feed one score function; preserve rebuild cooldown and objective bonuses.
