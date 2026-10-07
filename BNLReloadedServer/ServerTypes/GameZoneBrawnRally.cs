using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    // Brawn I is the first implemented mandatory class perk. Other choices are not enabled yet.
    // High-bit spawn IDs distinguish mobile rally points without changing the existing wire record.
    private readonly Dictionary<uint, uint> _rallyPlayers = new(); // spawn ID -> player ID
    private readonly Dictionary<uint, long> _rallyCombatUntil = new();
    private Func<long> _rallyClock = () => Environment.TickCount64;
    private long _nextRallyRefresh;
    private const long RallyCombatMilliseconds = 5000;

    private bool HasBrawnRally(Unit unit) => _gameInitiator is not WaitingArenaInitiator &&
        unit.PlayerId.HasValue && unit.UnitCard?.Data is UnitDataPlayer data &&
        data.Class.GetCard<CardHeroClass>()?.Type == HeroClassType.Brawn;

    private void MarkRallyCombat(Unit unit)
    {
        if (!HasBrawnRally(unit)) return;
        _rallyCombatUntil[unit.PlayerId!.Value] = _rallyClock() + RallyCombatMilliseconds;
        _nextRallyRefresh = 0;
    }

    private void RefreshRallySpawns()
    {
        long now = _rallyClock();
        if (now < _nextRallyRefresh) return;
        _nextRallyRefresh = now + 100;
        foreach (var unit in _playerUnits.Values)
        {
            if (!HasBrawnRally(unit) || _rallyPlayers.ContainsValue(unit.PlayerId!.Value)) continue;
            _rallyPlayers.Add(NewSpawnId() | 0x80000000u, unit.PlayerId.Value);
        }
        foreach (var (spawnId, playerId) in _rallyPlayers)
        {
            var host = GetPlayerFromPlayerId(playerId);
            var state = host == null ? SpawnPointLockType.ServerBlocked : RallyState(host, null, out _);
            _zoneData.UpdateSpawn(spawnId, state, playerId, true, host?.Transform.Position, host?.Team);
        }
    }

    private SpawnPointLockType RallyState(Unit host, Unit? guest, out Vector3 position)
    {
        position = default;
        if (!HasBrawnRally(host) || host.IsDead || !host.IsActive || host.IsDropped ||
            host.Team == TeamType.Neutral || host.IsBuff(BuffType.Disabled) ||
            (guest != null && (guest.PlayerId == host.PlayerId || guest.Team != host.Team || AreOpponents(guest, host))) ||
            _rallyClock() < _rallyCombatUntil.GetValueOrDefault(host.PlayerId!.Value) ||
            host.CurrentChannelData != null || host.Transform.IsJump || host.Transform.IsWallClimb ||
            host.Transform.IsDash || host.Transform.IsGroundSlam ||
            MathF.Abs(host.Transform.GetLocalVelocity().Y) > .1f)
            return SpawnPointLockType.ServerBlocked;

        var feet = host.Transform.Position;
        if (!RallySupported(feet)) return SpawnPointLockType.ServerBlocked;
        // Eight nearby spots, standing clearance for the arriving hero, and a clear path
        // from the carrier prevent spawning through a wall, on a ledge, or inside a player.
        bool playerBlocked = false;
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ,
                     new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(1,0,-1), new Vector3(-1,0,-1) })
        {
            var offset = Vector3.Normalize(direction) * 1.15f;
            var candidate = feet + offset;
            if (!RallySupported(candidate)) continue;
            bool clear = true;
            for (int step = 1; step <= 4 && clear; step++) clear = RallyWorldClear(feet + offset * (step / 4f));
            if (!clear) continue;
            var box = new BoundingBoxEx(candidate + new Vector3(0, .95f, 0), new Vector3(.9f, 1.88f, .9f));
            var occupants = _unitOctree.GetColliding(box).Where(u => !u.IsDead && u.IsActive).ToArray();
            if (occupants.Length != 0)
            { playerBlocked |= occupants.Any(u => u.PlayerId.HasValue); continue; }
            position = candidate;
            return SpawnPointLockType.Free;
        }
        return playerBlocked ? SpawnPointLockType.PlayerBlocked : SpawnPointLockType.WorldBlocked;
    }

    private bool RallySupported(Vector3 feet)
    {
        if (!float.IsFinite(feet.X) || !float.IsFinite(feet.Y) || !float.IsFinite(feet.Z) ||
            MathF.Abs(feet.Y - MathF.Round(feet.Y)) > .15f) return false;
        foreach (float x in new[] { -.4f, .4f }) foreach (float z in new[] { -.4f, .4f })
        {
            var cell = new Vector3s(MathF.Floor(feet.X + x), MathF.Floor(feet.Y - .16f), MathF.Floor(feet.Z + z));
            if (!MapBinary.ContainsBlock(cell) || MapBinary[cell].Card.Passable != BlockPassableType.None ||
                !MapBinary[cell].Card.Solid || MapBinary[cell].IsPassable) return false;
        }
        return true;
    }

    private bool RallyWorldClear(Vector3 feet)
    {
        for (int x = (int)MathF.Floor(feet.X - .44f); x <= (int)MathF.Floor(feet.X + .44f); x++)
        for (int y = (int)MathF.Floor(feet.Y + .02f); y <= (int)MathF.Floor(feet.Y + 1.9f); y++)
        for (int z = (int)MathF.Floor(feet.Z - .44f); z <= (int)MathF.Floor(feet.Z + .44f); z++)
        {
            var cell = new Vector3s(x, y, z);
            if (!MapBinary.ContainsBlock(cell) || MapBinary[cell].Card.Passable != BlockPassableType.Any) return false;
        }
        return true;
    }

    private bool TryRallyRespawn(Unit guest, uint spawnId)
    {
        if (!_rallyPlayers.TryGetValue(spawnId, out var playerId)) return false;
        var host = GetPlayerFromPlayerId(playerId);
        if (host == null || RallyState(host, guest, out var position) != SpawnPointLockType.Free) return false;
        if (!guest.Respawn(position, Quaternion.Identity, host.Transform.Rotation)) return false;
        _zoneData.UpdateSpawnTime(guest.PlayerId!.Value, null);
        // Respawn inserts its body into the octree synchronously. The next guest is checked again.
        _nextRallyRefresh = 0;
        return true;
    }
}
