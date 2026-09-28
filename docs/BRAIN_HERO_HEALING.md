# Brains hero default healing

This is a catalogue-only change. Every Brains hero gains a default way to heal
teammates.

| Hero | Change |
| --- | --- |
| Trondson | Echo Pulse (Sonic Staff alt fire) heals allies in its radius: 15 HP instantly plus 5 HP/s for 4 s (recasting refreshes the timer). Pulse cost 5 -> 20 ammo; Echo-Location keeps its +6 penalty (11 -> 26). The pulse no longer reveals enemies (2026-09-28); its regen is applied through a friendly-targeted bunch because `self` wrappers are targeted relative to their holder. |
| Genie | The heavy orb no longer damages anything. It passes through enemies, stops on the first teammate it touches, and heals allies within 3 m at 5 HP/s. Speed Ball is the same orb at double speed; its burn and damage text is replaced. |
| Tony | The base Caulk Gun heals allied Heroes (3 HP per tick: 10 HP/s primary, 20 HP/s alt) and keeps its slow. The Healing Caulk perk, shop item and gear variant are removed. |
| Vander | The Static Gloves beam heals allied Heroes 1 HP per 0.1 s tick (10 HP/s). |
| Doc | Each Globe Gun globe heals allied Heroes within 2.5 m of its burst for 6 HP (Doc excluded). |

## Why these work without server code

- `all_units_bunch` is centred on the impact point, so the Echo Pulse (self
  hitscan) and the globe burst reach every ally in range; each heal's own
  targeting keeps them to friendly players.
- Hitscan and spinup hits use `RaycastUnitAndBlockAll`, which does not filter
  teams, so the beam reports teammates as targets and the server applies the
  friendly heal.
- A unit projectile stops on its first collision client-side. With
  `collide_with: friendly` the heavy orb ignores enemies; with
  `die_on_player_collision: false` the server keeps it alive when it touches a
  teammate, so it parks there until its 20 s lifetime ends. The owner is always
  excluded from collision. Friendly non-player units (turrets, devices) still
  count as world collision and pop the orb.
- Timed effects with the same key refresh rather than stack.

## Client follow-ups (not in this change)

- Green heavy orb for teammates: the orb prefab has no team-colour component.
- Caulk Gun lock-on: `ToolLogicChannel.DoChannelToUnit` ends the channel as
  soon as the crosshair leaves the target. Keeping the lock until the aim moves
  far enough or the trigger is released needs a client change; the server
  already keeps applying effects to the channel target.

## Publication

On the production host, from this branch:

```bash
sudo python3 tools/rebalance_brain_hero_healing.py
sudo python3 tools/rebalance_brain_hero_healing.py --apply
```

The first command is read-only. `--apply` creates the three new effects,
updates the cards with revision guards, unlists the perk before deleting its
three cards, saves every original document under `/root/config-backups`, and
verifies the result, including that nothing references a deleted card.

For an offline audit against a catalogue array:

```bash
python3 tools/rebalance_brain_hero_healing.py --from-file catalogue.json
```
