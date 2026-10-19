using BNLReloadedServer.Database;
using SQLite;

public static class LeaderboardTrophyTests
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "bnl-leaderboard-trophies-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            using var db = new SQLiteConnection(Path.Combine(folder, "test.db"));
            Trophies.CreateTables(db); db.CreateTable<PlayerRecord>();
            db.Insert(new PlayerRecord { PlayerId = 1, Username = "First" });
            db.Insert(new PlayerRecord { PlayerId = 2, Username = "Second" });
            LeaderboardTrophies.InstallArt(db, Path.Combine(AppContext.BaseDirectory, "LeaderboardTrophies"), folder, 1);
            Check(Trophies.Catalogue(db).Trophies.Count == 17, "all 17 definitions installed with real shipped art");
            Check(Trophies.Catalogue(db).Trophies.All(t => Trophies.ValidPng(File.ReadAllBytes(Trophies.ImagePath(folder, t.Id)))), "all packaged PNGs meet upload constraints");
            LeaderboardTrophies.InstallArt(db, Path.Combine(AppContext.BaseDirectory, "LeaderboardTrophies"), folder, 2);
            Check(db.Table<TrophyRecord>().ToList().All(t => t.ImageVersion == 1), "restart does not bump unchanged art versions");
            Check(LeaderboardTrophies.Names.Keys.All(k => LeaderboardTrophies.Description(k).Length <= Trophies.MaxDescriptionLength), "all 17 stat meanings fit the catalogue limit");
            Check(LeaderboardTrophies.Description("best_streak").Contains("without dying") && LeaderboardTrophies.Description("double_kills").Contains("8s between kills"), "life streak and rapid-chain trophy meanings are distinct");
            Check(LeaderboardTrophies.Description("kdr").Contains("divided by deaths") && LeaderboardTrophies.Description("win_rate").Contains("percentage") && LeaderboardTrophies.Description("average_dps").Contains("per second"), "rate trophy descriptions explain their measurements");
            var winners = LeaderboardTrophies.Names.Keys.ToDictionary(k => k, _ => new HashSet<uint>());
            winners["kills"] = [1, 2]; winners["best_streak"] = [1]; winners["league"] = [2];
            db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 10));
            Check(Trophies.Collection(db, 1).Trophies.Count == 2 && Trophies.Collection(db, 2).Trophies.Count == 2, "tied leaders and multiple categories receive trophies");
            Check(Trophies.Collection(db, 1).Showcase.Count == 0, "awards do not opt players into banner display");
            var notices = LeaderboardTrophies.Pending(db, 1);
            Check(notices.Count == 2, "offline awards queue until login acknowledgement");
            db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 20));
            Check(LeaderboardTrophies.Pending(db, 1).Select(n => n.GrantId).SequenceEqual(notices.Select(n => n.GrantId)), "repeat reconciliation preserves grant IDs and notification state");
            LeaderboardTrophies.Acknowledge(db, 2, notices.Select(n => n.GrantId).ToArray());
            Check(LeaderboardTrophies.Pending(db, 1).Count == 2, "another player cannot acknowledge your awards");
            LeaderboardTrophies.Acknowledge(db, 1, notices.Select(n => n.GrantId).ToArray());
            Check(LeaderboardTrophies.Pending(db, 1).Count == 0, "acknowledged awards do not notify on next login");
            Check(!LeaderboardTrophies.Acknowledge(db, 1, [-1]), "malformed acknowledgements rejected");
            Trophies.SetShowcase(db, 1, ["leaderboard-kills", "leaderboard-best_streak"]);
            Trophies.Save(db, "event-cup", "Event", "", true, 1);
            Trophies.Grant(db, 1, "event-cup", "admin", "", 2);
            winners["kills"] = [2]; winners["best_streak"] = [];
            db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 30));
            Check(Trophies.Collection(db, 1).Showcase.Count == 0, "lost leadership removes previously pinned trophies");
            Check(Trophies.Collection(db, 1).Trophies.Single().Id == "event-cup", "event awards survive leaderboard transfers");
            Check(LeaderboardTrophies.Pending(db, 2).Count == 2, "continuous leader is not granted a duplicate");
            winners["kills"] = [1];
            db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 40));
            Check(LeaderboardTrophies.Pending(db, 1).Single().GrantId > notices.Max(n => n.GrantId), "regaining leadership generates a new notification");
            Check(LeaderboardTrophies.Pending(db, 2).All(n => n.TrophyId != "leaderboard-kills"), "revoked awards are not announced at later login");
            winners["kills"] = [999];
            db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 50));
            Check(!db.Table<PlayerTrophyRecord>().Any(g => g.TrophyId == "leaderboard-kills"), "deleted accounts cannot receive awards");
            winners.Remove("hours"); bool rejected = false;
            try { db.RunInTransaction(() => LeaderboardTrophies.Reconcile(db, winners, 60)); } catch (ArgumentException) { rejected = true; }
            Check(rejected && Trophies.Collection(db, 2).Trophies.Single().Id == "leaderboard-league", "incomplete snapshots fail before revoking valid ownership");
        }
        finally { Directory.Delete(folder, true); }
    }
}
