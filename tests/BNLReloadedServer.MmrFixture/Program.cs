using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using Moserware.Skills;
using SQLite;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

if(args.Length==3&&args[0]=="--replay"){Replay.Run(args[1],args[2]);return;}
if(args.Length==4&&args[0]=="--calibrate")
{
    using var db=new SQLiteConnection(args[1],SQLiteOpenFlags.ReadOnly);
    var matches=db.Table<MmrMatchSettlement>().ToList().Select(m=>MmrLedger.Decode(m.Payload));
    var profiles=MmrReferenceTrainer.Build(matches,long.Parse(args[3]));
    File.WriteAllText(args[2],System.Text.Json.JsonSerializer.Serialize(profiles,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"MMR_CALIBRATION_OK profiles={profiles.Count}");return;
}
int checks=0;
void Check(bool ok,string message) { if(!ok)throw new Exception(message);checks++; }
CompletedMatchRecord Match()=>new(){MatchId=Guid.NewGuid().ToString(),RatingVersion=2,StartedAt=1000000,
    ActiveStartedAt=1000000,EndedAt=2200000,MatchType=MatchType.ShieldRush2,
    GameModeKey=new("game_mode_friendly"),MapKey=new("map_test"),Winner=TeamType.Team1,EndReason=MatchEndReason.ObjectivesDestroyed,
    Players=Enumerable.Range(1,8).Select(i=>new CompletedMatchPlayer {PlayerId=(uint)i,WasInitial=true,StartingRatingMean=25,StartingRatingDeviation=3,
        Presences=[new(){Sequence=0,TeamSlot=(i-1)%4+1,Team=i<=4?TeamType.Team1:TeamType.Team2,JoinedAt=1000000,LeftAt=2200000,LeaveKind=MatchLeaveKind.MatchEnded,HeroKey=new("unit_test")}]}).ToList()};
Dictionary<uint,Rating> Ratings(CompletedMatchRecord m)=>m.Players.ToDictionary(p=>p.PlayerId,p=>new Rating(25,3));
List<MmrAward> Calc(CompletedMatchRecord m)=>PersonalMmr.Calculate(m,Ratings(m));
var normal=Match();var normalAwards=Calc(normal);
Check(normalAwards.Take(4).All(a=>a.Delta>0)&&normalAwards.Skip(4).All(a=>a.Delta<0),"outcome signs");
foreach(var reason in new[]{MatchLeaveKind.Quit,MatchLeaveKind.Inactivity,MatchLeaveKind.Disconnect})
foreach(ulong left in new ulong[]{1000001,1600000,2199999})
foreach(var winner in new[]{TeamType.Team1,TeamType.Team2,TeamType.Neutral})
{
    var m=Match();m.Winner=winner;if(winner==TeamType.Neutral)m.EndReason=MatchEndReason.Abandoned;
    m.Players[0].Presences[0].LeftAt=left;m.Players[0].Presences[0].LeaveKind=reason;
    var a=Calc(m)[0];Check(a.Abandoned&&a.Delta<0&&a.Performance==0&&a.Shortage==0,"quit/disconnect loses even at last ms or team wins/empty match");
    Check(a.Delta<=normalAwards[4].Delta*1.25+1e-8,"quit never cheaper than ordinary worst full loss");
}
var reconnect=Match();var segment=reconnect.Players[0].Presences[0];segment.LeftAt=1500000;segment.LeaveKind=MatchLeaveKind.Disconnect;
reconnect.Players[0].Presences.Add(new(){Sequence=1,Team=TeamType.Team1,TeamSlot=1,JoinedAt=1545000,LeftAt=reconnect.EndedAt,LeaveKind=MatchLeaveKind.MatchEnded,HeroKey=segment.HeroKey});
Check(!Calc(reconnect)[0].Abandoned,"return exactly at grace accepted");
reconnect.Players[0].Presences[1].JoinedAt++;
Check(Calc(reconnect)[0].Abandoned,"late reconnect cannot erase abandonment");
reconnect.Players[0].Presences[1].JoinedAt=1510000;segment.LeaveKind=MatchLeaveKind.Quit;
Check(Calc(reconnect)[0].Abandoned,"explicit quit and return remains abandonment");
var returnedLoss=Match();returnedLoss.Winner=TeamType.Team2;
returnedLoss.Players[0].Presences[0].LeftAt=1500000;returnedLoss.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Disconnect;
returnedLoss.Players[0].Presences.Add(new(){Sequence=1,Team=TeamType.Team1,TeamSlot=1,JoinedAt=1510000,LeftAt=returnedLoss.EndedAt,LeaveKind=MatchLeaveKind.MatchEnded,HeroKey=new("unit_test")});
var returnedAward=Calc(returnedLoss)[0];
Check(!returnedAward.Abandoned && Math.Abs(returnedAward.Delta/returnedAward.Base-1.25)<1e-9,"reconnect cannot erase a weak-performance loss penalty");
var repeated=Match();var rp=repeated.Players[0];rp.Presences[0].LeftAt=1500000;rp.Presences[0].LeaveKind=MatchLeaveKind.Disconnect;
rp.Presences.Add(new(){Sequence=1,Team=TeamType.Team1,TeamSlot=1,JoinedAt=1530000,LeftAt=1600000,LeaveKind=MatchLeaveKind.Disconnect});
rp.Presences.Add(new(){Sequence=2,Team=TeamType.Team1,TeamSlot=1,JoinedAt=1630000,LeftAt=repeated.EndedAt,LeaveKind=MatchLeaveKind.MatchEnded});
Check(Calc(repeated)[0].Abandoned,"repeated reconnects cannot reset the total 45-second budget");
var end=Match();end.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Disconnect;
Check(!Calc(end)[0].Abandoned,"disconnect at/after authoritative match end exempt");
var backfill=Match();backfill.Players[0].WasInitial=false;backfill.Players[0].WasBackfiller=true;backfill.Players[0].Presences[0].JoinKind=MatchJoinKind.Backfill;
backfill.Players[0].Presences[0].LeftAt=2000000;backfill.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Quit;
Check(Calc(backfill)[0] is {Delta:0,Deviation:3,Abandoned:true},"backfiller freeze plus misconduct cooldown eligibility");
backfill.Players[0].WasInitial=true;
Check(Calc(backfill)[0].Delta<0,"original flag wins over contaminated backfill flag");
var tracker=new MatchParticipationTracker();var at=DateTimeOffset.FromUnixTimeMilliseconds(1000000);
tracker.Start(at,[new(){PlayerId=1,Team=TeamType.Team1}],new Dictionary<uint,Rating>{{1,new(25,3)}});
tracker.Leave(1,at.AddSeconds(10),MatchLeaveKind.Disconnect);
tracker.Leave(1,at.AddSeconds(55),MatchLeaveKind.Quit);
tracker.Join(new(){PlayerId=1,Team=TeamType.Team1},at.AddSeconds(60),MatchJoinKind.Backfill,true);
var tracked=tracker.Complete(at.AddSeconds(90))[0];
Check(tracked.WasInitial&&!tracked.WasBackfiller&&tracked.Presences[0].LeaveKind==MatchLeaveKind.Quit,"tracker preserves original identity and timer expiry");
var roundtrip=MmrLedger.Decode(MmrLedger.Encode(normal));
Check(roundtrip.RatingVersion==2&&roundtrip.ActiveStartedAt==normal.ActiveStartedAt&&roundtrip.MatchType==normal.MatchType,"wire trailer roundtrip");
var shortage=Match();shortage.Players[0].Presences[0].LeftAt=1600000;shortage.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Quit;
Check(Math.Abs(PersonalMmr.Exposure(shortage)-.5)<1e-9,"half-match missing player exposure");
Check(Calc(shortage).Skip(4).All(a=>a.Shortage==0),"full team loss gets no shortage change");
var reference=new MmrReference(new Key("unit_test").Hash,normal.GameModeKey.Hash,normal.MapKey.Hash,999999,"fixture",20,
    Enumerable.Range(1,100).Select(i=>(double)i).ToArray(),Enumerable.Range(1,100).Select(i=>(double)i).ToArray(),Enumerable.Range(1,100).Select(i=>(double)i).ToArray(),GameType:normal.MatchType);
var scored=Match();scored.Players[0].RawStats[ScoreType.DamagePlayerByHero]=1;
Check(PersonalMmr.Performance(scored,scored.Players[0],[reference])==-.4,"missing utility/objective stay neutral; combat bounded at 40 percent");
scored.Players[0].Stats[PlayerMatchStatType.Destroyed]=int.MaxValue;
Check(PersonalMmr.Performance(scored,scored.Players[0],[reference])==-.4,"legacy Destroyed never awards performance");
Check(PersonalMmr.Performance(scored,scored.Players[0],[reference with{Before=(long)scored.StartedAt}])==0,"no future/current match reference leakage");
Check(PersonalMmr.Performance(scored,scored.Players[0],[reference with{Players=19}])==0,"sparse cohort fallback");
var betaReference=reference with{Scope="beta-v2",Map=0,Loadout="*",Players=5,Kills=Enumerable.Repeat(1d,10).ToArray(),Assists=Enumerable.Repeat(1d,10).ToArray(),Damage=Enumerable.Repeat(1d,10).ToArray()};
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference])==-.4,"beta pooled cohort activates at ten samples/five players");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Players=4}])==0,"beta requires five distinct players");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Damage=new double[9]}])==0,"beta requires ten samples in each combat component");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Scope="typo"}])==0,"unknown cohort scope cannot bypass exact gates");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Mode=new Key("game_mode_ranked").Hash}])==0,"beta never crosses queues");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{GameType=null}])==0,"fresh beta never crosses match types");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Before=(long)scored.StartedAt}])==0,"beta excludes current/future cohorts");
var betaHealing=Match();betaHealing.Players[0].RawStats[ScoreType.DamagePlayerByHero]=1;betaHealing.Players[0].EffectiveAllyHealing=1000;
var healerReference=betaReference with{EffectiveAllyHealing=Enumerable.Repeat(1d,10).ToArray()};
Check(Math.Abs(PersonalMmr.Performance(betaHealing,betaHealing.Players[0],[healerReference])+.1)<1e-9,"audited beta ally healing activates full utility component");
Check(PersonalMmr.Performance(betaHealing,betaHealing.Players[0],[healerReference with{Scope="beta-legacy",GameType=null}])==0,"historical cohorts cannot award performance");
var preferred=betaReference with{Kills=new double[10],Assists=new double[10],Damage=new double[10]};
Check(PersonalMmr.Performance(scored,scored.Players[0],[reference,preferred])==-.4,"exact calibration takes precedence over pooled beta");
Check(PersonalMmr.Performance(scored,scored.Players[0],[betaReference with{Damage=[double.NaN]},preferred])>0,"invalid cohort does not hide valid fallback");
var training=Enumerable.Range(0,2).Select(_=>Match()).ToArray();
foreach(var m in training)foreach(var p in m.Players){p.Stats[PlayerMatchStatType.Kill]=1;p.Stats[PlayerMatchStatType.Assist]=1;p.Stats[PlayerMatchStatType.Objective]=0;p.RawStats[ScoreType.DamagePlayerByHero]=10;p.EffectiveAllyHealing=0;}
var freshProfiles=MmrReferenceTrainer.BuildBeta(training,3000000);
Check(freshProfiles.Count==1&&freshProfiles[0].Scope=="beta-v2"&&freshProfiles[0].Kills.Length==16&&freshProfiles[0].EffectiveAllyHealing?.Length==16,"beta trainer emits audited full component cohort");
Check(MmrReferenceTrainer.Build(training,3000000).Count==0,"strict offline trainer retains strict requirements");
Check(MmrReferenceTrainer.BuildBeta([training[0],training[0]],3000000).Count==0,"duplicate match cannot create beta training coverage");
Check(MmrReferenceTrainer.BuildBeta(training,2200000).Count==0,"trainer excludes current and future outcomes");
Check(MmrReferenceTrainer.BuildBeta(training,3000000+31L*86400000).Count==0,"beta training expires samples beyond thirty days");
var smallTraining=Enumerable.Range(0,3).Select(i=>{var m=MmrLedger.Decode(MmrLedger.Encode(training[0]));m.MatchId=Guid.NewGuid().ToString();m.Players=m.Players.Where(p=>p.PlayerId is 1 or 2 or 5 or 6).ToList();foreach(var p in m.Players)p.PlayerId+=(uint)i*10;return m;}).ToArray();
Check(MmrReferenceTrainer.BuildBeta(smallTraining,3000000).Count==0,"unrated small matches cannot train beta cohorts even with enough distinct players");
using(var betaDb=new SQLiteConnection(":memory:"))
{
    MmrLedger.CreateTables(betaDb);
    foreach(var m in training)betaDb.Insert(new MmrMatchSettlement{MatchId=m.MatchId,Version=2,Payload=MmrLedger.Encode(m)});
    var next=Match();next.StartedAt=3000000;
    Check(MmrReferenceFile.ForMatch(betaDb,next).Any(r=>r.Scope=="beta-v2"&&r.EffectiveAllyHealing?.Length==16),"settlement automatically loads full audited beta cohorts from durable history");
    Check(!MmrReferenceFile.ForMatch(betaDb,training[0]).Any(r=>r.Scope=="beta-v2"),"automatic history cannot train on concurrent/current results");
}
var random=new Random(31);
for(int i=0;i<500;i++)
{
    var m=Match();foreach(var p in m.Players){p.Stats[PlayerMatchStatType.Kill]=random.Next(1000);p.RawStats[ScoreType.DamagePlayerByHero]=random.Next(10000);}
    m.Players[0].Presences[0].LeftAt=(ulong)random.Next(1000001,2199999);m.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Quit;
    foreach(var a in PersonalMmr.Calculate(m,Ratings(m),[reference]).Where(a=>!a.Abandoned&&a.Base!=0))
        Check(a.Delta/a.Base>=.25-1e-9&&a.Delta/a.Base<=(a.Base>0?1.5:1.25)+1e-9,"combined caps and signs");
}
var partial=Match();partial.Players[0].WasInitial=false;partial.Players[0].Presences[0].JoinKind=MatchJoinKind.Backfill;
var completeWeight=Calc(partial)[1].Base;partial.Players[0].Presences[0].JoinedAt=2000000;
Check(Math.Abs(Calc(partial)[1].Base-completeWeight)>1e-5,"installed TrueSkill adapter actually honors partial play weight");
Check(MmrReferenceTrainer.Build([normal],3000000).Count==0,"trainer refuses sparse reference cohort");
var unknown=Match();unknown.Winner=TeamType.Neutral;unknown.EndReason=MatchEndReason.Unknown;unknown.Players[0].Presences[0].LeftAt=2000000;unknown.Players[0].Presences[0].LeaveKind=MatchLeaveKind.Quit;
Check(Calc(unknown).All(a=>a.Delta==0&&!a.Abandoned),"server-invalid outcome cannot punish players");
var oldWire=MmrLedger.Encode(normal);var legacyLength=oldWire.Length-(1+4+1+8+1+1+1+normal.Players.Count*5);
using(var reader=new BinaryReader(new MemoryStream(oldWire[..legacyLength])))
    Check(CompletedMatchRecord.ReadRecord(reader).RatingVersion==0,"legacy archive trailer absence never opts into new awards");
