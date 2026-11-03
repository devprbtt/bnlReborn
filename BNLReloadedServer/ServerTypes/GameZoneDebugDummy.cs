using System.Globalization;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    public static bool TryParseDummyHealth(IReadOnlyList<string> args, out int health)
    {
        health = 0;
        return args.Count == 1 && int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out health)
            && health is >= 1 and <= DebugDummyRegistration.MaxHealth;
    }

    // Custom-room limit: one living dummy per spawning team. Admin debug spawns are not limited.
    private readonly Dictionary<TeamType, Unit> _livingTestDummies = new();

    public void ReceivedCustomTestDummy(uint playerId, int health)
    {
        if (_gameInitiator is not CustomGamePlayerGroup custom || !custom.GameInfo.TestDummies ||
            health is < 1 or > DebugDummyRegistration.MaxHealth ||
            !_playerIdToUnitId.TryGetValue(playerId, out var unitId) || !_playerUnits.TryGetValue(unitId, out var player)) return;
        if (_livingTestDummies.TryGetValue(player.Team, out var living) && living is { IsDead: false, IsDropped: false }) return;
        if (SpawnDebugDummy(player, health) is { } dummy) _livingTestDummies[player.Team] = dummy;
    }

    private Unit? SpawnDebugDummy(Unit player, int health)
    {
        if (player.IsDead || !player.IsActive || _zoneData.MatchEnded || player.Team == TeamType.Neutral ||
            Databases.Catalogue.GetCard<CardUnit>(DebugDummyRegistration.Key) == null) return null;
        var forward = Vector3.Transform(Vector3.UnitZ, player.Transform.GetRotation());
        forward.Y = 0;
        forward = forward.LengthSquared() > .001f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        var position = player.Transform.Position + forward * 3;
        // Keep the stationary target outside walls and within the map, at the player's foot height.
        var midpoint = position + Vector3.UnitY * .95f;
        if (!MapBinary.ContainsBlock((Vector3s)position) ||
            !MapBinary.ContainsBlock((Vector3s)(position + Vector3.UnitY * 1.9f)) ||
            !TeleportPlacement.PlayerFits(MapBinary, midpoint)) return null;
        var init = new UnitInit
        {
            Key = DebugDummyRegistration.Key, SkinKey = new Key("skin_sarge_stone_s1"),
            Transform = ZoneTransformHelper.ToZoneTransform(position, player.Transform.GetRotation()),
            Team = player.Team == TeamType.Team1 ? TeamType.Team2 : TeamType.Team1,
            Controlled = true, Gears = []
        };
        // No PlayerId/OwnerId: this is a world target, never a connected participant or respawn candidate.
        var dummy = new Unit(NewUnitId(), init, _defaultUnitUpdater);
        dummy.SetDebugDummyHealth(health);
        return dummy;
    }
}
