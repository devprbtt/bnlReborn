using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ControlPanel;
using BNLReloadedServer.Service;
using BNLReloadedServer.ProtocolHelpers;
using Moserware.Skills;
using SQLite;

namespace BNLReloadedServer.Database;

public class MasterServerDatabase : IMasterServerDatabase
{
    private sealed class SchemaColumn { public string Name { get; set; } = string.Empty; }
    // Region servers register on their session thread and deregister from disconnect teardown,
    // while the control panel and login enumerate the list: every touch goes through the lock,
    // and callers get a copy so they cannot enumerate it while it is being edited.
    private readonly List<RegionInfo> _regionServers = [];
    private readonly Lock _regionServersLock = new();
    private readonly ConcurrentDictionary<string, int> _regionPlayerCounts = new();
    private readonly SQLiteAsyncConnection _playerDb;

    private readonly SemaphoreSlim _asyncLock = new(1, 1);

    public MasterServerDatabase()
    {
        _playerDb = new SQLiteAsyncConnection(Databases.PlayerDatabaseFile);
        _playerDb.CreateTableAsync<PlayerRecord>().Wait();
        _playerDb.CreateTableAsync<PlayerIpRecord>().Wait();
        _playerDb.CreateTableAsync<PlayerPresenceRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchTeamRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchPlayerRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchPlayerDeviceRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchPresenceRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchDeviceRecord>().Wait();
        _playerDb.CreateTableAsync<ArchivedMatchPerkRecord>().Wait();
        EnsureMatchArchiveColumns().Wait();
        _playerDb.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MatchPlayers_Player ON MatchPlayers(player_id)").Wait();
        _playerDb.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MatchPresences_Player ON MatchPresences(player_id)").Wait();
        BackfillRankEligibility().Wait();
    }