var path=Path.Combine(Path.GetTempPath(),"bnl-mmr-"+Guid.NewGuid()+".db");
try
{
    using(var db=new SQLiteConnection(path))
    {
        MmrLedger.CreateTables(db);db.CreateTable<PlayerRecord>();db.CreateTable<ArchivedMatchRecord>();
        foreach(var p in shortage.Players)db.Insert(new PlayerRecord{PlayerId=p.PlayerId,RatingMean=25,RatingDeviation=3});
        db.RunInTransaction(()=>MmrLedger.Apply(db,shortage,2300000));
        var mean=db.Find<PlayerRecord>(1u).RatingMean;var ban=db.Find<MmrQueuePenalty>(1u).Until;
        db.RunInTransaction(()=>MmrLedger.Apply(db,shortage,2400000));
        Check(db.Table<MmrSettlement>().Count()==8&&db.Find<PlayerRecord>(1u).RatingMean==mean&&db.Find<MmrQueuePenalty>(1u).Until==ban,"retry exactly once ratings and cooldown");
        var rollback=Match();rollback.StartedAt=3000000;rollback.ActiveStartedAt=3000000;rollback.EndedAt=4200000;
        foreach(var p in rollback.Players){p.Presences[0].JoinedAt=3000000;p.Presences[0].LeftAt=4200000;}
        try {db.RunInTransaction(()=>{MmrLedger.Apply(db,rollback,4300000);throw new Exception("rollback");});}catch(Exception ex)when(ex.Message=="rollback"){}
        Check(db.Table<MmrSettlement>().Count()==8&&db.Find<PlayerRecord>(1u).RatingMean==mean,"ratings ledger and cooldown roll back together");
    }
    using(var db=new SQLiteConnection(path))Check(db.Find<MmrQueuePenalty>(1u)!=null&&db.Table<MmrSettlement>().Count()==8,"penalty and ledger survive reopening");
}finally{File.Delete(path);}
Check(MmrQueueGuard.CooldownMinutes(0)==5&&MmrQueueGuard.CooldownMinutes(1)==15&&MmrQueueGuard.CooldownMinutes(2)==60,"repeat abandonment escalates");
MmrQueueGuard.Reserve(100,"one");MmrQueueGuard.Release(100,"other");Check(MmrQueueGuard.Blocked(100,0),"wrong match cannot clear pending lock");
MmrQueueGuard.Release(100,"one");MmrQueueGuard.Ban(100,100);Check(MmrQueueGuard.Blocked(100,99)&&!MmrQueueGuard.Blocked(100,100),"cooldown exact expiry");
Console.WriteLine($"MMR_FIXTURE_OK checks={checks}");
