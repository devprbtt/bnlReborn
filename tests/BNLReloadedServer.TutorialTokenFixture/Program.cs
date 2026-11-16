using BNLReloadedServer.Database;
using BNLReloadedServer.BaseTypes;
using SQLite;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.Service;
using BNLReloadedServer.Servers;

static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
string file=Path.Combine(Path.GetTempPath(),"bnl-tutorial-"+Guid.NewGuid()+".sqlite");
try {
    using(var db=new SQLiteConnection(file)){
        db.CreateTable<PlayerRecord>();db.CreateTable<TutorialTokenCollection>();
        db.Insert(new PlayerRecord{PlayerId=1,Username="fixture"});db.Insert(new PlayerRecord{PlayerId=2,Username="other"});
        var unit=new MapUnit{UnitKey=new Key("tutorial_token"),Position=new Vector3(1,2,3)};
        string token=TutorialTokenProgress.TokenId(new Key("tutorial"),unit);
        Check(token==TutorialTokenProgress.TokenId(new Key("tutorial"),unit),"stable map token identity across sessions");
        int result=0;db.RunInTransaction(()=>result=TutorialTokenProgress.Record(db,1,token,35));
        Check(result==1,"first accepted token saved");
        db.RunInTransaction(()=>result=TutorialTokenProgress.Record(db,1,token,35));
        Check(result==1&&db.Table<TutorialTokenCollection>().Count()==1,"repeat pickup cannot count twice");
        Check(TutorialTokenProgress.Record(db,2,token,35)==1,"another account owns independent collection");
    }
    using(var db=new SQLiteConnection(file)){
        Check(db.Find<PlayerRecord>(1).TutorialTokens==1,"progress survives database close and reopen");
        for(int i=1;i<35;i++){int index=i;db.RunInTransaction(()=>TutorialTokenProgress.Record(db,1,"other-token-"+index,35));}
        Check(db.Find<PlayerRecord>(1).TutorialTokens==35,"all 35 distinct practice tokens persist");
        Check(TutorialTokenProgress.Record(db,1,"repeat-session",35)==35,"collection bounded by actual map token total");
        Check(TutorialTokenProgress.Record(db,999,"invalid-player",35)==0,"missing player cannot create progress");
        Check(db.Find<PlayerRecord>(2).TutorialTokens==1,"collection leaves other account unchanged");
    }
}finally{File.Delete(file);}

// The existing completion popup must return a tutorial player to the menu.
var match=new CardMatch{Id="fixture_tutorial_match",Data=new MatchDataTutorial()};
var mode=new CardGameMode{Id="fixture_tutorial_mode",MatchMode=new Key(match.Id)};
((ServerCatalogue)Databases.Catalogue).Replicate(new List<Card>{match,mode});
var sender=DispatchProxy.Create<ISender,Capture>();var sent=(Capture)(object)sender;sent.Player=77;
var game=DispatchProxy.Create<IGameInstance,Capture>();var gameCapture=(Capture)(object)game;gameCapture.Mode=mode.Key;
var region=DispatchProxy.Create<IRegionServerDatabase,Capture>();((Capture)(object)region).Game=game;
Databases.SetRegionDatabase(region);
var service=(ServiceZone)RuntimeHelpers.GetUninitializedObject(typeof(ServiceZone));
const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
foreach(var field in typeof(ServiceZone).GetFields(flags).Where(f=>f.FieldType==typeof(ISender)))field.SetValue(service,sender);
var finish=typeof(ServiceZone).GetMethod("ReceiveFinishTutorial",flags)!;
using var reader=new BinaryReader(new MemoryStream());
finish.Invoke(service,[reader]);Check(gameCapture.Left.SequenceEqual(new uint[]{77}),"tutorial completion returns authenticated player to menu");
sent.Player=null;finish.Invoke(service,[reader]);Check(gameCapture.Left.Count==1,"unauthenticated completion ignored");
sent.Player=77;gameCapture.Mode=Key.None;finish.Invoke(service,[reader]);Check(gameCapture.Left.Count==1,"completion cannot leave a non-tutorial match");

public class Capture:DispatchProxy
{
 public uint? Player;public Key Mode;public IGameInstance? Game;public List<uint> Left=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  switch(method!.Name){case "get_AssociatedPlayerId":return Player;case "GetGameInstance":return Game;case "GetGameMode":return Mode;case "PlayerLeftInstance":Left.Add((uint)args![0]!);break;}
  var type=method.ReturnType;return type==typeof(void)||!type.IsValueType?null:Activator.CreateInstance(type);
 }
}