    private async Task EnsureMatchArchiveColumns()
    {
        var playerColumns = await _playerDb.QueryAsync<SchemaColumn>("PRAGMA table_info(MatchPlayers)");
        if (!playerColumns.Any(column => column.Name.Equals("starting_rating_mean", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPlayers ADD COLUMN starting_rating_mean REAL");
        if (!playerColumns.Any(column => column.Name.Equals("rating_delta", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPlayers ADD COLUMN rating_delta REAL");
        if (!playerColumns.Any(column => column.Name.Equals("starting_rating_deviation", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPlayers ADD COLUMN starting_rating_deviation REAL");
        if (!playerColumns.Any(column => column.Name.Equals("rating_deviation_delta", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPlayers ADD COLUMN rating_deviation_delta REAL");
        if (!playerColumns.Any(column => column.Name.Equals("raw_stats", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPlayers ADD COLUMN raw_stats BLOB");
        var presenceColumns = await _playerDb.QueryAsync<SchemaColumn>("PRAGMA table_info(MatchPresences)");
        if (!presenceColumns.Any(column => column.Name.Equals("team_slot", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE MatchPresences ADD COLUMN team_slot INTEGER NOT NULL DEFAULT 0");
        var matchColumns = await _playerDb.QueryAsync<SchemaColumn>("PRAGMA table_info(Matches)");
        if (!matchColumns.Any(column => column.Name.Equals("end_reason", StringComparison.OrdinalIgnoreCase)))
            await _playerDb.ExecuteAsync("ALTER TABLE Matches ADD COLUMN end_reason INTEGER NOT NULL DEFAULT 0");
    }

    public async Task StoreCompletedMatch(CompletedMatchRecord match)
    {
        await _asyncLock.WaitAsync();
        try
        {
            // Rating updates are received just before the completed-match message on the same
            // region connection. Sharing their lock makes these values the post-update rating,
            // rather than a timing-dependent later profile read.
            var previousPlayers = await _playerDb.Table<ArchivedMatchPlayerRecord>()
                .Where(player => player.MatchId == match.MatchId).ToListAsync();
            var previousByPlayer = previousPlayers.ToDictionary(player => (uint)player.PlayerId);
            var playerIds = match.Players.Select(player => player.PlayerId).Distinct().ToList();
            var currentRatings = (await _playerDb.Table<PlayerRecord>()
                    .Where(player => playerIds.Contains(player.PlayerId)).ToListAsync())
                .ToDictionary(player => player.PlayerId);
            foreach (var player in match.Players)
            {
                // A retransmission must retain the delta originally recorded for this match;
                // deriving it again after later games would be wrong.
                if (previousByPlayer.GetValueOrDefault(player.PlayerId)?.RatingDelta is double recordedDelta)
                    player.RatingDelta = recordedDelta;
                else if (player.StartingRatingMean is double startingRating &&
                         currentRatings.TryGetValue(player.PlayerId, out var currentRating))
                    player.RatingDelta = currentRating.RatingMean - startingRating;
                if (previousByPlayer.GetValueOrDefault(player.PlayerId)?.RatingDeviationDelta is double recordedDeviationDelta)
                    player.RatingDeviationDelta = recordedDeviationDelta;
                else if (player.StartingRatingDeviation is double startingDeviation &&
                         currentRatings.TryGetValue(player.PlayerId, out var currentDeviationRating))
                    player.RatingDeviationDelta = currentDeviationRating.RatingDeviation - startingDeviation;
            }

            await _playerDb.RunInTransactionAsync(db =>
            {
                // A region can retransmit after losing its master connection. Replacing the complete
                // aggregate makes the operation idempotent without leaving stale child rows behind.
                var oldPresenceIds = db.Table<ArchivedMatchPresenceRecord>()
                    .Where(p => p.MatchId == match.MatchId).Select(p => p.Id).ToList();
                foreach (var presenceId in oldPresenceIds)
                {
                    db.Execute("DELETE FROM MatchPresenceDevices WHERE presence_id = ?", presenceId);
                    db.Execute("DELETE FROM MatchPresencePerks WHERE presence_id = ?", presenceId);
                }
                db.Execute("DELETE FROM MatchPresences WHERE match_id = ?", match.MatchId);
                db.Execute("DELETE FROM MatchPlayers WHERE match_id = ?", match.MatchId);
                db.Execute("DELETE FROM MatchPlayerDevices WHERE match_id = ?", match.MatchId);
                db.Execute("DELETE FROM MatchTeams WHERE match_id = ?", match.MatchId);

                db.InsertOrReplace(new ArchivedMatchRecord
                {
                    Id = match.MatchId,
                    MapKey = match.MapKey.Hash,
                    GameModeKey = match.GameModeKey.Hash,
                    StartedAt = (long)match.StartedAt,
                    EndedAt = (long)match.EndedAt,
                    Winner = (int)match.Winner,
                    EndReason = (int)match.EndReason
                });
                foreach (var team in match.Teams) db.Insert(new ArchivedMatchTeamRecord
                {
                    MatchId = match.MatchId,
                    Team = (int)team.Team,
                    IsWinner = team.IsWinner,
                    CubesAtStart = team.CubesAtStart,
                    CubesRemaining = team.CubesRemaining,
                    BaseDestroyed = team.BaseDestroyed
                });
                foreach (var player in match.Players)
                {
                    using var statStream = new MemoryStream();
                    using (var writer = new BinaryWriter(statStream, System.Text.Encoding.UTF8, true))
                        writer.WriteMap(player.Stats, writer.WriteByteEnum, writer.Write);
                    using var rawStatStream = new MemoryStream();
                    using (var writer = new BinaryWriter(rawStatStream, System.Text.Encoding.UTF8, true))
                        writer.WriteMap(player.RawStats, writer.WriteByteEnum, writer.Write);
                    db.Insert(new ArchivedMatchPlayerRecord
                    {
                        MatchId = match.MatchId,
                        PlayerId = player.PlayerId,
                        Nickname = player.Nickname,
                        SquadId = player.SquadId?.ToString(),
                        WasInitial = player.WasInitial,
                        WasBackfiller = player.WasBackfiller,
                        IsWinner = player.IsWinner,
                        StartingRatingMean = player.StartingRatingMean,
                        StartingRatingDeviation = player.StartingRatingDeviation,
                        RatingDelta = player.RatingDelta,
                        RatingDeviationDelta = player.RatingDeviationDelta,
                        TotalScore = player.TotalScore,
                        Stats = statStream.ToArray(),
                        RawStats = rawStatStream.ToArray()
                    });
                    foreach (var (deviceKey, counts) in player.DeviceStats) db.Insert(new ArchivedMatchPlayerDeviceRecord
                    {
                        MatchId = match.MatchId, PlayerId = player.PlayerId, DeviceKey = deviceKey.Hash,
                        Placed = counts.Placed, Destroyed = counts.Destroyed
                    });
                    foreach (var presence in player.Presences)
                    {
                        var row = new ArchivedMatchPresenceRecord
                        {
                            MatchId = match.MatchId,
                            PlayerId = player.PlayerId,
                            Sequence = presence.Sequence,
                            TeamSlot = presence.TeamSlot,
                            JoinedAt = (long)presence.JoinedAt,
                            LeftAt = (long?)presence.LeftAt,
                            JoinKind = (int)presence.JoinKind,
                            LeaveKind = (int?)presence.LeaveKind,
                            Team = (int)presence.Team,
                            HeroKey = presence.HeroKey.Hash,
                            SkinKey = presence.SkinKey.Hash
                        };
                        db.Insert(row);
                        foreach (var device in presence.Devices) db.Insert(new ArchivedMatchDeviceRecord
                        {
                            PresenceId = row.Id,
                            Slot = device.Key,
                            DeviceKey = device.Value.Hash,
                            DeviceLevel = presence.DeviceLevels.GetValueOrDefault(device.Value)
                        });
                        for (var slot = 0; slot < presence.Perks.Count; slot++) db.Insert(new ArchivedMatchPerkRecord
                        {
                            PresenceId = row.Id,
                            Slot = slot,
                            PerkKey = presence.Perks[slot].Hash
                        });
                    }
                }
            });
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task<LifetimeProfileStats> GetLifetimeProfileStats(uint playerId)
    {
        LifetimeProfileStats result = null!;
        await _playerDb.RunInTransactionAsync(db => result = LifetimeProfileStats.Read(db, playerId));
        return result;
    }

    public async Task<List<ArchivedMatchRecord>> GetCompletedMatches(int limit, long? before)
    {
        var query = _playerDb.Table<ArchivedMatchRecord>();
        if (before.HasValue) query = query.Where(match => match.EndedAt < before.Value);
        return await query.OrderByDescending(match => match.EndedAt).Take(Math.Clamp(limit, 1, 100)).ToListAsync();
    }

    public async Task<ArchivedMatchDetail?> GetCompletedMatch(string matchId)
    {
        var match = await _playerDb.FindAsync<ArchivedMatchRecord>(matchId);
        if (match == null) return null;
        var teams = await _playerDb.Table<ArchivedMatchTeamRecord>().Where(row => row.MatchId == matchId).ToListAsync();
        var players = await _playerDb.Table<ArchivedMatchPlayerRecord>().Where(row => row.MatchId == matchId).ToListAsync();
        var playerDevices = await _playerDb.Table<ArchivedMatchPlayerDeviceRecord>().Where(row => row.MatchId == matchId).ToListAsync();
        var presences = await _playerDb.Table<ArchivedMatchPresenceRecord>().Where(row => row.MatchId == matchId).ToListAsync();
        var devices = new List<ArchivedMatchDeviceRecord>();
        var perks = new List<ArchivedMatchPerkRecord>();
        foreach (var presence in presences)
        {
            devices.AddRange(await _playerDb.Table<ArchivedMatchDeviceRecord>()
                .Where(row => row.PresenceId == presence.Id).ToListAsync());
            perks.AddRange(await _playerDb.Table<ArchivedMatchPerkRecord>()
                .Where(row => row.PresenceId == presence.Id).ToListAsync());
        }
        return new ArchivedMatchDetail(match, teams, players, playerDevices, presences, devices, perks);
    }

    // Ranks are relative, so the ladder is rebuilt whenever a rating or a match count could have
    // moved: after a match, after a control panel edit, and once at startup.
    private async Task RefreshLadder()
    {
        var records = await _playerDb.Table<PlayerRecord>().ToListAsync() ?? [];
        SetLadderFrom(records);
    }

    // rank_eligible_until only starts being written when a player finishes a match, so fill it in
    // from the match history everyone already has, or the whole server reads as unranked until it
    // has played through another five matches.
    private async Task BackfillRankEligibility()
    {
        var records = await _playerDb.Table<PlayerRecord>().ToListAsync() ?? [];
        var changed = new List<PlayerRecord>();
        foreach (var record in records)
        {
            var eligibleUntil = LeagueRanker.EligibleUntil(PlayerData.ReadMatchByteRecord(record.MatchHistory));
            if (eligibleUntil == record.RankEligibleUntil) continue;
            record.RankEligibleUntil = eligibleUntil;
            changed.Add(record);
        }

        if (changed.Count > 0) await _playerDb.UpdateAllAsync(changed);
        SetLadderFrom(records);
    }

    private static void SetLadderFrom(List<PlayerRecord> records) =>
        LeagueRanker.SetLadder(records
            .Where(LeagueRanker.IsOnLadder)
            .Select(r => r.RatingMean));

    public List<RegionInfo> GetRegionServers()
    {
        lock (_regionServersLock) return [.. _regionServers];
    }

    public bool AddRegionServer(string id, string host, RegionGuiInfo regionGuiInfo)
    {
        lock (_regionServersLock)
        {
            if (_regionServers.Any(x => x.Id == id)) return false;
            _regionServers.Add(new RegionInfo
            {
                Id = id,
                Host = host,
                Info = regionGuiInfo,
                Port = 28101
            });
        }

        ControlPanelEvents.Publish(ControlPanelEvent.Status);
        return true;
    }

    public bool RemoveRegionServer(string id)
    {
        _regionPlayerCounts.TryRemove(id, out _);
        bool removed;
        lock (_regionServersLock) removed = _regionServers.RemoveAll(r => r.Id == id) > 0;
        if (removed) ControlPanelEvents.Publish(ControlPanelEvent.Status);
        return removed;
    }

    public bool SetRegionPlayerCount(string id, int playerCount)
    {
        if (GetRegionServer(id) == null)
        {
            if (GetRegionServer("master") == null)
            {
                return false;
            }
            else
            {
                _regionPlayerCounts["master"] = playerCount;
                ControlPanelEvents.Publish(ControlPanelEvent.Status);
                return true;
            }
        }
        _regionPlayerCounts[id] = playerCount;
        ControlPanelEvents.Publish(ControlPanelEvent.Status);
        return true;
    }

    public int GetRegionPlayerCount(string id) => _regionPlayerCounts.GetValueOrDefault(id, 0);

    public RegionInfo? GetRegionServer(string id)
    {
        lock (_regionServersLock) return _regionServers.FirstOrDefault(x => x.Id == id);
    }

    public async Task<PlayerData> AddPlayer(ulong steamId, string playerName, string region)
    {
        var newRecord = new PlayerRecord
        {
            SteamId = steamId,
            Username = playerName,
            PlayerRole = PlayerRole.User,
            Region = region,
            LeagueInfo = null,
            Progression = PlayerProgression.WriteByteRecord(CatalogueHelper.GetDefaultProgression()),
            LookingForFriends = false,
            BadgeInfo = PlayerData.WriteBadgeByteRecord(new Dictionary<BadgeType, List<Key>>()),
            LoadoutData = PlayerData.WriteLoadoutByteRecord(new Dictionary<Key, LobbyLoadout>()),
            HeroStats = PlayerData.WriteStatByteRecord([]),
            MatchHistory = PlayerData.WriteMatchByteRecord([]),
            TimeTrialInfo = TimeTrialData.WriteByteRecord(new TimeTrialData
            {
                BestResultTime = new Dictionary<Key, float>(),
                CompletedGoals = new Dictionary<Key, List<int>>(),
                ResetTime = 0
            })
        };

        var players = await _playerDb.Table<PlayerRecord>().Where(x => x.SteamId == newRecord.SteamId).ToListAsync();
        if (players is { Count: > 0 })
        {
            return await LoadSanitized(players.First());
        }

        await _playerDb.InsertAsync(newRecord);
        return PlayerData.FromPlayerRecord(newRecord);
    }

    public async Task<PlayerData?> GetPlayer(ulong steamId)
    {
        var record = await _playerDb.Table<PlayerRecord>().Where(x => x.SteamId == steamId).FirstOrDefaultAsync();
        return record != null ? await LoadSanitized(record) : null;
    }

    public async Task<PlayerData?> GetPlayer(uint playerId)
    {
        var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
        return record != null ? await LoadSanitized(record) : null;
    }

    private async Task<PlayerData> LoadSanitized(PlayerRecord record)
    {
        var player = PlayerData.FromPlayerRecord(record);
        if (!player.SanitizeAgainstCatalogue()) return player;

        await _asyncLock.WaitAsync();
        try
        {
            await _playerDb.UpdateAsync(player.ToPlayerRecord());
        }
        finally
        {
            _asyncLock.Release();
        }

        return player;
    }

    public async Task RecordPlayerIp(uint playerId, IPAddress? address)
    {
        // A session whose socket died before anyone asked has no address left to attribute.
        if (address == null) return;

        var ip = address.ToString();
        var now = DateTimeOffset.UtcNow;

        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerIpRecord>()
                .Where(x => x.PlayerId == playerId && x.Ip == ip).FirstOrDefaultAsync();

            if (record == null)
            {
                await _playerDb.InsertAsync(new PlayerIpRecord
                {
                    PlayerId = playerId,
                    Ip = ip,
                    FirstSeen = now,
                    LastSeen = now,
                    Hits = 1
                });
            }
            else
            {
                record.LastSeen = now;
                record.Hits += 1;
                await _playerDb.UpdateAsync(record);
            }
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task<DateTimeOffset?> GetLastOnline(uint playerId)
    {
        var record = await _playerDb.FindAsync<PlayerPresenceRecord>(playerId);
        return record?.LastOnline;
    }

    public async Task SaveLastOnline(uint playerId, DateTimeOffset lastOnline)
    {
        await _asyncLock.WaitAsync();
        try
        {
            await _playerDb.InsertOrReplaceAsync(new PlayerPresenceRecord
            {
                PlayerId = playerId,
                LastOnline = lastOnline
            });
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task<List<PlayerIpRecord>> GetIpsForPlayer(uint playerId) =>
        await _playerDb.Table<PlayerIpRecord>().Where(x => x.PlayerId == playerId)
            .OrderByDescending(x => x.LastSeen).ToListAsync();

    public async Task<List<PlayerIpRecord>> GetPlayersForIp(string ip) =>
        await _playerDb.Table<PlayerIpRecord>().Where(x => x.Ip == ip)
            .OrderByDescending(x => x.LastSeen).ToListAsync();

    // Who an address has been before, likeliest first: a connection is anonymous until it logs in,
    // and this is the only thing that can put a name to it in the meantime. An account that never
    // picked a nickname is still worth naming, by id.
    // The limit is applied in the query rather than by the caller: a shared address — a household,
    // a school, a VPN exit — collects a row per account that ever logged in from it, and every one
    // of those ids would otherwise end up in the nickname lookup's IN list.
    public async Task<List<string>> GetNicknamesForIp(string ip, int limit)
    {
        var rows = await _playerDb.Table<PlayerIpRecord>().Where(x => x.Ip == ip)
            .OrderByDescending(x => x.LastSeen).Take(limit).ToListAsync();
        if (rows.Count == 0) return [];

        var found = (await GetSearchResults(rows.Select(row => row.PlayerId).ToList()))
            .ToDictionary(result => result.PlayerId, result => result.Nickname);

        return rows.Select(row =>
            found.TryGetValue(row.PlayerId, out var nickname) && !string.IsNullOrWhiteSpace(nickname)
                ? nickname
                : $"#{row.PlayerId}").ToList();
    }

    public async Task<bool> SetRegionForPlayer(uint playerId, string region)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.Region = region;
            await _playerDb.UpdateAsync(record);
        }
        finally
        {
            _asyncLock.Release();
        }
        return true;
    }

    public async Task<bool> SetUsernameForPlayer(uint playerId, string username)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.Username = username;
            await _playerDb.UpdateAsync(record);
            Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { Nickname = username });
        }
        finally
        {
            _asyncLock.Release();
        }
        return true;
    }

    public async Task<bool> SetLookingForFriendsForPlayer(uint playerId, bool lookingForFriends)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.LookingForFriends = lookingForFriends;

            await _playerDb.UpdateAsync(record);
            Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { LookingForFriends = lookingForFriends });
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetLastPlayedForPlayer(uint playerId, Key hero)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.LastPlayedHero = hero.GetCard<CardUnit>()?.Id;
            await _playerDb.UpdateAsync(record);
            Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { LastPlayedHero = hero });

        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetBadgesForPlayer(uint playerId, Dictionary<BadgeType, List<Key>> badges)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.BadgeInfo = PlayerData.WriteBadgeByteRecord(badges);
            await _playerDb.UpdateAsync(record);
            Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { SelectedBadges = badges });

        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetLoadoutForPlayer(uint playerId, Key hero, LobbyLoadout loadout)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;

            var pData = PlayerData.FromPlayerRecord(record);
            pData.HeroLoadouts[hero] = loadout;
            var newLoadouts = pData.HeroLoadouts;
            record = pData.ToPlayerRecord();

            await _playerDb.UpdateAsync(record);
            Databases.PlayerDatabase.SetLoadout(playerId, newLoadouts);
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    // The region computed this against its own copy of the record, which can lag a write it has
    // not been told about yet, so the goals are merged rather than assigned and the time is kept
    // only if it beats what is already stored.
    private static void ApplyTimeTrialResult(PlayerData player, Key mapKey, TimeTrialResultData result)
    {
        var completedGoals = player.TimeTrial.CompletedGoals ??= new Dictionary<Key, List<int>>();
        var bestTimes = player.TimeTrial.BestResultTime ??= new Dictionary<Key, float>();

        completedGoals[mapKey] = (completedGoals.GetValueOrDefault(mapKey) ?? [])
            .Union(result.NewGoalsCompleted ?? []).Order().ToList();

        if (result.BestResultTime is not { } best) return;
        bestTimes[mapKey] = bestTimes.TryGetValue(mapKey, out var previous) ? MathF.Min(previous, best) : best;
    }

    public async Task<bool> SetNewMatchDataForPlayer(EndMatchResults endMatchResults)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == endMatchResults.PlayerId)
                .FirstOrDefaultAsync();
            if (record == null) return false;

            // Convert record to player data
            var currPlayer = PlayerData.FromPlayerRecord(record);

            // Update hero stats
            var endMatchData = endMatchResults.MatchData;
            var hero = endMatchData.HeroKey;
            var timeTrialResult = endMatchData.TimeTrialData;

            // A solo course is not a match: it keeps its own goal and best time record, and stays
            // out of the hero's played/won tally and the match history.
            if (timeTrialResult != null)
            {
                ApplyTimeTrialResult(currPlayer, endMatchResults.MapKey, timeTrialResult);
            }
            else
            {
                if (currPlayer.HeroStats.All(x => x.Hero != hero))
                {
                    currPlayer.HeroStats.Add(new HeroStats
                    {
                        Hero = hero,
                        TotalMatches = 0,
                        Wins = 0
                    });
                }

                foreach (var stat in currPlayer.HeroStats.FindAll(s => s.Hero == hero))
                {
                    if (endMatchData is not { IsBackfiller: false }) continue;

                    stat.TotalMatches += 1;
                    if (endMatchData is { IsWinner: true })
                    {
                        stat.Wins += 1;
                    }
                }
            }

            // Update progression
            if (endMatchData.OldPlayerXp is not null && endMatchData.RewardXp > 0)
            {
                currPlayer.Progression.PlayerProgress =
                    CatalogueHelper.LeveLUp(endMatchData.OldPlayerXp, endMatchData.RewardXp);
            }

            if (endMatchData.NewHeroXp is not null)
            {
                currPlayer.Progression.HeroesProgress?[hero] = endMatchData.NewHeroXp;
            }

            // Create match history
            var encoder = new UTF8Encoding();
            var currPlayerResults =
                endMatchData.PlayersData?.FirstOrDefault(x => x.PlayerId == endMatchResults.PlayerId);
            var history = timeTrialResult != null ? null : new MatchHistoryRecord
            {
                MatchId = encoder.GetBytes(endMatchResults.GameInstanceId),
                HeroKey = hero,
                SkinKey = endMatchData.SkinKey,
                MapKey = endMatchResults.MapKey,
                GameModeKey = endMatchResults.GameModeKey,
                MatchEndTime = endMatchResults.MatchEndTime,
                MatchSeconds = endMatchData.MatchSeconds,
                IsWinner = endMatchData.IsWinner,
                IsBackfiller = endMatchData.IsBackfiller,
                IsQuit = endMatchData.IsAfk,
                ResourcesEarned = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Earned) ?? 0,
                BlocksBuilt = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Built) ?? 0,
                BlockAssist = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.BlockAssist) ?? 0,
                Destruction = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Destroyed) ?? 0,
                ObjectiveDamage =
                    currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Objective) ?? 0,
                Kill = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Kill) ?? 0,
                Death = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Death) ?? 0,
                Assist = currPlayerResults?.Stats?.Stats?.GetValueOrDefault(PlayerMatchStatType.Assist) ?? 0
            };

            if (history != null)
            {
                currPlayer.MatchHistory = currPlayer.MatchHistory.Prepend(history).Take(10).ToList();
            }

            record = currPlayer.ToPlayerRecord();
            await _playerDb.UpdateAsync(record);
            // This match may have been the fifth one that makes the player rankable, so the ladder
            // has to be rebuilt before their league is read back out of the record.
            await RefreshLadder();
            var league = LeagueRanker.Derive(record);
            Databases.PlayerDatabase.SetHeroStats(currPlayer.PlayerId, currPlayer.HeroStats);
            Databases.PlayerDatabase.UpdatePlayer(currPlayer.PlayerId, new PlayerUpdate
            {
                Progression = currPlayer.Progression,
                League = league,
                TimeTrial = currPlayer.TimeTrial
            });
            Databases.PlayerDatabase.SetMatchHistory(currPlayer.PlayerId, currPlayer.MatchHistory);
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetNewRatings(List<uint> winners, List<uint> losers, HashSet<uint> excluded)
    {
        await _asyncLock.WaitAsync();
        try
        {
            // If there's barely anyone left, just don't bother
            if (winners.Count <= 2 && losers.Count <= 2)
            {
                return true;
            }

            var winnerRecords =
                await _playerDb.Table<PlayerRecord>().Where(x => winners.Contains(x.PlayerId)).ToListAsync() ?? [];
            var loserRecords =
                await _playerDb.Table<PlayerRecord>().Where(x => losers.Contains(x.PlayerId)).ToListAsync() ?? [];

            var winnerPlayers = winnerRecords.Select(PlayerData.FromPlayerRecord).ToList();
            var loserPlayers = loserRecords.Select(PlayerData.FromPlayerRecord).ToList();

            var winnerRatings = winnerPlayers.ToDictionary(k => new Player<uint>(k.PlayerId), v => v.Rating);
            var loserRatings = loserPlayers.ToDictionary(k => new Player<uint>(k.PlayerId), v => v.Rating);

            var newRatings =
                TrueSkillCalculator.CalculateNewRatings(Databases.DefaultGameInfo, [winnerRatings, loserRatings], 1, 2);

            var allPlayers = winnerPlayers.Union(loserPlayers).ToList();
            foreach (var (playerId, rating) in newRatings.ToDictionary(k => k.Key.Id, v => v.Value))
            {
                if (excluded.Contains(playerId))
                {
                    continue;
                }

                var player = allPlayers.FirstOrDefault(x => x.PlayerId == playerId);
                player?.Rating = rating;
            }

            var newRecords = allPlayers.Select(d => d.ToPlayerRecord()).ToList();
            await _playerDb.UpdateAllAsync(newRecords);
            await RefreshLadder();
            var leagues = newRecords.ToDictionary(r => r.PlayerId, LeagueRanker.Derive);
            Databases.PlayerDatabase.SetRatings(allPlayers.ToDictionary(p => p.PlayerId, p => p.Rating));
            foreach (var (playerId, league) in leagues)
                Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { League = league });
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetFriends(uint receiverId, uint senderId, bool accepted)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var recordReceiver = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == receiverId)
                .FirstOrDefaultAsync();
            var recordSender = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == senderId)
                .FirstOrDefaultAsync();
            if (recordReceiver == null || recordSender == null) return false;

            var receiverPlayer = PlayerData.FromPlayerRecord(recordReceiver);
            var senderPlayer = PlayerData.FromPlayerRecord(recordSender);

            receiverPlayer.RequestsFromFriends.Remove(senderId);
            senderPlayer.RequestsFromMe.Remove(receiverId);

            if (accepted)
            {
                if (!receiverPlayer.Friends.Contains(senderId))
                    receiverPlayer.Friends.Add(senderId);
                if (!senderPlayer.Friends.Contains(receiverId))
                    senderPlayer.Friends.Add(receiverId);
            }
            else
            {
                receiverPlayer.Friends.Remove(senderId);
                senderPlayer.Friends.Remove(receiverId);
            }

            recordReceiver = receiverPlayer.ToPlayerRecord();
            recordSender = senderPlayer.ToPlayerRecord();
            List<PlayerRecord> recList = [recordReceiver, recordSender];
            await _playerDb.UpdateAllAsync(recList);
            Databases.PlayerDatabase.SetFriendsInfo(receiverId, receiverPlayer.Friends,
                receiverPlayer.RequestsFromFriends, null);
            Databases.PlayerDatabase.SetFriendsInfo(senderId, senderPlayer.Friends, null,
                senderPlayer.RequestsFromMe);
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }

    public async Task<bool> SetFriendRequest(uint receiverId, uint senderId)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var recordReceiver = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == receiverId)
                .FirstOrDefaultAsync();
            var recordSender = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == senderId)
                .FirstOrDefaultAsync();
            if (recordReceiver == null || recordSender == null) return false;

            var receiverPlayer = PlayerData.FromPlayerRecord(recordReceiver);
            var senderPlayer = PlayerData.FromPlayerRecord(recordSender);

            if (!receiverPlayer.RequestsFromFriends.Contains(senderId))
                receiverPlayer.RequestsFromFriends.Add(senderId);
            if (!senderPlayer.RequestsFromMe.Contains(receiverId))
                senderPlayer.RequestsFromMe.Add(receiverId);

            recordReceiver = receiverPlayer.ToPlayerRecord();
            recordSender = senderPlayer.ToPlayerRecord();
            List<PlayerRecord> recList = [recordReceiver, recordSender];
            await _playerDb.UpdateAllAsync(recList);
            Databases.PlayerDatabase.SetFriendsInfo(receiverId, null, receiverPlayer.RequestsFromFriends, null);
            Databases.PlayerDatabase.SetFriendsInfo(senderId, null, null, senderPlayer.RequestsFromMe);
        }
        finally
        {
            _asyncLock.Release();
        }

        return true;
    }


    public async Task<ProfileData> GetProfileData(uint playerId)
    {
        var player = await GetPlayer(playerId);
        if (player != null)
        {
            return new ProfileData
            {
                Nickname = player.Nickname,
                SteamId = player.SteamId,
                League = player.League,
                Progression = player.Progression,
                MatchHistory = player.MatchHistory,
                HeroStats = player.HeroStats,
                GlobalStats = new GlobalStats(),
                SelectedBadges = player.Badges,
                LookingForFriends = player.LookingForFriends,
                FriendsCount = player.Friends.Count
            };
        }

        return new ProfileData
        {
            MatchHistory = [],
            HeroStats = [],
            LookingForFriends = false,
            FriendsCount = 0
        };
    }

    public async Task<List<SearchResult>> GetSearchResults(string pattern)
    {
        var records = await _playerDb.Table<PlayerRecord>().Where(x => x.Username.StartsWith(pattern)).ToListAsync() ?? [];
        return records.Select(rec => new SearchResult
        {
            PlayerId = rec.PlayerId,
            SteamId = rec.SteamId,
            Nickname = rec.Username
        }).ToList();
    }

    public async Task<List<SearchResult>> GetSearchResults(List<uint> playerIds)
    {
        var records = await _playerDb.Table<PlayerRecord>().Where(x => playerIds.Contains(x.PlayerId)).ToListAsync() ?? [];
        return records.Select(rec => new SearchResult
        {
            PlayerId = rec.PlayerId,
            SteamId = rec.SteamId,
            Nickname = rec.Username
        }).ToList();
    }

    public async Task<List<SearchResult>> GetSearchResults(List<ulong> steamIds)
    {
        var records = await _playerDb.Table<PlayerRecord>().Where(x => steamIds.Contains(x.SteamId)).ToListAsync() ?? [];
        return records.Select(rec => new SearchResult
        {
            PlayerId = rec.PlayerId,
            SteamId = rec.SteamId,
            Nickname = rec.Username
        }).ToList();
    }

    public async Task<List<PlayerData>> GetAllPlayersAsync()
    {
        var records = await _playerDb.Table<PlayerRecord>().ToListAsync();
        return records.Select(PlayerData.FromPlayerRecord).ToList();
    }

    public async Task<bool> UpdatePlayerAsync(uint playerId, PlayerData updated)
    {
        await _asyncLock.WaitAsync();
        try
        {
            var record = await _playerDb.Table<PlayerRecord>().Where(x => x.PlayerId == playerId).FirstOrDefaultAsync();
            if (record == null) return false;
            record.Username = updated.Nickname;
            record.PlayerRole = updated.Role;
            record.Region = updated.Region;
            record.RatingMean = updated.Rating.Mean;
            record.RatingDeviation = updated.Rating.StandardDeviation;
            record.LeagueInfo = updated.League != null ? League.WriteByteRecord(updated.League) : null;
            record.TutorialTokens = updated.TutorialTokens;
            record.LookingForFriends = updated.LookingForFriends;
            record.MatchmakerBanEnd = updated.MatchmakerBanEnd.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.MatchmakerBanEnd.Value) : null;
            record.GraveyardPermanent = updated.GraveyardPermanent;
            record.GraveyardLeaveTime = updated.GraveyardLeaveTime.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.GraveyardLeaveTime.Value) : null;
            await _playerDb.UpdateAsync(record);
            // A rating edited from the control panel moves this player through the ladder. Everyone
            // they passed is left to pick their new rank up on their next match or profile read.
            await RefreshLadder();
            var league = LeagueRanker.Derive(record);
            Databases.PlayerDatabase.UpdatePlayer(playerId, new PlayerUpdate { League = league });
        }
        finally
        {
            _asyncLock.Release();
        }
        return true;
    }

    // Only maps somebody has finished come back; the region fills in the courses nobody has run
    // yet, since it is the side that is certain to have the catalogue.
    public async Task<Dictionary<Key, List<TtLeaderboardRecord>>> GetTimeTrialLeaderboard()
    {
        var leaderboard = new Dictionary<Key, List<TtLeaderboardRecord>>();
        foreach (var record in await _playerDb.Table<PlayerRecord>().ToListAsync() ?? [])
        {
            var player = PlayerData.FromPlayerRecord(record);
            if (player.TimeTrial.BestResultTime is not { } bestTimes) continue;

            foreach (var (mapKey, seconds) in bestTimes)
            {
                if (!leaderboard.TryGetValue(mapKey, out var records))
                {
                    leaderboard[mapKey] = records = [];
                }

                records.Add(new TtLeaderboardRecord
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.Nickname,
                    ResultSeconds = seconds
                });
            }
        }

        foreach (var (mapKey, records) in leaderboard)
        {
            leaderboard[mapKey] = records.OrderBy(r => r.ResultSeconds).Take(100).ToList();
        }

        return leaderboard;
    }

    public async Task<List<LeagueLeaderboardRecord>> GetLeaderboard()
    {
        // Same population the tiers are cut from, so the position shown here is the one a Pro
        // player's badge counts off.
        var records = (await _playerDb.Table<PlayerRecord>().ToListAsync() ?? [])
            .Where(LeagueRanker.IsOnLadder)
            .OrderByDescending(p => p.RatingMean).Take(100)
            .ToList();

        return records.Select((record, idx) =>
        {
            var p = PlayerData.FromPlayerRecord(record);
            return new LeagueLeaderboardRecord
            {
                PlayerId = p.PlayerId,
                SteamId = p.SteamId,
                PlayerName = p.Nickname,
                Points = LeagueRanker.PointsFor(p.Rating.Mean),
                Status = idx + 1,
                Wins = p.HeroStats.Sum(h => h.Wins),
                TotalMatches = p.HeroStats.Sum(h => h.TotalMatches),
                RegistrationTime = default,
                // Region is what the client's last column reads, but it never renders the value
                // anywhere else, so the slot carries the player's rank badge instead.
                Region = LeagueRanker.Label(LeagueRanker.Derive(record))
            };
        }).ToList();
    }
}
