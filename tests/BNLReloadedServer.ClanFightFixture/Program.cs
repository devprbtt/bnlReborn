using BNLReloadedServer.Clans;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;

// Dedicated temporary database; never touch a developer or live player database.
var temp = Path.Combine(Path.GetTempPath(), "bnl-clan-fight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(temp,"PlayerData")); Directory.SetCurrentDirectory(temp);

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
ClanFightRoom Room(int size = 4)
{
    var r = new ClanFightRoom { HomeClan = 10, Leader = 1 };
    for (uint i = 1; i <= size; i++) Check(r.Join(i,10), "home join");
    for (uint i = 11; i < 11+size; i++) Check(r.Join(i,20), "away join");
    r.Maps.AddRange([101,102,103,104,105]); r.Heroes.AddRange([201,202,203,204]);
    return r;
}
foreach (int size in new[] {4,5})
{
    var r = Room(size);
    Check(!r.CanStart, "unready gate"); Check(!r.Join(99,30), "third clan rejected");
    Check(!r.Join(99,0), "clanless rejected"); Check(!r.Join(1,10), "duplicate rejected");
    foreach (var p in r.Players) p.Ready = true;
    Check(r.CanStart, "balanced ready room can start"); Check(!r.Start(11,0), "opponent cannot start");
    Check(r.Start(1,0), "leader starts"); Check(r.Captain == 11, "opponent bans first");
    Check(!r.Ban(1,101,1), "wrong captain rejected"); Check(!r.Ban(11,999,1), "unknown map rejected");
    Check(!r.Join(99,10), "late join rejected"); Check(r.Ban(11,101,1), "first map ban");
    Check(!r.Ban(1,101,2), "duplicate map ban rejected"); Check(r.Ban(1,102,2), "second map ban");
    Check(!r.Tick(10001), "timer not early"); Check(r.Tick(10002), "timeout bans automatically");
    Check(r.BannedMaps.Count == 3 && r.Captain == 1, "timeout changes turn once");
    Check(!r.Tick(10002), "duplicate timer does not advance"); Check(r.Ban(1,104,10003), "last map ban");
    Check(r.Map == 105 && r.Phase == "heroes" && r.Captain == 1, "single map remains; creator starts heroes");
    Check(r.Ban(1,201,10004), "first hero ban"); Check(!r.Ban(11,201,10005), "duplicate hero rejected");
    Check(r.Tick(20004), "hero timeout"); Check(r.Phase == "draft" && r.BannedHeroes.Count == 2, "two bans enter draft");
    Check(!r.Tick(90000), "draft has no ban timer");
}
foreach (var phase in new[]{"maps","heroes","draft"})
{
    var locked=Room(); locked.Phase=phase; locked.Deadline=12345; locked.BannedMaps.Add(101);
    foreach (uint player in new uint[]{1,2,11,12})
        Check(!locked.TryLeave(player),phase+" rejects voluntary departure for captains and members");
    Check(locked.Players.Count==8 && locked.Leader==1 && locked.OpponentCaptain==11 && locked.Phase==phase && locked.Deadline==12345 && locked.BannedMaps.SequenceEqual(new uint[]{101}),phase+" leaves roster and ban progress intact");
}
var open=Room();Check(open.TryLeave(2) && open.Players.Count==7,"voluntary leave remains available before bans");
var five = Room(5); Check(!five.Join(6,10), "team cap");
five.Players.ForEach(p => p.Ready = true); five.Leave(15); Check(!five.CanStart,"5v4 cannot start");
var four = Room(); four.Players.ForEach(p => p.Ready = true); Check(four.Join(5,10), "fifth member joins");
Check(four.Players.All(p => !p.Ready),"roster change resets readiness");
four.Leave(1); Check(four.Leader == 2,"leader passes to clanmate");
four.Leave(11); Check(four.OpponentCaptain == 12,"opponent captain passes to clanmate");
var cancel = Room(); cancel.Players.ForEach(p => p.Ready = true); cancel.Start(1,0); cancel.Ban(11,101,1); cancel.Leave(12);
Check(cancel.Phase == "room" && cancel.BannedMaps.Count == 0 && cancel.Players.All(p => !p.Ready),"disconnect cancels bans and readiness");
var single = Room(); single.Maps.Clear(); single.Maps.Add(101); single.Players.ForEach(p=>p.Ready=true);
Check(single.Start(1,0) && single.Phase == "heroes" && single.Captain == 1,"one-map pool skips map bans");
Check(!single.Ban(1,201,10000),"deadline is authoritative");
Check(ClanRatings.InitialMmr == 1000 && ClanRatings.Delta(1000,1000) == 16,"equal initial ratings");
Check(ClanRatings.Delta(800,1200) > ClanRatings.Delta(1200,800),"upset earns more rating");
ClanRatings.Record("match-1",10,20,TeamType.Team1);
using (var db = new SQLiteConnection(Databases.PlayerDatabaseFile))
{
    Check(db.Find<ClanRatingRecord>(10).Mmr == 1016 && db.Find<ClanRatingRecord>(20).Mmr == 984,"ratings persisted as one result");
    Parallel.For(0,20,_ => ClanRatings.Record("match-1",10,20,TeamType.Team1));
    Check(db.Find<ClanRatingRecord>(10).Wins == 1 && db.Find<ClanRatingRecord>(20).Losses == 1,"duplicate and concurrent result retries counted once");
    ClanRatings.Record("draw",10,20,TeamType.Neutral);
    Check(db.Table<ClanRatedMatch>().Count() == 1,"draw does not award a win");
    ClanRatings.Record("match-2",10,20,TeamType.Team2);
    Check(db.Find<ClanRatingRecord>(10).Losses == 1 && db.Find<ClanRatingRecord>(20).Wins == 1,"other side receives correct outcome");
    Check(db.Find<ClanRatingRecord>(10).Mmr + db.Find<ClanRatingRecord>(20).Mmr == 2000,"rating updates conserve total");
}
ChatChecks.Run(Check);
Console.WriteLine($"PASS: {checks} clan fight checks");
