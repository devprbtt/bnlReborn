using BNLReloadedServer.Database;
using BNLReloadedServer.BaseTypes;
using SQLite;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var path=Path.Combine(Path.GetTempPath(),"bnl-milestone-"+Guid.NewGuid()+".db");
Key sarge=new("unit_hero_sarge_stone"),asta=new("unit_hero_astrogirl");
PlayerProgression Progress(int level, Key? hero=null)=>new(){HeroesProgress=new(){{hero??sarge,new XpInfo{Level=level}}}};
PlayerRecord Record(uint id,int level)=>new PlayerData{PlayerId=id,Progression=Progress(level)}.ToPlayerRecord();
using(var legacy=new SQLiteConnection(":memory:")) {
 legacy.Execute("CREATE TABLE Users (id INTEGER PRIMARY KEY AUTOINCREMENT, progression BLOB)");
 legacy.Execute("INSERT INTO Users (progression) VALUES (?)",PlayerProgression.WriteByteRecord(Progress(50)));
 legacy.CreateTable<PlayerRecord>();
 Check(legacy.Table<PlayerRecord>().First().Gold==0 && legacy.Table<PlayerRecord>().First().Platinum==0,"old database gains zero-valued wallet columns");
 legacy.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(legacy));
 Check(legacy.Table<PlayerRecord>().First().Gold==2750 && legacy.Table<PlayerRecord>().First().Platinum==100,"schema upgrade retroactively grants earned level 50 rewards");
}
uint veteranId=0;
try {
 using(var db=new SQLiteConnection(path)) {
  db.CreateTable<PlayerRecord>();db.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(db));
  Check(HeroMilestoneRewards.Schedule.Select(x=>x.Level).SequenceEqual(new[]{10,20,30,50,60,70,80,90,100,150,200}),"exact requested milestones, no level 40");
  long totalGold=0,totalPlat=0;
  for(int i=0;i<HeroMilestoneRewards.Schedule.Count;i++) {
   var r=HeroMilestoneRewards.Schedule[i];totalGold+=r.Gold;totalPlat+=r.Platinum;
   var record=Record((uint)(100+i),r.Level-1);db.Insert(record);
   db.RunInTransaction(()=>{HeroMilestoneRewards.GrantEarned(db,record,Progress(r.Level-1));db.Update(record);});
   var oldGold=record.Gold;var oldPlat=record.Platinum;
   db.RunInTransaction(()=>{HeroMilestoneRewards.GrantEarned(db,record,Progress(r.Level));db.Update(record);});
   Check(record.Gold-oldGold==r.Gold && record.Platinum-oldPlat==r.Platinum,"exact reward at level "+r.Level);
   Check(record.Gold==totalGold && record.Platinum==totalPlat,"cumulative rewards through "+r.Level);
   db.RunInTransaction(()=>{var reward=HeroMilestoneRewards.GrantEarned(db,record,Progress(r.Level));Check(reward==(0L,0L),"duplicate level "+r.Level+" pays nothing");});
  }
  Check(totalGold==28250 && totalPlat==1600,"full hero total is 28,250 Gold and 1,600 Platinum");
  var veteran=Record(1,200);db.Insert(veteran);veteranId=veteran.PlayerId;
  db.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(db));veteran=db.Find<PlayerRecord>(veteranId);
  Check(veteran.Gold==totalGold && veteran.Platinum==1600,"existing level 200 receives all earned milestones");
  var before=db.Table<HeroMilestoneClaim>().Count();db.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(db));
  Check(db.Table<HeroMilestoneClaim>().Count()==before && db.Find<PlayerRecord>(veteranId).Gold==totalGold,"startup backfill is repeat-safe");
  var other=Progress(50,asta);db.RunInTransaction(()=>{HeroMilestoneRewards.GrantEarned(db,veteran,other);db.Update(veteran);});
  Check(veteran.Gold==totalGold+2750 && veteran.Platinum==1700,"different hero earns independent milestones");
  var rollback=Record(2,9);db.Insert(rollback);
  try{db.RunInTransaction(()=>{HeroMilestoneRewards.GrantEarned(db,rollback,Progress(100));db.Update(rollback);throw new InvalidOperationException("rollback");});}catch(InvalidOperationException){}
  Check(db.Find<PlayerRecord>(rollback.PlayerId).Gold==0 && db.Table<HeroMilestoneClaim>().Where(x=>x.PlayerId==rollback.PlayerId).Count()==0,"failed transaction rolls back wallet and claims");
  var player=PlayerData.FromPlayerRecord(veteran);using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);player.Write(writer);bytes.Position=0;var loaded=PlayerData.ReadRecord(new BinaryReader(bytes));
  Check(loaded.Currency[CurrencyType.Virtual]==veteran.Gold && loaded.Currency[CurrencyType.Real]==veteran.Platinum,"wallet survives master-region binary transfer");
  Check(loaded.ToPlayerRecord().Gold==veteran.Gold,"other profile saves preserve wallet");

 }
 using(var reopened=new SQLiteConnection(path)) {
  Check(reopened.Find<PlayerRecord>(veteranId).Gold==31000 && reopened.Find<PlayerRecord>(veteranId).Platinum==1700,"wallet survives database reopen");
  int count=reopened.Table<HeroMilestoneClaim>().Count();reopened.RunInTransaction(()=>HeroMilestoneRewards.InstallAndBackfill(reopened));
  Check(reopened.Table<HeroMilestoneClaim>().Count()==count,"claims survive restart and stop duplicate retroactive awards");
 }
 // Exercise actual match settlement and the online wallet update, including switched heroes.
 var asyncDb=new SQLiteAsyncConnection(path);
 var catalogue=(ServerCatalogue)Databases.Catalogue;
 catalogue.Replicate([new CardGlobalLogic {Id="global_logic",XpLogic=new GlobalExpLogic {HeroXp=new ExpLogic {FlatCoeff=100,MultCoeff=0,PowerCoeff=1,MaxLevel=200}}}]);
 var start=Record(0,9);var initial=Progress(9);initial.HeroesProgress![asta]=new XpInfo{Level=9,LevelXp=0,XpForNextLevel=100};start.Progression=PlayerProgression.WriteByteRecord(initial);await asyncDb.InsertAsync(start);
 var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
 typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
 var online=(System.Collections.Concurrent.ConcurrentDictionary<uint,PlayerData>)typeof(PlayerDatabase).GetField("_players",flags)!.GetValue(Databases.PlayerDatabase)!;
 online[start.PlayerId]=PlayerData.FromPlayerRecord(start);
 var region=System.Reflection.DispatchProxy.Create<IRegionServerDatabase,RegionProbe>();Databases.SetRegionDatabase(region);
 var result=new EndMatchResults {PlayerId=start.PlayerId,GameInstanceId="milestone-integration",MatchEndTime=123,MatchData=new EndMatchData {HeroKey=sarge,NewHeroXp=new XpInfo {Level=10}},OtherHeroXp=new(){{asta,100}}};
 await master.SetNewMatchDataForPlayer(result);
 var paid=await asyncDb.FindAsync<PlayerRecord>(start.PlayerId);
 Check(paid.Gold==500 && paid.Platinum==0,"real settlement pays both final and switched heroes");
 Check(Databases.PlayerDatabase.GetCurrency(start.PlayerId)[CurrencyType.Virtual]==500 && ((RegionProbe)(object)region).Wallet![CurrencyType.Virtual]==500,"online cache and client notification receive persisted balance");
 var beforeXp=paid.Progression;await master.SetNewMatchDataForPlayer(result);paid=await asyncDb.FindAsync<PlayerRecord>(start.PlayerId);
 Check(paid.Gold==500 && paid.Progression.SequenceEqual(beforeXp),"duplicate match report cannot replay XP or milestone payouts");
 Check((await asyncDb.Table<HeroProgressSettlement>().ToListAsync()).Count==1,"successful match has one durable settlement guard");
 // Profile sanitization must not save the stale wallet it read before waiting on settlement.
 var stale=Record(0,9);stale.BadgeInfo=PlayerData.WriteBadgeByteRecord(new(){{BadgeType.Title,[new Key("removed_badge")]}});await asyncDb.InsertAsync(stale);
 var gate=(SemaphoreSlim)typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.GetValue(master)!;
 await gate.WaitAsync();
 var sanitize=(Task<PlayerData>)typeof(MasterServerDatabase).GetMethod("LoadSanitized",flags)!.Invoke(master,[stale])!;
 await asyncDb.ExecuteAsync("UPDATE Users SET gold = 777 WHERE id = ?",stale.PlayerId);
 gate.Release();var sanitized=await sanitize;
 Check(sanitized.Currency[CurrencyType.Virtual]==777 && (await asyncDb.FindAsync<PlayerRecord>(stale.PlayerId)).Gold==777,"profile cleanup cannot overwrite currency committed while it waited");
 online.TryRemove(start.PlayerId,out _);
 await asyncDb.CloseAsync();
} finally {SQLiteAsyncConnection.ResetPool();File.Delete(path);}
Console.WriteLine($"PASS hero milestones: {checks} checks");

public class RegionProbe : System.Reflection.DispatchProxy
{
 public Dictionary<CurrencyType,float>? Wallet;
 protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
 {
  if(method!.Name=="NotifyCurrency")Wallet=new((Dictionary<CurrencyType,float>)args![1]!);
  return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
 }
}
