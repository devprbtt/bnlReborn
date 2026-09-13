using System.Collections.Concurrent;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using Moserware.Skills;

namespace BNLReloadedServer.ServerTypes;

/// <summary>Shared, unscored free-for-all used while players remain in matchmaking.</summary>
public sealed class WaitingArenaInitiator(CardGameMode gameMode, MapData map) : IGameInitiator
{
    private readonly ConcurrentDictionary<uint, TeamType> _players = new();
    private int _nextTeam;

    public string? GameInstanceId { get; set; }
    public bool IsWaitingArena => true;

    public TeamType AddPlayer(uint playerId)
    {
        return _players.GetOrAdd(playerId, _ =>
            Interlocked.Increment(ref _nextTeam) % 2 == 0 ? TeamType.Team2 : TeamType.Team1);
    }

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
    public float GetResourceCap() => 3000;
    public float GetResourceAmount() => map.Properties?.StartingResources ?? 2000;
    public long? GetBuildPhaseEndTime(DateTimeOffset startTime) => startTime.ToUnixTimeMilliseconds();
    public float GetRespawnMultiplier() => 0;
    public bool IsSuperSupplies() => false;
    public bool NeedsBackfill() => false;
    public void SetBackfillReady(bool backfillReady) { }
    public (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() => ([], []);
}
