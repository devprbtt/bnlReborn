using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.ControlPanel;
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
CompletedMatchRecord Match(string id, ulong start = 1000000, MatchLeaveKind leave = MatchLeaveKind.MatchEnded) => new() {
    MatchId = id, StartedAt = start, ActiveStartedAt = start, EndedAt = start + 600000,
    GameModeKey = new("game_mode_friendly"), EndReason = MatchEndReason.ObjectivesDestroyed,
    Players = [new() { PlayerId = 1, TotalScore = 1, WasInitial = true,
        Presences = [new() { JoinedAt = start, LeftAt = leave == MatchLeaveKind.MatchEnded ? start + 600000 : start + 300000, LeaveKind = leave }] }]
};
var path = Path.Combine(Path.GetTempPath(), "bnl-reputation-" + Guid.NewGuid() + ".db");
try {
using (var db = new SQLiteConnection(path)) {
    Reputation.CreateTables(db, 100);
    db.CreateTable<PlayerRecord>();db.Insert(new PlayerRecord { PlayerId=1 });
    Check(Reputation.Read(db,1).Score == 100, "new players start at 100");
    void Apply(CompletedMatchRecord m) => db.RunInTransaction(() => Reputation.Apply(db,m));
    var q = Match("quit1", leave: MatchLeaveKind.Quit); Apply(q); Apply(q);
    Check(Reputation.Read(db,1).Score == 90 && db.Table<ReputationEvent>().Count() == 1, "quit costs 10 exactly once");
    Apply(Match("quit2", 100000000, MatchLeaveKind.Quit));
    Check(Reputation.Read(db,1).Score == 75, "subsequent quit costs 15 even on another day");
    Apply(Match("quit3", 200000000, MatchLeaveKind.Inactivity));
    Check(Reputation.Read(db,1).Score == 55, "third abandonment costs 20 including AFK");
    CompletedMatchRecord EmptyMatch() { var m=Match("empty",210000000);m.Players[0].TotalScore=0;return m; }
    Apply(EmptyMatch());
    Check(Reputation.Read(db,1).Score == 55, "idle zero-participation match cannot recover");
    Apply(Match("clean", 300000000));
    Check(Reputation.Read(db,1).Score == 56, "clean completion recovers one");
    Apply(Match("quit4", 400000000, MatchLeaveKind.Quit));
    Check(Reputation.Read(db,1).Score == 46, "clean completion resets abandonment streak");
    // Out-of-order delivery must give the same score as chronological settlement.
    var late = Match("late", 250000000); Apply(late);
    Check(Reputation.Read(db,1).Score == 47, "late clean result recomputes ledger deterministically");
    for (ulong i=0;i<6;i++) Apply(Match("floor"+i,500000000+i*1000000,MatchLeaveKind.Quit));
    Check(Reputation.Read(db,1).Score == 0, "score cannot go below zero");
    Apply(Match("recover",600000000));
    Check(Reputation.Read(db,1).Score == 1, "recovery from zero is still one");
    Reputation.CreateTables(db,999999999);
    Check(db.Find<ReputationPolicy>(1).StartedAt == 100, "restart does not reset policy epoch");
    int count=db.Table<ReputationEvent>().Count();
    var historical=Match("historical",1); Apply(historical);
    var custom=Match("custom");custom.GameModeKey=new("game_mode_custom");Apply(custom);
    var cancelled=Match("cancelled");cancelled.EndReason=MatchEndReason.Unknown;Apply(cancelled);
    Check(db.Table<ReputationEvent>().Count()==count,"historical/custom/cancelled excluded");
    var rollback=Match("rollback",700000000);
    try {db.RunInTransaction(()=>{Reputation.Apply(db,rollback);throw new InvalidOperationException();});}catch(InvalidOperationException){}
    Check(db.Find<ReputationEvent>("rollback/1")==null && Reputation.Read(db,1).Score==1,"archive failure rolls back reputation");
}
using(var db=new SQLiteConnection(path)) Check(Reputation.Read(db,1).Score==1,"score survives restart");
var asyncDb=new SQLiteAsyncConnection(path);
var master=(MasterServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
typeof(MasterServerDatabase).GetField("_playerDb",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(master,asyncDb);
var view=await master.GetReputation(1);
Check(view?.Score==1,"public profile query reads persisted score");
Check(await master.GetReputation(999)==null,"unknown player is not assigned a fake reputation");
await asyncDb.CloseAsync();
var options=(JsonSerializerOptions)typeof(ControlPanelServer).GetField("JsonOptions",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
string json=JsonSerializer.Serialize(view,options);
Check(json.Contains("player_id") && !json.Contains("MatchId"),"public wire payload contains score, not private ledger");
if(args.Length>0)File.WriteAllText(args[0],json);
var route=typeof(ControlPanelServer).GetMethod("IsPublicReadRequest",BindingFlags.Static|BindingFlags.NonPublic)!;
Check((bool)route.Invoke(null,["GET","/api/reputation/1"])!,"reputation readable publicly");
Check(!(bool)route.Invoke(null,["POST","/api/reputation/1"])!,"public cannot write reputation");
} finally {File.Delete(path);}
foreach(var seconds in new[]{119,120,121}) {
    var m=Match("disconnect",leave:MatchLeaveKind.Disconnect);var p=m.Players[0];
    p.Presences.Add(new(){JoinedAt=p.Presences[0].LeftAt!.Value+(ulong)seconds*1000,LeftAt=m.EndedAt,LeaveKind=MatchLeaveKind.MatchEnded});
    Check(Reputation.Classify(m,p)==(seconds<=120?"Completion":"Disconnect"),"two-minute reconnect boundary "+seconds);
}
var lost=Match("lost",leave:MatchLeaveKind.Disconnect);Check(Reputation.Classify(lost,lost.Players[0])=="Disconnect","unreturned disconnect penalized");
lost.Players[0].Presences[0].LeftAt=lost.EndedAt-60000;Check(Reputation.Classify(lost,lost.Players[0])==null,"disconnect near match end receives grace, no recovery");
var shortGame=Match("short");shortGame.EndedAt=shortGame.StartedAt+179000;shortGame.Players[0].Presences[0].LeftAt=shortGame.EndedAt;
Check(Reputation.Classify(shortGame,shortGame.Players[0])==null,"short matches do not farm recovery");
var backfill=Match("backfill");backfill.Players[0].WasInitial=false;backfill.Players[0].WasBackfiller=true;backfill.Players[0].Presences[0].JoinedAt=backfill.EndedAt-180000;
Check(Reputation.Classify(backfill,backfill.Players[0])=="Completion","meaningful backfill participation recovers");
backfill.Players[0].Presences[0].JoinedAt++;Check(Reputation.Classify(backfill,backfill.Players[0])==null,"late backfill cannot farm recovery");
var surrendered=Match("surrender");surrendered.EndReason=MatchEndReason.Surrender;
Check(Reputation.Classify(surrendered,surrendered.Players[0])=="Completion","accepted surrender is normal completion");
var departed=Match("after",leave:MatchLeaveKind.Quit);departed.Players[0].Presences[0].LeftAt=departed.EndedAt;
Check(Reputation.Classify(departed,departed.Players[0])==null,"departure after result not penalized");
foreach(var (score,status) in new[]{(100,"Good"),(80,"Good"),(79,"Fair"),(60,"Fair"),(59,"Low"),(40,"Low"),(39,"Very low"),(0,"Very low")})
    Check(Reputation.Status(score)==status,"public band boundary "+score);
Console.WriteLine($"REPUTATION PASS {checks} checks");
