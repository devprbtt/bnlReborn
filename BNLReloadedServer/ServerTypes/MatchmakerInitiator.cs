using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using Moserware.Skills;

namespace BNLReloadedServer.ServerTypes;

public class MatchmakerInitiator(CardGameMode gameMode, List<PlayerQueueData> team1, List<PlayerQueueData> team2,
    int playersPerTeam)
    : IGameInitiator
{
    private const double SlotSettleSeconds = 10;

    private readonly List<PlayerQueueData> _team1 = team1.ToList();
    private readonly List<PlayerQueueData> _team2 = team2.ToList();

    private readonly HashSet<uint> _spectators = [];

    private readonly Lock _spectatorsLock = new();

    private bool _backfillReady;

    private DateTimeOffset? _firstSlotFreed;

    public string? GameInstanceId { get; set; }

    public void StartIntoMatch()
    {
    }

    public void ClearInstance(string? instanceId)
    {
    }

    public bool AddPlayer(PlayerQueueData player, TeamType team)
    {
        switch (team)
        {
            case TeamType.Team1:
                if (_team1.Count >= playersPerTeam)
                {
                    return false;
                }
                _team1.Add(player);
                break;
            case TeamType.Team2:
                if (_team2.Count >= playersPerTeam)
                {
                    return false;
                }
                _team2.Add(player);
                break;
            case TeamType.Neutral:
            default:
                return false;
        }

        if (_team1.Count >= playersPerTeam && _team2.Count >= playersPerTeam)
        {
            _firstSlotFreed = null;
        }

        return true;
    }

    public void RemovePlayer(uint playerId)
    {
        lock (_spectatorsLock) _spectators.Remove(playerId);
        if (_team1.RemoveAll(p => p.PlayerId == playerId) + _team2.RemoveAll(p => p.PlayerId == playerId) > 0)
        {
            _firstSlotFreed ??= DateTimeOffset.Now;
        }
    }

    public TeamType GetTeamForPlayer(uint playerId) =>
        _team1.Any(p => p.PlayerId == playerId)
            ? TeamType.Team1
            : _team2.Any(p => p.PlayerId == playerId)
                ? TeamType.Team2
                : TeamType.Neutral;

    public bool IsPlayerSpectator(uint playerId)
    {
        lock (_spectatorsLock) return _spectators.Contains(playerId);
    }

    public bool IsMaxSpectators()
    {
        lock (_spectatorsLock)
            return _spectators.Count >= CatalogueHelper.GlobalLogic.CustomGame!.MaxSpectatorsPerMatch;
    }

    public bool AddSpectator(uint playerId)
    {
        lock (_spectatorsLock)
        {
            if (_spectators.Contains(playerId)) return true;
            if (_spectators.Count >= CatalogueHelper.GlobalLogic.CustomGame!.MaxSpectatorsPerMatch) return false;
            return _spectators.Add(playerId);
        }
    }

    public int PlayerCount => _team1.Count + _team2.Count;

    public int PlayersPerTeam => playersPerTeam;

    public int MaxPlayers => playersPerTeam * 2;

    public bool IsPlayerBackfill(uint playerId) =>
        !(team1.Any(p => p.PlayerId == playerId) || team2.Any(p => p.PlayerId == playerId));

    public Key GetGameMode() => gameMode.Key;

    public bool CanSwitchHero() => false;

    public bool IsThirdPersonForced() => false;

    public bool IsMapEditor() => false;

    public float GetResourceCap() => gameMode.MatchMode.GetCard<CardMatch>()?.ResourceCap ?? 7500;

    public float GetResourceAmount() => gameMode.MatchMode.GetCard<CardMatch>()?.InitResource ?? 2000;

    public long? GetBuildPhaseEndTime(DateTimeOffset startTime) => startTime.AddSeconds((long)(gameMode.MatchMode.GetCard<CardMatch>()?.Data switch
    {
        MatchDataShieldCapture matchDataShieldCapture => matchDataShieldCapture.Build1Time,
        MatchDataShieldRush2 matchDataShieldRush2 => matchDataShieldRush2.Build1Time,
        MatchDataTimeTrial matchDataTimeTrial => matchDataTimeTrial.PrestartTime,
        MatchDataTutorial matchDataTutorial => matchDataTutorial.BuildTime,
        _ => 0
    })).ToUnixTimeMilliseconds();

    public float GetRespawnMultiplier() => 0;

    public bool IsSuperSupplies() => false;

    public bool NeedsBackfill() => (_team1.Count < playersPerTeam || _team2.Count < playersPerTeam) &&
                                   _backfillReady && (_firstSlotFreed is null ||
                                                      (DateTimeOffset.Now - _firstSlotFreed.Value).TotalSeconds >=
                                                      SlotSettleSeconds);

    public void SetBackfillReady(bool backfillReady) => _backfillReady = backfillReady;

    public (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() => (
        _team1.ToDictionary(k => k.PlayerId, v => v.Rating), _team2.ToDictionary(k => k.PlayerId, v => v.Rating));
}
