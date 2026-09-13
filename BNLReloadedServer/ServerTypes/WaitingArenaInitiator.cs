using System.Collections.Concurrent;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using Moserware.Skills;

namespace BNLReloadedServer.ServerTypes;

/// <summary>Shared, unscored free-for-all used while players remain in matchmaking.</summary>
public sealed class WaitingArenaInitiator(CardGameMode gameMode, MapData map) : IGameInitiator
{
    private const float SpawnNoBuildRadius = 6f;
    private readonly ConcurrentDictionary<uint, TeamType> _players = new();

    public string? GameInstanceId { get; set; }
    public bool IsWaitingArena => true;

    public TeamType AddPlayer(uint playerId) => _players.GetOrAdd(playerId, TeamType.Neutral);

    public void RemovePlayer(uint playerId) => _players.TryRemove(playerId, out _);
    public int PlayerCount => _players.Count;
    public void StartIntoMatch() { }
    public void ClearInstance(string? instanceId) { }
    public TeamType GetTeamForPlayer(uint playerId) => _players.GetValueOrDefault(playerId, TeamType.Neutral);
    public bool IsPlayerSpectator(uint playerId) => false;
    public bool IsPlayerBackfill(uint playerId) => false;
    public Key GetGameMode() => gameMode.Key;
    public bool CanSwitchHero() => true;
    public bool IsMapEditor() => false;
    public bool IsThirdPersonForced() => false;
    public float GetResourceCap() => 1000;
    public float GetResourceAmount() => 1000;
    public long? GetBuildPhaseEndTime(DateTimeOffset startTime) => startTime.ToUnixTimeMilliseconds();
    public float GetRespawnMultiplier() => 0;
    public bool IsSuperSupplies() => false;
    public bool NeedsBackfill() => false;
    public void SetBackfillReady(bool backfillReady) { }
    public (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() => ([], []);

    public bool IsInSpawnNoBuildZone(Vector3s position)
    {
        var radiusSquared = SpawnNoBuildRadius * SpawnNoBuildRadius;
        return map.SpawnPoints.Any(spawn =>
        {
            var deltaX = position.x + 0.5f - spawn.Position.X;
            var deltaZ = position.z + 0.5f - spawn.Position.Z;
            return deltaX * deltaX + deltaZ * deltaZ <= radiusSquared;
        });
    }
}
