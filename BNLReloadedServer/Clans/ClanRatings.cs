using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;

namespace BNLReloadedServer.Clans;

public static class ClanRatings
{
    public const int InitialMmr = 1000;
    private static readonly object Gate = new();
    private static SQLiteConnection? database;
    private static SQLiteConnection Db
    {
        get
        {
            if (database != null) return database;
            database = new SQLiteConnection(Databases.PlayerDatabaseFile);
            database.BusyTimeout = TimeSpan.FromSeconds(5);
            database.CreateTable<ClanRatingRecord>(); database.CreateTable<ClanRatedMatch>(); return database;
        }
    }
    public static int Delta(int winner, int loser) => Math.Max(1, (int)Math.Round(32 * (1 - 1 / (1 + Math.Pow(10, (loser - winner) / 400.0)))));
    public static void Record(string match, int home, int away, TeamType winner)
    {
        if (winner is not (TeamType.Team1 or TeamType.Team2) || home == away) return;
        lock (Gate) Db.RunInTransaction(() => {
            if (Db.Find<ClanRatedMatch>(match) != null) return;
            var a = Db.Find<ClanRatingRecord>(home) ?? new ClanRatingRecord { ClanId = home };
            var b = Db.Find<ClanRatingRecord>(away) ?? new ClanRatingRecord { ClanId = away };
            var win = winner == TeamType.Team1 ? a : b; var loss = winner == TeamType.Team1 ? b : a;
            int delta = Math.Min(loss.Mmr, Delta(win.Mmr, loss.Mmr));
            win.Mmr += delta; loss.Mmr -= delta; win.Wins++; loss.Losses++;
            Db.InsertOrReplace(a); Db.InsertOrReplace(b); Db.Insert(new ClanRatedMatch { Id = match });
        });
    }
    public static string Leaderboard()
    {
        lock (Gate)
        {
            var ratings = Db.Table<ClanRatingRecord>().ToList().ToDictionary(r => r.ClanId);
            var rows = ClanHub.Store.AllClans().Select(c => {
                var r = ratings.GetValueOrDefault(c.Id) ?? new ClanRatingRecord { ClanId = c.Id };
                return new { id = c.Id, name = c.Name, tag = c.Tag, tagColor = c.TagColor, memberCount = c.Members.Length, mmr = r.Mmr,
                    totalMatches = r.Wins + r.Losses, wins = r.Wins, losses = r.Losses,
                    winRate = r.Wins + r.Losses == 0 ? 0 : 100.0 * r.Wins / (r.Wins + r.Losses) };
            }).OrderByDescending(c => c.mmr).ThenByDescending(c => c.wins).ThenBy(c => c.id).Take(100).ToArray();
            return JsonSerializer.Serialize(new { rows });
        }
    }
}
[Table("ClanRatings")]
public sealed class ClanRatingRecord
{
    [PrimaryKey] public int ClanId { get; set; }
    public int Mmr { get; set; } = ClanRatings.InitialMmr;
    public int Wins { get; set; }
    public int Losses { get; set; }
}
[Table("ClanRatedMatches")]
public sealed class ClanRatedMatch { [PrimaryKey] public string Id { get; set; } = ""; }
