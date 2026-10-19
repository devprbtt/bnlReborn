using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using SQLite;

public static class KillAccoladeTests
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    public static void Run()
    {
        Tracker();
        RecordRoundTrip();
        ReconnectMerge();
        Lifetime();
    }

    static void Tracker()
    {
        var t = new KillAccoladeTracker();
        var first = t.Register(victimPlayerId: 20, killerPlayerId: 1, crit: true, nowSeconds: 0);
        Check(first.Accolades.SequenceEqual([KillAccolade.FirstKill, KillAccolade.Headshot]) && first.Streak == 1,
            "first counted kill earns First Kill and a critical kill earns Headshot");
        Check(!t.Register(21, 2, false, 1).Accolades.Contains(KillAccolade.FirstKill), "First Kill is awarded once per match");

        var expected = new[] { KillAccolade.DoubleKill, KillAccolade.MultiKill, KillAccolade.UltraKill, KillAccolade.Fantastic, KillAccolade.Unbelievable, KillAccolade.Unbelievable };
        for (int i = 0; i < expected.Length; i++)
        {
            var kill = t.Register((uint)(30 + i), 1, false, 7.5 * (i + 1));
            Check(kill.Accolades.SequenceEqual([expected[i]]) && kill.Streak == i + 2, "streak kill " + (i + 2) + " earns " + expected[i]);
        }
        Check(t.Register(40, 1, false, 45 + KillAccoladeTracker.StreakWindowSeconds + .01).Streak == 1, "a kill after the 8 s window starts a new streak");
        Check(t.Register(41, 1, false, 100).Accolades.Count == 0, "a lone kill earns nothing");
        Check(t.Register(42, 1, false, 100 + KillAccoladeTracker.StreakWindowSeconds).Accolades.SequenceEqual([KillAccolade.DoubleKill]),
            "a kill exactly at the window edge continues the streak");

        // A death ends the victim's streak, including deaths that are not counted kills.
        var d = new KillAccoladeTracker();
        d.Register(50, 3, false, 0); d.Register(51, 3, false, 1);
        Check(d.Register(3, 9, false, 2).Streak == 1, "the killed player's streak is not credited to their killer");
        Check(d.Register(52, 3, false, 3).Streak == 1, "dying ends your own streak");
        d.Register(53, 3, false, 4);
        Check(d.Register(3, null, false, 5).Accolades.Count == 0, "an uncounted death (fall, suicide, team kill) earns nothing");
        Check(d.Register(54, 3, false, 6).Streak == 1, "an uncounted death still ends the victim's streak");
        Check(d.Register(55, 55, false, 7).Accolades.Count == 0, "a suicide earns nothing");

        // BEST STREAK: kills since the killer last died, however far apart.
        var l = new KillAccoladeTracker();
        Check(l.Register(60, 4, false, 0).LifeStreak == 1, "the first kill of a life is a life streak of 1");
        l.Register(61, 4, false, 30); l.Register(62, 4, false, 90);
        var fourth = l.Register(63, 4, false, 400);
        Check(fourth.LifeStreak == 4 && fourth.Streak == 1, "kills minutes apart keep the life streak (4) while the 8 s chain restarts");
        Check(l.Register(4, 60, false, 401).LifeStreak == 1, "the killer's own killer starts their count; it is not credited the victim's streak");
        Check(l.Register(64, 4, false, 402).LifeStreak == 1, "dying ends your life streak");
        l.Register(65, 4, false, 403); l.Register(4, null, false, 404);
        Check(l.Register(66, 4, false, 405).LifeStreak == 1, "an uncounted death (fall, suicide, team kill) also ends it");
        Check(l.Register(67, null, false, 406).LifeStreak == 0 && l.Register(68, 4, false, 407).LifeStreak == 2,
            "an uncounted kill neither counts nor resets the killer");
        Check(l.Register(69, 69, false, 408).LifeStreak == 0, "a suicide counts nothing");
    }

    static CompletedMatchRecord Match() => new()
    {
        MatchId = "m1", StartedAt = 1000, EndedAt = 2000, Winner = TeamType.Team1, EndReason = MatchEndReason.ObjectivesDestroyed,
        Players =
        [
            new CompletedMatchPlayer
            {
                PlayerId = 7, Nickname = "Atlas", RawStats = new() { [ScoreType.Kills] = 9 },
                DeviceStats = new() { [Key.None] = new CompletedMatchDeviceStats { Placed = 4, Destroyed = 1 } },
                Accolades = new() { [KillAccolade.DoubleKill] = 3, [KillAccolade.UltraKill] = 1 }, BestStreak = 4,
                Presences = [new CompletedMatchPresence { Sequence = 0, TeamSlot = 2 }]
            },
            new CompletedMatchPlayer { PlayerId = 8, Nickname = "Nova", Presences = [new CompletedMatchPresence()] }
        ]
    };

    static byte[] Bytes(CompletedMatchRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) record.Write(writer);
        return stream.ToArray();
    }

    static CompletedMatchRecord Read(byte[] bytes) => CompletedMatchRecord.ReadRecord(new BinaryReader(new MemoryStream(bytes)));

    static void RecordRoundTrip()
    {
        var bytes = Bytes(Match());
        var read = Read(bytes);
        var atlas = read.Players.Single(p => p.PlayerId == 7);
        Check(atlas.Accolades![KillAccolade.DoubleKill] == 3 && atlas.Accolades[KillAccolade.UltraKill] == 1 && atlas.BestStreak == 4,
            "accolade counts and best streak travel from region to master");
        Check(atlas.RawStats[ScoreType.Kills] == 9 && atlas.DeviceStats[Key.None].Placed == 4 && atlas.Presences[0].TeamSlot == 2,
            "existing archive extension fields are unchanged");
        Check(read.Players.Single(p => p.PlayerId == 8).Accolades == null, "a player without accolade data stays untracked");

        // An older region writes no accolade block: everything else must still read, with accolades unknown.
        var legacy = Match(); foreach (var p in legacy.Players) p.Accolades = null;
        var legacyBytes = Bytes(legacy);
        Check(legacyBytes[^2] == 1 && legacyBytes[^1] == 0, "an untracked record ends with the block version and an empty list");
        var withoutBlock = legacyBytes[..^2]; // what a region without accolade tracking sends
        var old = Read(withoutBlock);
        Check(old.Players.All(p => p.Accolades == null) && old.Players.Single(p => p.PlayerId == 7).DeviceStats[Key.None].Placed == 4,
            "a record from an older region (no accolade block) still reads, with accolades unknown");
        Check(Read(legacyBytes).Players.All(p => p.Accolades == null), "an empty accolade block reads as untracked");
    }

    static void ReconnectMerge()
    {
        var tracker = new MatchParticipationTracker();
        tracker.Start(DateTimeOffset.FromUnixTimeMilliseconds(1000),
            [new PlayerLobbyState { PlayerId = 7, Nickname = "Atlas", Team = TeamType.Team1 }, new PlayerLobbyState { PlayerId = 8, Nickname = "Nova", Team = TeamType.Team2 }],
            new Dictionary<uint, Moserware.Skills.Rating>());
        tracker.SetResult(7, false, null, 0, null, null, new() { [KillAccolade.DoubleKill] = 2 }, 3);
        tracker.SetResult(7, true, null, 0, null, null, new() { [KillAccolade.DoubleKill] = 1, [KillAccolade.FirstKill] = 1 }, 2);
        tracker.SetResult(8, true, null, 0, null, null);
        var players = tracker.Complete(DateTimeOffset.FromUnixTimeMilliseconds(2000));
        var atlas = players.Single(p => p.PlayerId == 7);
        Check(atlas.Accolades![KillAccolade.DoubleKill] == 3 && atlas.Accolades[KillAccolade.FirstKill] == 1, "reconnect results add accolade counts");
        Check(atlas.BestStreak == 3, "best streak keeps the longest streak, not the sum");
        Check(players.Single(p => p.PlayerId == 8).Accolades == null, "results without accolade data stay untracked");
    }

    static byte[] Stats(int kills)
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            w.WriteMap(Enum.GetValues<PlayerMatchStatType>().ToDictionary(k => k, k => k == PlayerMatchStatType.Kill ? kills : 0), w.WriteByteEnum, w.Write);
        return stream.ToArray();
    }

    static byte[] Raw()
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            w.WriteMap(new Dictionary<ScoreType, float> { { ScoreType.KillPlayerByHero, 5 } }, w.WriteByteEnum, w.Write);
        return stream.ToArray();
    }

    static void Lifetime()
    {
        var path = Path.Combine(Path.GetTempPath(), "bnl-accolades-" + Guid.NewGuid() + ".db");
        try
        {
            using var db = new SQLiteConnection(path);
            db.CreateTable<ArchivedMatchRecord>(); db.CreateTable<ArchivedMatchPlayerRecord>(); db.CreateTable<ArchivedMatchPresenceRecord>();
            void Round(string id, long started, long hero, byte[]? accolades)
            {
                db.Insert(new ArchivedMatchRecord { Id = id, StartedAt = started, EndedAt = started + 100000, Winner = 1 });
                db.Insert(new ArchivedMatchPlayerRecord { MatchId = id, PlayerId = 3, IsWinner = true, Stats = Stats(6), RawStats = Raw(), Accolades = accolades });
                db.Insert(new ArchivedMatchPresenceRecord { MatchId = id, PlayerId = 3, HeroKey = hero, Sequence = 0, JoinedAt = started, LeftAt = started + 100000 });
            }
            Round("before-tracking", 1000, 123, null);
            Round("tracked-1", 5000, 123, ArchivedAccolades.Write(new() { [KillAccolade.DoubleKill] = 2, [KillAccolade.MultiKill] = 1, [KillAccolade.FirstKill] = 1, [KillAccolade.Headshot] = 4 }, 3));
            Round("tracked-2", 9000, 456, ArchivedAccolades.Write(new() { [KillAccolade.DoubleKill] = 1, [KillAccolade.Unbelievable] = 1 }, 6));
            Round("tracked-empty", 7000, 123, ArchivedAccolades.Write(new(), 0));
            Round("corrupt", 8000, 123, [9, 9]);

            var stats = LifetimeProfileStats.Read(db, 3);
            var all = stats.Overall;
            Check(all.Matches == 5 && all.AccoladeMatches == 3, "only rounds with readable accolade data count toward accolade coverage");
            Check(all.DoubleKills == 3 && all.MultiKills == 1 && all.FirstKills == 1 && all.UnbelievableKills == 1 && all.UltraKills == 0,
                "accolade totals add across rounds");
            Check(all.BestStreak == 6 && all.AccoladeSince == 5000, "best streak is the maximum and tracked-since is the first tracked round");
            var cog = stats.Heroes.Single(h => h.HeroKey == 123).Combat;
            Check(cog.AccoladeMatches == 2 && cog.DoubleKills == 2 && cog.BestStreak == 3, "per-hero accolade totals use that hero's rounds");
            Check(ArchivedAccolades.Read(null) == null && ArchivedAccolades.Read([]) == null && ArchivedAccolades.Read([1, 0, 0]) == null,
                "missing or truncated accolade blobs read as untracked");
            var written = ArchivedAccolades.Write(new() { [KillAccolade.DoubleKill] = 1 }, 7)!;
            Check(written[0] == 2 && ArchivedAccolades.Read(written)?.BestStreak == 7, "new rounds store the life streak as format 2");
            var legacy = (byte[])written.Clone(); legacy[0] = 1;
            Check(ArchivedAccolades.Read(legacy)?.BestStreak == 7, "format 1 rounds (8 s chains, a lower bound) still read");
            Check(ArchivedAccolades.Read([3, 0, 0, 0, 0, 0, 0, 0, 0]) == null, "an unknown future format reads as untracked");

            var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Check(json.StartsWith("{\"version\":2,"), "profile-stats version stays 2 so current clients keep parsing it");
            foreach (var field in new[] { "\"accolade_matches\":3", "\"double_kills\":3", "\"multi_kills\":1", "\"first_kills\":1", "\"unbelievable_kills\":1", "\"best_streak\":6", "\"accolade_since\":5000" })
                Check(json.Contains(field), "public projection includes " + field);
            Check(!json.Contains("headshot\":") && json.Contains("\"headshot_kills\""), "Headshot accolades are not duplicated beside headshot kills");
        }
        finally { File.Delete(path); }
    }
}
