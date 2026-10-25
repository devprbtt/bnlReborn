using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using SQLite;

public static class PlayerLeaderboardTests
{
    const long Ranked = 11, Casual = 22, Custom = 33, Day = 86_400_000L, Now = 100 * Day;
    const int Min = PlayerStatLeaderboards.MinMatchesPlayed;
    static readonly long[] Modes = [Ranked, Casual];
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    static byte[] Stats(int kills, int assists, int objective, int deaths = 0)
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            w.WriteMap(Enum.GetValues<PlayerMatchStatType>().ToDictionary(k => k, k => k switch
            {
                PlayerMatchStatType.Kill => kills, PlayerMatchStatType.Assist => assists, PlayerMatchStatType.Objective => objective,
                PlayerMatchStatType.Death => deaths, _ => 0
            }), w.WriteByteEnum, w.Write);
        return stream.ToArray();
    }

    static byte[] Raw(float headshots, float damage)
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            w.WriteMap(new Dictionary<ScoreType, float> { { ScoreType.KillPlayerCriticalByHero, headshots }, { ScoreType.KillPlayerByHero, headshots }, { ScoreType.DamagePlayerByHero, damage } },
                w.WriteByteEnum, w.Write);
        return stream.ToArray();
    }

    static int round;
    // One 100-second round for one player.
    static void Round(SQLiteConnection db, uint player, string nickname, long mode, long endedAt, int kills, int assists = 0, int objective = 0,
        float headshots = 0, float damage = 0, Dictionary<KillAccolade, int>? accolades = null, int streak = 0, bool twoHeroes = false,
        int deaths = 0, bool winner = true)
    {
        var id = "r" + round++;
        db.Insert(new ArchivedMatchRecord { Id = id, GameModeKey = mode, StartedAt = endedAt - 100_000, EndedAt = endedAt, Winner = 1 });
        db.Insert(new ArchivedMatchPlayerRecord { MatchId = id, PlayerId = player, Nickname = nickname, IsWinner = winner, Stats = Stats(kills, assists, objective, deaths),
            RawStats = Raw(headshots, damage), Accolades = ArchivedAccolades.Write(accolades, streak) });
        db.Insert(new ArchivedMatchPresenceRecord { MatchId = id, PlayerId = player, HeroKey = 5, Sequence = 0, JoinedAt = endedAt - 100_000, LeftAt = endedAt - 50_000 });
        db.Insert(new ArchivedMatchPresenceRecord { MatchId = id, PlayerId = player, HeroKey = twoHeroes ? 6 : 5, Sequence = 1, JoinedAt = endedAt - 50_000, LeftAt = endedAt });
    }

    // Many identical rounds in one transaction.
    static void Rounds(SQLiteConnection db, int count, Action<int> add) => db.RunInTransaction(() => { for (int i = 0; i < count; i++) add(i); });

    static PlayerLeaderboard Board(SQLiteConnection db, PlayerLeaderboardStat stat, bool recent = false) =>
        PlayerStatLeaderboards.Read(db, stat, recent, Now, Modes, Casual);

    // PlayerRecord ids auto-increment, so Insert ignores the id; move the row to the wanted id afterwards.
    static void Account(SQLiteConnection db, uint id, string name, double rating)
    {
        db.Insert(new PlayerRecord { Username = name, RatingMean = rating, RatingDeviation = 5 });
        db.Execute("UPDATE " + db.GetMapping<PlayerRecord>().TableName + " SET id = ? WHERE username = ?", id, name);
    }

    static SQLiteConnection Open(string path)
    {
        var db = new SQLiteConnection(path);
        db.CreateTable<ArchivedMatchRecord>(); db.CreateTable<ArchivedMatchPlayerRecord>(); db.CreateTable<ArchivedMatchPresenceRecord>(); db.CreateTable<PlayerRecord>();
        return db;
    }

    public static void Run()
    {
        var gate = typeof(BNLReloadedServer.ControlPanel.ControlPanelServer).GetMethod("IsPublicReadRequest", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Check((bool)gate.Invoke(null, ["GET", "/api/leaderboards/players"])!, "stat leaderboards are readable without an admin session");
        Check(!(bool)gate.Invoke(null, ["POST", "/api/leaderboards/players"])!, "stat leaderboards accept no public writes");

        var path = Path.Combine(Path.GetTempPath(), "bnl-leaderboards-" + Guid.NewGuid() + ".db");
        try
        {
            using var db = Open(path);
            Account(db, 1, "Atlas", 30);
            Account(db, 2, "Nova", 60);
            Account(db, 3, "Flint", 35);
            Account(db, 7, "Mira", 41.23);
            Account(db, 8, "Idle", 50);

            // Atlas: 20 recent ranked rounds of 3 kills + 5 old casual rounds of 1 kill = 65 kills in 25 rounds.
            Rounds(db, Min, _ => Round(db, 1, "OldAtlasName", Ranked, Now - Day, kills: 3, assists: 1, objective: 50, headshots: 1, damage: 1000));
            Rounds(db, 5, _ => Round(db, 1, "OldAtlasName", Casual, Now - 40 * Day, kills: 1));
            Rounds(db, Min, _ => Round(db, 2, "OldNovaName", Ranked, Now - Day, kills: 2, assists: 3));
            Rounds(db, Min, _ => Round(db, 7, "Mira", Casual, Now - Day, kills: 2));
            Rounds(db, Min - 1, _ => Round(db, 3, "Flint", Casual, Now - Day, kills: 10));
            Rounds(db, 30, _ => Round(db, 4, "Echo", Custom, Now - Day, kills: 50, assists: 50, objective: 9999));
            Rounds(db, Min, _ => Round(db, 5, "Raven", Ranked, Now - Day, kills: 90, twoHeroes: true));

            var kills = Board(db, PlayerLeaderboardStat.Kills);
            Check(kills.Rows.All(r => r.PlayerId != 4), "custom games never count toward stat leaderboards");
            Check(kills.Rows.All(r => r.PlayerId != 5), "ineligible rounds (mixed heroes) are excluded, as on profiles");
            Check(kills.Rows.All(r => r.PlayerId != 3), "19 counted rounds do not qualify for any board");
            Check(kills.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(1u, 2.6), (2u, 2d), (7u, 2d)]), "kills rank the average per counted match, highest first");
            Check(kills.Rows.Select(r => r.Rank).SequenceEqual([1, 2, 2]), "equal values share a rank");
            Check(kills.Rows[0].Matches == 25 && kills.MinMatches == Min, "rows carry counted matches and the board its minimum");
            Check(kills.Rows[0].Nickname == "Atlas" && kills.Rows[1].Nickname == "Nova", "current account names win over archived nicknames");

            var recent = Board(db, PlayerLeaderboardStat.Kills, recent: true);
            Check(recent.Period == "30d" && recent.Rows.Single(r => r.PlayerId == 1).Value == 3, "the 30-day period drops older rounds before averaging");

            Round(db, 3, "Flint", Casual, Now - Day, kills: 10);
            Check(Board(db, PlayerLeaderboardStat.Kills).Rows.First().PlayerId == 3, "the 20th counted round qualifies a player");
            Check(Board(db, PlayerLeaderboardStat.Headshots).Rows.Single().Value == 0.8, "headshots average the critical-kill counter per match");
            Check(Board(db, PlayerLeaderboardStat.Assists).Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, 3d), (1u, 0.8)]), "assists average per match, without zero rows");
            Check(Board(db, PlayerLeaderboardStat.ObjectiveDamage).Rows.Single().Value == 40, "objective damage averages per match, ignoring custom-game farming");
            Check(Board(db, PlayerLeaderboardStat.AverageDps).Rows.Single().Value == Math.Round(20000 / 2500d, 2), "average DPS stays damage per played second");

            Check(Board(db, PlayerLeaderboardStat.DoubleKills).Rows.Count == 0, "accolade rankings are empty until rounds are tracked");
            Rounds(db, Min, i => Round(db, 2, "Nova", Ranked, Now - Day, kills: 2,
                accolades: i % 2 == 0 ? new() { [KillAccolade.DoubleKill] = 1 } : new(), streak: i == 0 ? 4 : 1));
            Rounds(db, Min - 1, _ => Round(db, 3, "Flint", Casual, Now - Day, kills: 10, accolades: new() { [KillAccolade.DoubleKill] = 2 }, streak: 7));
            var doubles = Board(db, PlayerLeaderboardStat.DoubleKills);
            Check(doubles.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, 0.5)]), "double kills average over tracked rounds and need 20 of them");
            Check(Board(db, PlayerLeaderboardStat.BestStreak).Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(3u, 7d), (2u, 4d)]),
                "best streak stays the record, for players with 20 counted rounds");

            var casual = Board(db, PlayerLeaderboardStat.CasualMmr);
            Check(casual.Rows.Select(r => (r.PlayerId, r.Value, r.Matches)).SequenceEqual([(7u, 4123d, 20L), (3u, 3500d, 39L)]),
                "casual MMR ranks the casual rating in points for players with 20 casual rounds; ranked rounds do not count");
            Check(PlayerStatLeaderboards.Read(db, PlayerLeaderboardStat.CasualMmr, false, Now, Modes).Rows.Count == 0, "without a casual mode nobody qualifies for casual MMR");
            Check(Board(db, PlayerLeaderboardStat.CasualMmr, recent: true).Rows.Count == 2, "casual MMR's 30-day board counts recent casual rounds");

            Rounds(db, 130 * Min, i => Round(db, (uint)(100 + i / Min), "P" + (100 + i / Min), Casual, Now - Day, kills: 100 + i / Min));
            var full = Board(db, PlayerLeaderboardStat.Kills);
            Check(full.Rows.Count == PlayerStatLeaderboards.Limit && full.Players > PlayerStatLeaderboards.Limit, "the top 100 are returned with the qualifying player count");

            var json = JsonSerializer.Serialize(doubles, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Check(json.StartsWith("{\"version\":1,\"stat\":\"double_kills\",\"period\":\"all\"") && json.Contains("\"player_id\":2") && json.Contains("\"min_matches\":20"),
                "public JSON uses the documented names");
            Check(PlayerStatLeaderboards.StatNames.Count == Enum.GetValues<PlayerLeaderboardStat>().Length && PlayerStatLeaderboards.StatNames["casual_mmr"] == PlayerLeaderboardStat.CasualMmr,
                "every stat has a public name, casual_mmr included");
            Check(PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.Kills) && PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.UnbelievableKills) &&
                  !PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.BestStreak) && !PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.Hours) &&
                  !PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.Kdr), "count stats average per match; totals, rates and records do not");
            Check(LeaderboardTrophies.Names.Keys.All(k => LeaderboardTrophies.Description(k).Length <= Trophies.MaxDescriptionLength),
                "every trophy description fits the trophy description limit");
        }
        finally { SQLiteAsyncConnection.ResetPool(); try { File.Delete(path); } catch (IOException) { } }
        Careers();
        TiedLeaders();
    }

    static void TiedLeaders()
    {
        using var db = Open(":memory:");
        Rounds(db, 102 * Min, i => Round(db, (uint)(1 + i / Min), "Tied", Ranked, Now - 40 * Day, kills: 10));
        Rounds(db, Min - 1, _ => Round(db, 500, "Short", Ranked, Now - 40 * Day, kills: 99));
        var all = PlayerStatLeaderboards.ReadAll(db, false, Now, Modes, includeAllLeaders: true, casualModeKey: Casual);
        Check(all[PlayerLeaderboardStat.Kills].Rows.Count == 102 && all[PlayerLeaderboardStat.Kills].Rows.All(r => r.Rank == 1), "trophy snapshot includes tied all-time leaders beyond top 100 and older than 30 days");
        Check(Board(db, PlayerLeaderboardStat.Kills).Rows.Count == 100 && Board(db, PlayerLeaderboardStat.Kills, true).Rows.Count == 0, "public top 100 and 30-day filters stay unchanged");
        Check(all.Count == 17 && all.Values.All(b => b.Rows.All(r => r.PlayerId != 500)), "complete snapshot retains the 20-match threshold on every board");
    }

    // Hours, matches, win rate and KDR, on their own archive so the totals are exact.
    static void Careers()
    {
        var path = Path.Combine(Path.GetTempPath(), "bnl-leaderboards-careers-" + Guid.NewGuid() + ".db");
        try
        {
            using var db = Open(path);
            // 1: 20 rounds, 14 won, 60 kills / 20 deaths. 2: 24 rounds, 12 won, 48 kills, never died. 3: 19 rounds, all won, 627 kills.
            Rounds(db, 20, i => Round(db, 1, "Atlas", Ranked, Now - Day, kills: 3, deaths: 1, winner: i < 14));
            Rounds(db, 24, i => Round(db, 2, "Nova", Casual, Now - Day, kills: 2, winner: i % 2 == 0));
            Rounds(db, 19, _ => Round(db, 3, "Flint", Ranked, Now - Day, kills: 33));
            Round(db, 4, "Echo", Custom, Now - Day, kills: 500);

            var hours = Board(db, PlayerLeaderboardStat.Hours);
            Check(hours.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, Math.Round(2400 / 3600d, 2)), (1u, Math.Round(2000 / 3600d, 2))]),
                "hours stay total played time in counted rounds (100 s each), for players with 20 rounds");
            var matches = Board(db, PlayerLeaderboardStat.Matches);
            Check(matches.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, 24d), (1u, 20d)]) && matches.MinMatches == Min, "matches stay a total and need 20 rounds");
            var wins = Board(db, PlayerLeaderboardStat.WinRate);
            Check(wins.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(1u, 70d), (2u, 50d)]), "win rate is the percent of counted rounds won");
            Check(wins.MinMatches == Min && wins.Rows.All(r => r.PlayerId != 3), "win rate needs 20 rounds: 19 straight wins do not qualify");
            var kdr = Board(db, PlayerLeaderboardStat.Kdr);
            Check(kdr.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, 48d), (1u, 3d)]), "KDR is kills per death, a deathless record dividing by one");
            Check(kdr.MinMatches == Min && kdr.Rows.All(r => r.PlayerId != 3), "KDR needs 20 rounds too");
            Check(PlayerStatLeaderboards.StatNames["hours"] == PlayerLeaderboardStat.Hours && PlayerStatLeaderboards.StatNames["matches"] == PlayerLeaderboardStat.Matches &&
                  PlayerStatLeaderboards.StatNames["win_rate"] == PlayerLeaderboardStat.WinRate && PlayerStatLeaderboards.StatNames["kdr"] == PlayerLeaderboardStat.Kdr,
                "the career stats keep their public names hours, matches, win_rate and kdr");
        }
        finally { SQLiteAsyncConnection.ResetPool(); try { File.Delete(path); } catch (IOException) { } }
    }
}
