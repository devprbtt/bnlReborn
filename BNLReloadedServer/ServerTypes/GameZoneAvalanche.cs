using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private static readonly Key AvalancheAreaKey = new("unit_abe_avalanche_area");
    private static readonly Key PermafrostAreaKey = new("unit_abe_permafrost_area");
    private static readonly Key AvalancheAreaSlowKey = new("effect_abe_avalanche_area_slow");
    private static readonly Key PermafrostRootKey = new("effect_abe_permafrost_root");
    private readonly Dictionary<uint, AvalancheOccupants> _avalancheAreas = [];

    private sealed class AvalancheOccupants(Unit area)
    {
        public readonly Unit Area = area;
        public readonly UnitSource Source = new(area);
        public readonly Dictionary<uint, (Unit Unit, DateTimeOffset Since)> Inside = [];
        public readonly HashSet<uint> Rooted = [];
    }

    // Called on the zone thread, on creation and every simulation tick. Areas are ordinary
    // replicated units: reconnecting clients receive them and expiry drops the visual too.
    private void TickAvalancheAreas(DateTimeOffset now)
    {
        foreach (var area in _units.Values.Where(u => u.Key == AvalancheAreaKey || u.Key == PermafrostAreaKey))
            if (!_avalancheAreas.ContainsKey(area.Id)) _avalancheAreas.Add(area.Id, new(area));

        foreach (var (id, state) in _avalancheAreas.ToArray())
        {
            var area = state.Area;
            var active = _units.ContainsKey(id) && !area.IsDead && area.IsActive &&
                         now < area.CreationTime.AddSeconds(area.UnitCard?.Lifetime ?? 4) && !HasEnded;
            var radius = area.Key == PermafrostAreaKey ? 4f : 5f;
            // Hero midpoint, same sphere rendered by the client; a grazing capsule does not
            // count as staying inside. No line-of-sight rule, matching the existing slow.
            var inside = active ? _playerUnits.Values.Where(u => !u.IsDead && u.IsActive &&
                RelationshipApplies(u, area, area.Team, RelativeTeamType.Opponent) &&
                Vector3.DistanceSquared(u.GetMidpoint(), area.Transform.Position) <= radius * radius)
                .ToDictionary(u => u.Id) : new Dictionary<uint, Unit>();
            foreach (var (unitId, entry) in state.Inside.ToArray())
            {
                if (inside.ContainsKey(unitId)) continue;
                entry.Unit.RemoveEffects([new ConstEffectInfo(AvalancheAreaSlowKey)], area.Team, state.Source);
                state.Inside.Remove(unitId);
            }
            foreach (var (unitId, unit) in inside)
            {
                var entering = !state.Inside.TryGetValue(unitId, out var entry);
                if (entering)
                {
                    state.Inside.Add(unitId, (unit, now));
                    entry = (unit, now);
                }
                // Reapplying registers this area's source and restores the slow after an
                // immunity/purge expires. Permanent effects deduplicate by key and source.
                unit.AddEffects([new ConstEffectInfo(AvalancheAreaSlowKey)], area.Team, state.Source);
                if (area.Key == PermafrostAreaKey && now - entry.Since >= TimeSpan.FromSeconds(2) &&
                    state.Rooted.Add(unitId))
                    unit.AddEffects([new ConstEffectInfo(PermafrostRootKey)], area.Team, state.Source);
            }
            if (!active) _avalancheAreas.Remove(id);
        }
    }
}
