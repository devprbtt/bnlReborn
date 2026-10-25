using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>Transferable trophies for the current all-time stat leaders and current season league leader.</summary>
public static class LeaderboardTrophies
{
    public const string Awarder = "leaderboard", Prefix = "leaderboard-";
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["league"] = "League", ["kills"] = "Kills", ["headshots"] = "Headshot Kills",
        ["average_dps"] = "Average DPS", ["assists"] = "Assists", ["objective_damage"] = "Objective Damage",
        ["hours"] = "Hours", ["matches"] = "Matches", ["win_rate"] = "Win Rate", ["kdr"] = "KDR",
        ["first_kills"] = "First Kills", ["double_kills"] = "Double Kills", ["multi_kills"] = "Multi Kills",
        ["ultra_kills"] = "Ultra Kills", ["fantastic_kills"] = "Fantastic Kills",
        ["unbelievable_kills"] = "Unbelievable Kills", ["best_streak"] = "Best Streak"
    };

    public static string Description(string stat)
    {
        if (stat == "league") return "Current season #1: highest placed League rank. Based on Ranked results. Held only while leading this season; transfers when the lead changes.";
        string meaning = stat switch
        {
            "kills" => "highest average credited player kills per match",
            "headshots" => "highest average kills finished with a headshot per match, not headshot hits",
            "average_dps" => "highest player damage from heroes and devices per second played",
            "assists" => "highest average credited assists per match",
            "objective_damage" => "highest average damage dealt to enemy objectives per match",
            "hours" => "most time played in qualifying matches, excluding disconnected time",
            "matches" => "most qualifying matches played, won or lost",
            "win_rate" => "highest percentage of qualifying matches won",
            "kdr" => "highest kills divided by deaths (at least 1 death used as divisor)",
            "first_kills" => "highest average First Kill awards per match, for scoring a match's first counted kill",
            "double_kills" => Chain("Double Kill", "2nd"),
            "multi_kills" => Chain("Multi Kill", "3rd"),
            "ultra_kills" => Chain("Ultra Kill", "4th"),
            "fantastic_kills" => Chain("Fantastic", "5th"),
            "unbelievable_kills" => Chain("Unbelievable", "6th and later"),
            "best_streak" => "most kills in one life without dying, regardless of time between kills",
            _ => throw new ArgumentOutOfRangeException(nameof(stat))
        };
        return "All-time #1: " + meaning + ". Ranked + Casual only. Tied leaders qualify. Transfers when the lead changes.";
    }

    private static string Chain(string name, string ordinal) =>
        "highest average " + name + " awards per match: " + ordinal + " kill in a chain, at most 8s between kills; death ends the chain";

    // Assets ship with the server; import before granting anything, fail closed if the package is incomplete.
    public static void InstallArt(SQLiteConnection db, string assets, string images, long now)
    {
        var files = Names.Keys.ToDictionary(k => k, k => File.ReadAllBytes(Path.Combine(assets, k + ".png")));
        if (files.Values.Any(png => !Trophies.ValidPng(png))) throw new InvalidDataException("Invalid leaderboard trophy art");
        foreach (var (stat, name) in Names)
        {
            string id = Prefix + stat, target = Trophies.ImagePath(images, id);
            var old = db.Find<TrophyRecord>(id);
            bool changed = old == null || !File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(files[stat]);
            if (changed) Trophies.SaveImage(images, id, files[stat]);
            var result = Trophies.Save(db, id, "Top 1 - " + name, Description(stat), changed, now);
            if (result != TrophyResult.Ok) throw new InvalidOperationException("Trophy install failed: " + id + " " + result);
        }
    }

    /// <summary>Caller supplies complete winners from one database snapshot; missing categories are an error.</summary>
    public static void Reconcile(SQLiteConnection db, IReadOnlyDictionary<string, HashSet<uint>> winners, long now)
    {
        if (Names.Keys.Any(k => !winners.ContainsKey(k))) throw new ArgumentException("Incomplete leaderboard snapshot");
        var accounts = db.Table<PlayerRecord>().ToList().Select(p => p.PlayerId).ToHashSet();
        foreach (string stat in Names.Keys)
        {
            string id = Prefix + stat;
            var definition = db.Find<TrophyRecord>(id) ?? throw new InvalidOperationException("Missing trophy " + id);
            var desired = definition.Retired ? new HashSet<uint>() : winners[stat].Where(accounts.Contains).ToHashSet();
            var held = db.Table<PlayerTrophyRecord>().Where(g => g.TrophyId == id).ToList();
            var keep = new HashSet<uint>();
            foreach (var grant in held)
                if (!desired.Contains((uint)grant.PlayerId) || !keep.Add((uint)grant.PlayerId)) Trophies.Revoke(db, grant.Id);
            foreach (uint player in desired.Except(keep))
                if (Trophies.Grant(db, player, id, Awarder, stat == "league" ? "Current season rank 1" : "All-time rank 1", now).Result != TrophyResult.Ok)
                    throw new InvalidOperationException("Leaderboard grant failed");
        }
    }

    public sealed record Notice(long GrantId, string TrophyId, string Name);
    public static List<Notice> Pending(SQLiteConnection db, uint player)
    {
        long id = player;
        var names = db.Table<TrophyRecord>().ToList().ToDictionary(t => t.Id, t => t.Name);
        return db.Table<PlayerTrophyRecord>().Where(g => g.PlayerId == id && !g.Notified && g.GrantedBy == Awarder)
            .ToList().OrderBy(g => g.Id).Take(17)
            .Select(g => new Notice(g.Id, g.TrophyId, names.GetValueOrDefault(g.TrophyId, g.TrophyId))).ToList();
    }

    public static bool Acknowledge(SQLiteConnection db, uint player, IReadOnlyCollection<long> grants)
    {
        if (grants.Count > 17 || grants.Any(id => id <= 0)) return false;
        foreach (long id in grants.Distinct())
            db.Execute("UPDATE PlayerTrophies SET notified = 1 WHERE id = ? AND player_id = ? AND granted_by = ?", id, (long)player, Awarder);
        return true;
    }
}
