using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using SQLite;
using Category = BNLReloadedServer.ServerTypes.MatchGold.Category;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
Dictionary<Category,double> V(params (Category c,double v)[] values)=>values.ToDictionary(x=>x.c,x=>x.v);
MatchGold.Candidate P(uint id,TeamType team,double seconds,bool win=false,bool eligible=true,Dictionary<Category,double>? v=null)=>
    new(id,team,eligible,win,seconds,v??[]);

// Policy: participation, cap and win bonus.
Check(MatchGold.Participation(15*60)==150,"15 minutes pays 150");
Check(MatchGold.Participation(40*60)==400 && MatchGold.Participation(75*60)==400,"participation stops at 40 minutes (400)");
Check(MatchGold.Participation(59)==9 && MatchGold.Participation(0)==0 && MatchGold.Participation(-5)==0,"partial minutes are pro-rata, rounded down; never negative");
var basic=MatchGold.Compute([P(1,TeamType.Team1,15*60,win:true),P(2,TeamType.Team2,15*60)]);
Check(basic[1].Total==200 && basic[2].Total==150,"15-minute win 200, loss 150");
Check(MatchGold.MaxTotal==520,"most a match can pay is 400 + 50 + 7 x 10");

// Stat leaders are per team; ties share; zero never qualifies; AFK players neither earn nor block a lead.
var all=Enum.GetValues<Category>().Select(c=>(c,5.0)).ToArray();
var leaders=MatchGold.Compute([
    P(10,TeamType.Team1,600,v:V((Category.Kills,8),(Category.Healing,0),(Category.Damage,900))),
    P(11,TeamType.Team1,600,v:V((Category.Kills,8),(Category.Damage,300),(Category.DropsCollected,2))),
    P(12,TeamType.Team1,600,eligible:false,v:V((Category.Kills,50),(Category.Damage,5000))),
    P(20,TeamType.Team2,600,v:V((Category.Kills,1))),
    P(21,TeamType.Team2,2400,win:true,v:V(all))]);
Check(leaders[10].Leads.Contains(Category.Kills) && leaders[11].Leads.Contains(Category.Kills),"tied leaders both earn the kill bonus");
Check(leaders[10].Leads.Contains(Category.Damage) && !leaders[11].Leads.Contains(Category.Damage),"AFK player's higher damage does not block the eligible leader");
Check(!leaders.ContainsKey(12),"AFK player earns nothing");
Check(!leaders[10].Leads.Contains(Category.Healing) && !leaders[11].Leads.Contains(Category.Healing),"zero healing on a team awards nobody");
Check(leaders[20].Leads.Count==0 && leaders[20].Total==100,"leaders are computed per team, not lobby-wide");
Check(leaders[21].Leads.Count==7 && leaders[21].Total==520,"leading all seven pays uncapped +70 (max 520)");
Check(leaders[11].Total==100+20,"drops and a shared kill lead pay +20");

// Each category reads the counters the scoreboard and leaderboards use.
var stats=new Dictionary<PlayerMatchStatType,int>{[PlayerMatchStatType.Kill]=4,[PlayerMatchStatType.Assist]=6,[PlayerMatchStatType.Objective]=700};
var raw=new Dictionary<ScoreType,float>{[ScoreType.EffectiveAllyHealing]=120,[ScoreType.HealPlayerByHero]=999,
    [ScoreType.DamagePlayerByHero]=300,[ScoreType.DamagePlayerByBlock]=50,[ScoreType.DamageFriendlyPlayer]=999,
    [ScoreType.DevicesDestroyed]=3,[ScoreType.HeroBlocksDestroyed]=2,[ScoreType.BlocksDestroyed]=99,
    [ScoreType.SupplyCollected]=1,[ScoreType.BlockbusterCollected]=1};
Check(MatchGold.Value(Category.Healing,stats,raw)==120,"healing counts effective ally healing, not raw or overheal");
Check(MatchGold.Value(Category.Damage,stats,raw)==350,"damage counts enemy players only, never friendly fire");
Check(MatchGold.Value(Category.DevicesDestroyed,stats,raw)==5,"devices destroyed counts devices and hero blocks, not building blocks");
Check(MatchGold.Value(Category.DropsCollected,stats,raw)==2,"drops collected counts supply and blockbuster pickups");
Check(MatchGold.Value(Category.Kills,stats,raw)==4 && MatchGold.Value(Category.Assists,stats,raw)==6 && MatchGold.Value(Category.ObjectiveDamage,stats,raw)==700,"kills, assists and objective damage use scoreboard stats");

