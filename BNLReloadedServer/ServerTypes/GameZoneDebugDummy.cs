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
    private readonly Dictionary<Unit, DebugDummyMotion> _testDummyMotion = new();

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
        // Spawn outside walls at the player's foot height. The normal tick settles it onto terrain.
        var team = player.Team == TeamType.Team1 ? TeamType.Team2 : TeamType.Team1;
        if (!MapBinary.ContainsBlock((Vector3s)position) ||
            !MapBinary.ContainsBlock((Vector3s)(position + Vector3.UnitY * (TeleportPlacement.PlayerHalfHeight * 2))) ||
            !DebugDummyMotion.Fits(MapBinary, team, position)) return null;
        var init = new UnitInit
        {
            Key = DebugDummyRegistration.Key, SkinKey = new Key("skin_sarge_stone_s1"),
            Transform = ZoneTransformHelper.ToZoneTransform(position, player.Transform.GetRotation()),
            Team = team,
            Controlled = false,
            Gears = DebugDummyRegistration.CombatGear.GetCard<CardGear>() is not null
                ? [DebugDummyRegistration.CombatGear] : []
        };
        // No PlayerId/OwnerId: this is a world target, never a connected participant or respawn candidate.
        var dummy = new Unit(NewUnitId(), init, _defaultUnitUpdater);
        dummy.SetDebugDummyHealth(health);
        _testDummyMotion.Add(dummy, new DebugDummyMotion());
        return dummy;
    }

    private void TickTestDummy(Unit dummy)
    {
        if (dummy.IsDead || dummy.IsDropped || !_testDummyMotion.TryGetValue(dummy, out var motion)) return;
        var movement = CatalogueHelper.MovementLogic;
        var pullOrigin = motion.Pull?.OriginPos;
        if (motion.Pull?.OriginUnitId is { } sourceId && _units.TryGetValue(sourceId, out var source))
            pullOrigin = source.Transform.Position;

        var previous = dummy.Transform;
        var position = motion.Step(MapBinary, dummy.Team, previous.Position, SecondsPerTick,
            movement?.JumpAirResistance ?? 0, movement?.GroundFriction ?? 10, pullOrigin,
            out var grounded, out var pad);
        var next = new ZoneTransform { Position = position, Rotation = previous.Rotation, IsJump = !grounded };
        if (motion.SlipYaw is { } yaw) next.Rotation = new Vector3s(0, (int)ZoneTransformHelper.PackToShort(yaw), 0);
        next.SetLocalVelocity(Vector3.Transform(motion.Velocity, Quaternion.Inverse(next.GetRotation())));
        if (!ZoneTransformHelper.CompareTransforms(previous, next))
            dummy.UnitMove(next, Math.Max(dummy.LastMoveUpdateTime + 1, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        if (pad.HasValue)
            _serviceZone.SendBroadcastZoneEvent(new ZoneEventJumpPadUsed { UnitId = dummy.Id, Position = pad.Value });

        if (!dummy.IsDead && motion.LandingHeight > 0)
            ApplyFallDamage(dummy, motion.LandingHeight, false);

        // The usual map plane handles most pits via UnitMoved. Also retire targets that
        // leave a map with no kill plane and no terrain below them.
        if (!dummy.IsDead && position.Y < -32)
            dummy.Killed(dummy.CreateBlankImpactData());
    }
}
