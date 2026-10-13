using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using SQLite;

public static class PlayerLeaderboardTests
{
    const long Ranked = 11, Casual = 22, Custom = 33, Day = 86_400_000L, Now = 100 * Day;
    static readonly long[] Modes = [Ranked, Casual];
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    static byte[] Stats(int kills, int assists, int objective)
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            w.WriteMap(Enum.GetValues<PlayerMatchStatType>().ToDictionary(k => k, k => k switch
            {
                PlayerMatchStatType.Kill => kills, PlayerMatchStatType.Assist => assists, PlayerMatchStatType.Objective => objective, _ => 0
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
        float headshots = 0, float damage = 0, Dictionary<KillAccolade, int>? accolades = null, int streak = 0, bool twoHeroes = false)
    {
        var id = "r" + round++;
        db.Insert(new ArchivedMatchRecord { Id = id, GameModeKey = mode, StartedAt = endedAt - 100_000, EndedAt = endedAt, Winner = 1 });
        db.Insert(new ArchivedMatchPlayerRecord { MatchId = id, PlayerId = player, Nickname = nickname, IsWinner = true, Stats = Stats(kills, assists, objective),
            RawStats = Raw(headshots, damage), Accolades = ArchivedAccolades.Write(accolades, streak) });
        db.Insert(new ArchivedMatchPresenceRecord { MatchId = id, PlayerId = player, HeroKey = 5, Sequence = 0, JoinedAt = endedAt - 100_000, LeftAt = endedAt - 50_000 });
        db.Insert(new ArchivedMatchPresenceRecord { MatchId = id, PlayerId = player, HeroKey = twoHeroes ? 6 : 5, Sequence = 1, JoinedAt = endedAt - 50_000, LeftAt = endedAt });
    }

    static PlayerLeaderboard Board(SQLiteConnection db, PlayerLeaderboardStat stat, bool recent = false) =>
        PlayerStatLeaderboards.Read(db, stat, recent, Now, Modes);

    public static void Run()
    {
        var gate = typeof(BNLReloadedServer.ControlPanel.ControlPanelServer).GetMethod("IsPublicReadRequest", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Check((bool)gate.Invoke(null, ["GET", "/api/leaderboards/players"])!, "stat leaderboards are readable without an admin session");
        Check(!(bool)gate.Invoke(null, ["POST", "/api/leaderboards/players"])!, "stat leaderboards accept no public writes");

        var path = Path.Combine(Path.GetTempPath(), "bnl-leaderboards-" + Guid.NewGuid() + ".db");
        try
        {
            using var db = new SQLiteConnection(path);
            db.CreateTable<ArchivedMatchRecord>(); db.CreateTable<ArchivedMatchPlayerRecord>(); db.CreateTable<ArchivedMatchPresenceRecord>(); db.CreateTable<PlayerRecord>();
            db.Insert(new PlayerRecord { PlayerId = 1, Username = "Atlas" });

            Round(db, 1, "OldAtlasName", Ranked, Now - Day, kills: 20, assists: 3, objective: 500, headshots: 4, damage: 2000);
            Round(db, 1, "OldAtlasName", Casual, Now - 40 * Day, kills: 10, headshots: 2, damage: 1000);
            Round(db, 2, "Nova", Ranked, Now - Day, kills: 25, assists: 9);
            Round(db, 3, "Flint", Casual, Now - Day, kills: 25, assists: 9);
            Round(db, 3, "Flint", Casual, Now - 2 * Day, kills: 0);
            Round(db, 4, "Echo", Custom, Now - Day, kills: 500, assists: 500, objective: 99999);
            Round(db, 5, "Raven", Ranked, Now - Day, kills: 90, twoHeroes: true);

            var kills = Board(db, PlayerLeaderboardStat.Kills);
            Check(kills.Rows.All(r => r.PlayerId != 4), "custom games never count toward stat leaderboards");
            Check(kills.Rows.All(r => r.PlayerId != 5), "ineligible rounds (mixed heroes) are excluded, as on profiles");
            Check(kills.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(1u, 30d), (2u, 25d), (3u, 25d)]), "kills rank all-time totals, highest first");
            Check(kills.Rows.Select(r => r.Rank).SequenceEqual([1, 2, 2]), "equal values share a rank");
            Check(kills.Rows[1].Matches < kills.Rows[2].Matches, "ties list the player with fewer matches first");
            Check(kills.Rows[0].Nickname == "Atlas" && kills.Rows[1].Nickname == "Nova", "current account names win over archived nicknames");

            var recent = Board(db, PlayerLeaderboardStat.Kills, recent: true);
            Check(recent.Period == "30d" && recent.Rows.Single(r => r.PlayerId == 1).Value == 20, "the 30-day period drops older rounds");
            Check(Board(db, PlayerLeaderboardStat.Headshots).Rows.Single().Value == 6, "headshots rank the server's critical-kill counter");
            Check(Board(db, PlayerLeaderboardStat.Assists).Rows.Select(r => r.PlayerId).SequenceEqual([2u, 3u, 1u]), "assists rank separately");
            Check(Board(db, PlayerLeaderboardStat.ObjectiveDamage).Rows.Single().PlayerId == 1, "objective damage ranks, ignoring custom-game farming");

            var dps = Board(db, PlayerLeaderboardStat.AverageDps);
            Check(dps.Rows.Count == 0 && dps.MinMatches == PlayerStatLeaderboards.MinDpsMatches, "average DPS needs ten rounds before a player qualifies");
            for (int i = 0; i < 9; i++) Round(db, 1, "Atlas", Ranked, Now - Day, kills: 1, damage: 1000);
            dps = Board(db, PlayerLeaderboardStat.AverageDps);
            Check(dps.Rows.Single().Value == Math.Round(12000 / 1100d, 2), "average DPS divides damage by played seconds over all qualifying rounds");

            Check(Board(db, PlayerLeaderboardStat.DoubleKills).Rows.Count == 0, "accolade rankings are empty until rounds are tracked");
            Round(db, 2, "Nova", Ranked, Now - Day, kills: 6, accolades: new() { [KillAccolade.DoubleKill] = 3, [KillAccolade.UltraKill] = 1 }, streak: 4);
            Round(db, 3, "Flint", Ranked, Now - Day, kills: 2, accolades: new() { [KillAccolade.DoubleKill] = 1 }, streak: 2);
            Round(db, 6, "Ash", Ranked, Now - Day, kills: 0, accolades: new(), streak: 0);
            var doubles = Board(db, PlayerLeaderboardStat.DoubleKills);
            Check(doubles.Rows.Select(r => (r.PlayerId, r.Value)).SequenceEqual([(2u, 3d), (3u, 1d)]), "double kills rank tracked rounds, without zero rows");
            Check(Board(db, PlayerLeaderboardStat.BestStreak).Rows.First().Value == 4 && Board(db, PlayerLeaderboardStat.UltraKills).Rows.Single().PlayerId == 2,
                "best streak and each accolade have their own ranking");

            for (uint p = 100; p < 230; p++) Round(db, p, "P" + p, Casual, Now - Day, kills: (int)p);
            var full = Board(db, PlayerLeaderboardStat.Kills);
            Check(full.Rows.Count == PlayerStatLeaderboards.Limit && full.Players > PlayerStatLeaderboards.Limit, "the top 100 are returned with the qualifying player count");

            var json = JsonSerializer.Serialize(doubles, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Check(json.StartsWith("{\"version\":1,\"stat\":\"double_kills\",\"period\":\"all\"") && json.Contains("\"player_id\":2") && json.Contains("\"min_matches\":1"),
                "public JSON uses the documented names");
            Check(PlayerStatLeaderboards.StatNames.Count == Enum.GetValues<PlayerLeaderboardStat>().Length, "every stat has a public name");
        }
        finally { File.Delete(path); }
    }
}