// The breakdown travels in each player's end-match stats and survives the byte-keyed wire format.
var written=new Dictionary<PlayerMatchStatType,int>{[PlayerMatchStatType.Kill]=8};
MatchGold.Write(leaders[11],written);
Check(written[PlayerMatchStatType.GoldEarned]==120 && written[PlayerMatchStatType.GoldParticipation]==100 && written[PlayerMatchStatType.GoldVictory]==0 &&
      written[PlayerMatchStatType.GoldMostKills]==10 && written[PlayerMatchStatType.GoldMostDropsCollected]==10 && written[PlayerMatchStatType.GoldMostDamage]==0,"breakdown written next to existing stats");
var afk=new Dictionary<PlayerMatchStatType,int>();MatchGold.Write(null,afk);
Check(afk[PlayerMatchStatType.GoldEarned]==0 && afk.Count==10,"AFK row shows explicit zero Gold");
var data=new EndMatchData{PlayersData=[new EndMatchPlayerData{PlayerId=11,Stats=new EndMatchPlayerStats{Stats=written,Total=5}}],HeroKey=new Key("unit_hero_sarge_stone")};
using(var ms=new MemoryStream()){using var w=new BinaryWriter(ms);data.Write(w);ms.Position=0;
    var back=EndMatchData.ReadRecord(new BinaryReader(ms));
    Check(back.PlayersData![0].Stats!.Stats!.OrderBy(x=>x.Key).SequenceEqual(written.OrderBy(x=>x.Key)),"Gold stats round-trip through the end-match wire format");}

// Master trusts only a plausible payout.
EndMatchResults Result(uint id,int? gold,string instance="gold-match",bool trial=false){
    var s=new Dictionary<PlayerMatchStatType,int>();if(gold is {} g)s[PlayerMatchStatType.GoldEarned]=g;
    return new EndMatchResults{PlayerId=id,GameInstanceId=instance,MatchEndTime=1000,MatchData=new EndMatchData{HeroKey=new Key("unit_hero_sarge_stone"),
        TimeTrialData=trial?new TimeTrialResultData():null,
        PlayersData=[new EndMatchPlayerData{PlayerId=id,Stats=new EndMatchPlayerStats{Stats=s}},new EndMatchPlayerData{PlayerId=id+1,Stats=new EndMatchPlayerStats{Stats=new(){[PlayerMatchStatType.GoldEarned]=400}}}]}};}
Check(MatchGold.Earned(Result(5,215))==215,"master reads the player's own Gold row");
Check(MatchGold.Earned(Result(5,null))==0,"no Gold row (custom game, old region) pays nothing");
Check(MatchGold.Earned(Result(5,521))==0 && MatchGold.Earned(Result(5,-1))==0,"impossible payouts are rejected");
Check(MatchGold.Earned(Result(5,200,trial:true))==0,"time trials never pay match Gold");

// Released clients display RewardCurrency x (1 + bonus fractions), rounding each line.
int mismatches=0;
foreach(var bonuses in new[]{new float[0],new[]{.5f},new[]{.5f,.5f},new[]{.5f,.25f}})
for(int total=0;total<=MatchGold.MaxTotal;total++){
    var b=MatchGold.DisplayBase(total,bonuses);int shown=(int)Math.Round(b,MidpointRounding.ToEven);
    foreach(var f in bonuses)shown+=(int)Math.Round(f*b,MidpointRounding.ToEven);
    if(Math.Abs(shown-total)>1)mismatches++;}
Check(mismatches==0,"released reward screen shows the credited total within 1 Gold of rounding");

