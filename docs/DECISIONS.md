

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

## 2026-10-21 - Build score exclusions by catalogue family

Use built card family IDs for requested build-point exclusions, covering hero/cosmetic variants without matching unrelated effects. Keep exclusions local to direct build rewards; preserve independent support and destruction rewards.

## 2026-10-21 - Trap engagement scores

Award confirmed enemy impact rather than placement for requested traps. Use admitted status application for slowing and damaging status, and actual damage for mine/fire/bush hits. Share per-owner/reason/enemy cooldown between damage and status so a single effect cannot pay twice. Retain source ownership on spawned fire and periodic effects; no reward for unowned map hazards. Yeti ice only loses build rewards; no new ice effect award requested.

## 2026-10-23 — One Discord presence bot owned by server repository

Retire the China service and credential; poll only the local Reborn player-count endpoint. Track the recovered Rust reconnect implementation and lockfile with provenance in infrastructure/discord-presence. Preserve the application shared with game Rich Presence. Validate bot identity/membership on startup; permanent credential/configuration failures exit 78 without restart, transient failures retry after 30 seconds. Retain the verified Rust executable and add a Python standard-library preflight, avoiding an unnecessary live-server rebuild. Credentials stay outside Git in root:bnldiscord 0640 runtime configuration.
