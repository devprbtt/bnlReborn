using BNLReloadedServer.BaseTypes;
using Moserware.Skills;

namespace BNLReloadedServer.ServerTypes;

internal sealed class MatchParticipationTracker
{
    private readonly Dictionary<uint, CompletedMatchPlayer> _players = [];
    public DateTimeOffset? StartedAt { get; private set; }
    public bool HasParticipant(uint playerId) => _players.ContainsKey(playerId);

    public void Start(DateTimeOffset startedAt, IEnumerable<PlayerLobbyState> initialPlayers,
        IReadOnlyDictionary<uint, Rating> startingRatings)
    {
        if (StartedAt.HasValue) return;
        StartedAt = startedAt;
        foreach (var player in initialPlayers)
            Join(player, startedAt, MatchJoinKind.Initial, false,
                startingRatings.TryGetValue(player.PlayerId, out var rating) ? rating : null);
    }

    public void Join(PlayerLobbyState state, DateTimeOffset at, MatchJoinKind kind, bool backfiller,
        Rating? startingRating = null)
    {
        if (!StartedAt.HasValue) return;
        if (!_players.TryGetValue(state.PlayerId, out var player))
        {
            player = new CompletedMatchPlayer
            {
                PlayerId = state.PlayerId,
                Nickname = state.Nickname ?? string.Empty,
                SquadId = state.SquadId,
                WasInitial = kind == MatchJoinKind.Initial,
                WasBackfiller = backfiller,
                StartingRatingMean = startingRating?.Mean,
                StartingRatingDeviation = startingRating?.StandardDeviation
            };
            _players[state.PlayerId] = player;
        }
        else
        {
            player.WasBackfiller |= backfiller;
            if (player.Presences.LastOrDefault()?.LeftAt is null) return;
        }

        player.Presences.Add(new CompletedMatchPresence
        {
            Sequence = player.Presences.Count,
            TeamSlot = GetTeamSlot(player, state.Team, at),
            JoinedAt = (ulong)at.ToUnixTimeMilliseconds(),
            JoinKind = kind,
            Team = state.Team,
            HeroKey = state.Hero,
            SkinKey = state.SkinKey,
            Devices = new Dictionary<int, Key>(state.Devices ?? []),
            Perks = [.. state.Perks ?? []],
            DeviceLevels = new Dictionary<Key, int>(state.DeviceLevels ?? [])
        });
    }

    public void Leave(uint playerId, DateTimeOffset at, MatchLeaveKind reason)
    {
        var presence = _players.GetValueOrDefault(playerId)?.Presences.LastOrDefault();
        if (presence is null || presence.LeftAt.HasValue) return;
        presence.LeftAt = (ulong)at.ToUnixTimeMilliseconds();
        presence.LeaveKind = reason;
    }

    public void SetResult(uint playerId, bool winner, Dictionary<PlayerMatchStatType, int>? stats, int total,
        Dictionary<ScoreType, float>? rawStats, Dictionary<Key, CompletedMatchDeviceStats>? deviceStats,
        Dictionary<KillAccolade, int>? accolades = null, int bestStreak = 0)
    {
        if (!_players.TryGetValue(playerId, out var player)) return;
        player.IsWinner = winner;
        foreach (var (type, value) in stats ?? []) player.Stats[type] = player.Stats.GetValueOrDefault(type) + value;
        player.TotalScore += total;
        foreach (var (type, value) in rawStats ?? []) player.RawStats[type] = player.RawStats.GetValueOrDefault(type) + value;
        foreach (var (key, value) in deviceStats ?? [])
        {
            if (!player.DeviceStats.TryGetValue(key, out var existing)) player.DeviceStats[key] = existing = new CompletedMatchDeviceStats();
            existing.Placed += value.Placed;
            existing.Destroyed += value.Destroyed;
        }
        if (accolades is not null)
        {
            player.Accolades ??= [];
            foreach (var (accolade, count) in accolades)
                player.Accolades[accolade] = player.Accolades.GetValueOrDefault(accolade) + count;
            player.BestStreak = Math.Max(player.BestStreak, bestStreak);
        }
    }

    public List<CompletedMatchPlayer> Complete(DateTimeOffset endedAt)
    {
        foreach (var player in _players.Values)
        {
            var open = player.Presences.LastOrDefault();
            if (open is not null && !open.LeftAt.HasValue)
            {
                open.LeftAt = (ulong)endedAt.ToUnixTimeMilliseconds();
                open.LeaveKind = MatchLeaveKind.MatchEnded;
            }
        }
        return _players.Values.ToList();
    }

    private int GetTeamSlot(CompletedMatchPlayer player, TeamType team, DateTimeOffset at)
    {
        var previousSlot = player.Presences.LastOrDefault(presence => presence.Team == team)?.TeamSlot;
        if (previousSlot is > 0) return previousSlot.Value;

        var occupiedSlots = _players.Values
            .SelectMany(candidate => candidate.Presences)
            .Where(presence => presence.Team == team && presence.LeftAt is null)
            .Select(presence => presence.TeamSlot)
            .ToHashSet();
        for (var slot = 1; ; slot++)
            if (!occupiedSlots.Contains(slot)) return slot;
    }
}