// Played time: overlapping reconnects merge, time before the start or after the end is excluded.
var trackerType=typeof(MatchGold).Assembly.GetType("BNLReloadedServer.ServerTypes.MatchParticipationTracker")!;
var tracker=Activator.CreateInstance(trackerType,true)!;
var t0=DateTimeOffset.FromUnixTimeSeconds(1_000_000);
PlayerLobbyState L(uint id)=>new(){PlayerId=id,Team=TeamType.Team1,Hero=new Key("unit_hero_sarge_stone")};
trackerType.GetMethod("Start")!.Invoke(tracker,[t0,new List<PlayerLobbyState>{L(1)},new Dictionary<uint,Moserware.Skills.Rating>()]);
void Leave(uint id,int s)=>trackerType.GetMethod("Leave")!.Invoke(tracker,[id,t0.AddSeconds(s),MatchLeaveKind.Disconnect]);
void Join(uint id,int s,MatchJoinKind k)=>trackerType.GetMethod("Join")!.Invoke(tracker,[L(id),t0.AddSeconds(s),k,k==MatchJoinKind.Backfill,null]);
double Played(uint id,int s)=>(double)trackerType.GetMethod("PlayedSeconds")!.Invoke(tracker,[id,t0.AddSeconds(s)])!;
Leave(1,300);Join(1,420,MatchJoinKind.Reconnect);
Join(2,600,MatchJoinKind.Backfill);
Check(Played(1,900)==780,"initial player: 15 min match minus 2 min disconnect");
Check(Played(2,900)==300,"backfiller earns from the time they joined");
Check(Played(3,900)==0,"unknown player has no played time");
Check(MatchGold.Participation(Played(1,900))==130 && MatchGold.Participation(Played(2,900))==50,"participation follows played time");

// Real master settlement: Gold is credited with milestones, once, behind the settlement guard.
var path=Path.Combine(Path.GetTempPath(),"bnl-match-gold-"+Guid.NewGuid()+".db");
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
try {
    using(var db=new SQLiteConnection(path)){db.CreateTable<PlayerRecord>();db.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(db));}
    var asyncDb=new SQLiteAsyncConnection(path);
    ((ServerCatalogue)Databases.Catalogue).Replicate([new CardGlobalLogic{Id="global_logic",XpLogic=new GlobalExpLogic{HeroXp=new ExpLogic{FlatCoeff=100,MultCoeff=0,PowerCoeff=1,MaxLevel=200}}}]);
    var sarge=new Key("unit_hero_sarge_stone");
    PlayerRecord Fresh(int level)=>new PlayerData{Progression=new PlayerProgression{HeroesProgress=new(){{sarge,new XpInfo{Level=level}}}}}.ToPlayerRecord();
    var player=Fresh(9);player.Gold=1000;await asyncDb.InsertAsync(player);
    var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
    typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
    typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
    var online=(System.Collections.Concurrent.ConcurrentDictionary<uint,PlayerData>)typeof(PlayerDatabase).GetField("_players",flags)!.GetValue(Databases.PlayerDatabase)!;
    online[player.PlayerId]=PlayerData.FromPlayerRecord(player);
    var region=DispatchProxy.Create<IRegionServerDatabase,RegionProbe>();Databases.SetRegionDatabase(region);
    var match=Result(player.PlayerId,230);match.MatchData.NewHeroXp=new XpInfo{Level=10};
    await master.SetNewMatchDataForPlayer(match);
    var paid=await asyncDb.FindAsync<PlayerRecord>(player.PlayerId);
    Check(paid.Gold==1000+230+250,"match Gold and level-10 milestone credited together");
    Check(Databases.PlayerDatabase.GetCurrency(player.PlayerId)[CurrencyType.Virtual]==1480 && ((RegionProbe)(object)region).Wallet![CurrencyType.Virtual]==1480,"online wallet and client notification show the new balance");
    await master.SetNewMatchDataForPlayer(match);
    Check((await asyncDb.FindAsync<PlayerRecord>(player.PlayerId)).Gold==1480,"duplicate match report pays nothing");
    var second=Result(player.PlayerId,150,"gold-match-2");second.MatchData.NewHeroXp=new XpInfo{Level=10};
    await master.SetNewMatchDataForPlayer(second);
    Check((await asyncDb.FindAsync<PlayerRecord>(player.PlayerId)).Gold==1630,"next match pays its own Gold; milestone not repeated");
    var unguarded=Result(player.PlayerId,150,"");unguarded.MatchData.NewHeroXp=new XpInfo{Level=10};
    await master.SetNewMatchDataForPlayer(unguarded);
    Check((await asyncDb.FindAsync<PlayerRecord>(player.PlayerId)).Gold==1630,"result without a settlement id pays no Gold");
    online.TryRemove(player.PlayerId,out _);
    await asyncDb.CloseAsync();
} finally {SQLiteAsyncConnection.ResetPool();File.Delete(path);}
Console.WriteLine($"PASS match gold: {checks} checks");

public class RegionProbe : DispatchProxy
{
    public Dictionary<CurrencyType,float>? Wallet;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if(method!.Name=="NotifyCurrency")Wallet=new((Dictionary<CurrencyType,float>)args![1]!);
        return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
}
