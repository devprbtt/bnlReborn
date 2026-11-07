using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Service;
using BNLReloadedServer.Servers;

if(args.Length<2)throw new ArgumentException("catalogue JSON and output directory required");
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var cards=JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]),JsonHelper.DefaultSerializerSettings)!;
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
var logic=CatalogueHelper.GlobalLogic.TimeTrial!;var courses=logic.Courses!;
Check(courses.Count>=6,"all six configured courses present");
Check(ReferenceEquals(TimeAssaultCourses.Select(courses,null),courses[0]),"legacy request retains featured course");
Check(TimeAssaultCourses.Select(courses,new Key("not_a_course"))==null,"unknown selection never falls back");
Check(TimeAssaultCourses.Select(null,null)==null && TimeAssaultCourses.Select([],null)==null,"empty catalogue safe");
Check(TimeAssaultCourses.Select(courses.Append(courses[1]),courses[1].Map)==null,"ambiguous duplicate course rejected");
Directory.CreateDirectory(args[1]);
using(var writer=new BinaryWriter(File.Create(Path.Combine(args[1],"time-trial-logic.bin"))))logic.Write(writer);
using(var writer=new BinaryWriter(File.Create(Path.Combine(args[1],"time-trial-cards.bin"))))
{
    var keys=courses.SelectMany(c=>new[]{c.Map,c.Hero}).ToHashSet();
    foreach(var course in courses)
        if(course.Hero.GetCard<CardUnit>()?.Data is UnitDataPlayer hero && hero.Skins is {Count:>0})keys.Add(hero.Skins[0]);
    var selectedCards=cards.Where(c=>keys.Contains(c.Key)).ToArray();writer.Write(selectedCards.Length);
    foreach(var card in selectedCards)Card.WriteVariant(writer,card);
}
var region=DispatchProxy.Create<IRegionServerDatabase,RegionProxy>();Databases.SetRegionDatabase(region);
var sender=new Sender();var service=new ServiceMatchmaker(sender);
foreach(var course in courses)
{
 Check(ReferenceEquals(TimeAssaultCourses.Select(courses,course.Map),course),"select exact course "+course.Map.GetCard<CardMap>()!.Id);
 Check(TimeAssaultCourses.TryLoad(course.Map,out var chosen,out var map)&&ReferenceEquals(chosen,course)&&map!=null,"load actual course payload");
 Check(chosen!.Hero==course.Hero && chosen.Devices==course.Devices && chosen.Goals==course.Goals && chosen.MatchObjectives==course.MatchObjectives,"retain course hero, loadout, rewards and objectives");
 using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((ushort)123);Key.WriteRecord(writer,course.Map);stream.Position=0;
 typeof(ServiceMatchmaker).GetMethod("ReceiveStartTimeTrialCourse",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[new BinaryReader(stream)]);
 var proxy=(RegionProxy)(object)region;
 Check(proxy.Player==7 && proxy.Map==course.Map,"selected map travels through service dispatcher");
 using var response=new BinaryReader(new MemoryStream(sender.Last!));
 Check(response.ReadByte()==(byte)ServiceId.ServiceMatchmaker && response.ReadByte()==35 && response.ReadUInt16()==123 && response.ReadByte()==0 && response.ReadBoolean() && response.BaseStream.Position==response.BaseStream.Length,"start reply contract");
}
((RegionProxy)(object)region).Started=false;
using(var stream=new MemoryStream()){
 using var writer=new BinaryWriter(stream);writer.Write((ushort)124);Key.WriteRecord(writer,courses[1].Map);stream.Position=0;
 typeof(ServiceMatchmaker).GetMethod("ReceiveStartTimeTrialCourse",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[new BinaryReader(stream)]);
 Check(sender.Last![^1]==0,"failed start is reported to picker");
}
var wanted=courses[1];var originalMap=wanted.Map;wanted.Map=new Key("missing_payload");
Check(!TimeAssaultCourses.TryLoad(wanted.Map,out _,out _),"removed map safely rejected");wanted.Map=originalMap;
// Exercise real region guards without creating sockets or a running zone.
const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
var actual=(RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(RegionServerDatabase));
var infoType=typeof(RegionServerDatabase).GetNestedType("ConnectionInfo",BindingFlags.NonPublic)!;
var info=Activator.CreateInstance(infoType,Guid.NewGuid(),new ChatPlayer(),false)!;
var connections=Activator.CreateInstance(typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(uint),infoType))!;
connections.GetType().GetMethod("TryAdd")!.Invoke(connections,[7u,info]);
typeof(RegionServerDatabase).GetField("_connectedUsers",fields)!.SetValue(actual,connections);
Check(!actual.StartTimeTrialCourse(99,courses[1].Map,service),"disconnected user cannot launch");
Check(!actual.StartTimeTrialCourse(7,new Key("forged_course"),service),"invalid request cannot touch queue or start a zone");
infoType.GetProperty("SquadId")!.SetValue(info,1UL);
Check(!actual.StartTimeTrialCourse(7,courses[1].Map,service),"squad member cannot launch solo course");
infoType.GetProperty("SquadId")!.SetValue(info,null);
infoType.GetProperty("GameInstanceId")!.SetValue(info,"current");
Check(!actual.StartTimeTrialCourse(7,courses[1].Map,service),"second launch cannot replace an active instance");
var current=DispatchProxy.Create<IGameInstance,InstanceProxy>();var instance=(InstanceProxy)(object)current;
typeof(RegionServerDatabase).GetField("_gameInstances",fields)!.SetValue(actual,new ConcurrentDictionary<string,IGameInstance>{["current"]=current});
instance.Started=false;
Check(!actual.RestartTimeTrialGame(7)&&instance.Left==0,"restart while loading is ignored");
instance.Started=true;instance.Map=new Key("removed_course");
Check(!actual.RestartTimeTrialGame(7)&&instance.Left==0 && (string?)infoType.GetProperty("GameInstanceId")!.GetValue(info)=="current","invalid restart keeps current run, never falls back to featured map");
foreach(var course in courses.Skip(1))
{
    instance.Map=course.Map;instance.Left=0;infoType.GetProperty("GameInstanceId")!.SetValue(info,"current");
    try { actual.RestartTimeTrialGame(7);throw new Exception("Expected isolated teardown boundary"); }
    catch(StopBeforeZoneCreation) { Check(instance.Left==1 && infoType.GetProperty("GameInstanceId")!.GetValue(info)==null,"valid restart resolves non-featured course before teardown"); }
}
// The actual start guard and nonce acceptance, without sockets or a live zone.
var game=(GameInstance)RuntimeHelpers.GetUninitializedObject(typeof(GameInstance));
var connectionType=typeof(GameInstance).GetNestedType("MatchConnectionInfo",BindingFlags.NonPublic)!;
var connection=Activator.CreateInstance(connectionType,Guid.NewGuid(),Guid.NewGuid(),TeamType.Team1,null)!;
var roster=Activator.CreateInstance(typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(uint),connectionType))!;
typeof(GameInstance).GetField("_connectedUsers",fields)!.SetValue(game,roster);
typeof(GameInstance).GetProperty("MapData",fields)!.SetValue(game,new MapData{Match=BNLReloadedServer.BaseTypes.MatchType.TimeTrial});
bool Ready()=>(bool)typeof(GameInstance).GetMethod("AllPlayersLoaded",fields)!.Invoke(game,null)!;
bool Ack(object conn,ulong token)=>(bool)typeof(GameInstance).GetMethod("AcceptTimeAssaultReady",fields)!.Invoke(game,[7u,conn,token])!;
void Stage(BNLReloadedServer.ServerTypes.ZoneLoadStage stage)=>connectionType.GetProperty("LoadStage")!.SetValue(connection,stage);
void Token(ulong? token)=>connectionType.GetProperty("TimeAssaultReadyToken")!.SetValue(connection,token);
void CannotForceStart(string why){typeof(GameInstance).GetMethod("BeginGame",fields)!.Invoke(game,null);Check(!game.IsStarted,why);}
Check(!Ready(),"empty instance cannot start");CannotForceStart("timeout cannot start empty TA");
roster.GetType().GetMethod("TryAdd")!.Invoke(roster,[7u,connection]);
foreach(var stage in new[]{BNLReloadedServer.ServerTypes.ZoneLoadStage.None,BNLReloadedServer.ServerTypes.ZoneLoadStage.InitZone,BNLReloadedServer.ServerTypes.ZoneLoadStage.LoadZone}){
 Stage(stage);Token(123);Check(!Ready()&&!Ack(connection,123),"no start/ack before snapshot delivery: "+stage);CannotForceStart("timeout cannot bypass loading: "+stage);
}
Stage(BNLReloadedServer.ServerTypes.ZoneLoadStage.Finished);Token(123);
Check(!Ready(),"snapshot delivered does not start TA before client presentation");CannotForceStart("timeout cannot bypass final client acknowledgement");
Check(!Ack(connection,999)&&!Ready(),"stale/forged token cannot release countdown");
var oldConnection=Activator.CreateInstance(connectionType,Guid.NewGuid(),Guid.NewGuid(),TeamType.Team1,null)!;
connectionType.GetProperty("LoadStage")!.SetValue(oldConnection,BNLReloadedServer.ServerTypes.ZoneLoadStage.Finished);
connectionType.GetProperty("TimeAssaultReadyToken")!.SetValue(oldConnection,123UL);
Check(!Ack(oldConnection,123)&&!Ready(),"replaced connection cannot release countdown");
Check(Ack(connection,123)&&Ready(),"matching final readiness releases countdown");
Check(!Ack(connection,123),"duplicate readiness is ignored");
Token(456);Check(!Ack(connection,123)&&!Ready(),"new load rejects prior readiness token");
Token(null);Check(Ready(),"legacy ready clients retain native handshake");
var zoneService=new ServiceZone(sender);zoneService.SendTimeAssaultReady(0x123456789ABCDEF0UL);
using(var reply=new BinaryReader(new MemoryStream(sender.Last!))){Check(reply.ReadByte()==(byte)ServiceId.ServiceZone && reply.ReadByte()==114 && reply.ReadUInt64()==0x123456789ABCDEF0UL && reply.BaseStream.Position==reply.BaseStream.Length,"readiness challenge wire contract");}
using(var parityWriter=new BinaryWriter(File.Create(Path.Combine(args[1],"completion-parity.bin"))))
{
    var samples=new[]{145.25f,12.1f,.005f,59.9996f,60f,3600.125f};
    parityWriter.Write(samples.Length);
    foreach(var seconds in samples)
    {
        var player=new PlayerData();
        var result=new TimeTrialResultData{ResultTime=seconds,BestResultTime=seconds};
        typeof(MasterServerDatabase).GetMethod("ApplyTimeTrialResult",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[player,courses[0].Map,result]);
        var saved=TimeTrialData.ReadByteRecord(TimeTrialData.WriteByteRecord(player.TimeTrial));
        var board=new TtLeaderboardRecord{PlayerId=7,PlayerName="Parity runner",ResultSeconds=saved.BestResultTime![courses[0].Map]};
        using var wire=new MemoryStream();using var writer=new BinaryWriter(wire);
        result.Write(writer);board.Write(writer);wire.Position=0;
        using var reader=new BinaryReader(wire);
        var finish=TimeTrialResultData.ReadRecord(reader);var leaderboard=TtLeaderboardRecord.ReadRecord(reader);
        Check(BitConverter.SingleToInt32Bits(finish.ResultTime)==BitConverter.SingleToInt32Bits(leaderboard.ResultSeconds),"completion and persisted leaderboard retain identical float bits: "+seconds);
        result.Write(parityWriter);board.Write(parityWriter);
        typeof(MasterServerDatabase).GetMethod("ApplyTimeTrialResult",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[player,courses[0].Map,new TimeTrialResultData{ResultTime=seconds+10,BestResultTime=seconds}]);
        Check(player.TimeTrial.BestResultTime![courses[0].Map]==seconds,"slower finish preserves best leaderboard time: "+seconds);
    }
}
Console.WriteLine($"Time Assault fixture passed: {checks} checks.");
File.WriteAllText(Path.Combine(args[1],"server-validation.txt"),$"PASS {checks} checks; all {courses.Count} actual map payloads load; course selection/loadout/objectives/rewards preserved; request/reply round trip.\n");
public class RegionProxy:DispatchProxy{
 public Key Map;public uint Player;public bool Started=true;
 protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name==nameof(IRegionServerDatabase.StartTimeTrialCourse)){Player=(uint)args![0]!;Map=(Key)args[1]!;return Started;}throw new NotSupportedException(method.Name);}
}
public class Sender:ISender{
 public byte[]? Last;public uint? AssociatedPlayerId{get;set;}=7;public int SenderCount=>1;
 public void Send(BinaryWriter writer)=>Last=((MemoryStream)writer.BaseStream).ToArray();public void Send(byte[] bytes)=>Last=bytes;
 public void SendExcept(BinaryWriter writer,List<Guid> excluded)=>Send(writer);public void Subscribe(Guid id){}public void Unsubscribe(Guid id){}public void UnsubscribeAll(){}
}
public class StopBeforeZoneCreation:Exception{}
public class InstanceProxy:DispatchProxy{
 public bool Started;public Key Map;public int Left;
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch{
  "get_IsStarted"=>Started,
  nameof(IGameInstance.GetGameMode)=>CatalogueHelper.ModeTimeTrial.Key,
  nameof(IGameInstance.GetMapInfo)=>new MapInfoCard{MapKey=Map},
  nameof(IGameInstance.PlayerLeftInstance)=>Stop(),
  _=>throw new NotSupportedException(method.Name)
 };
 private object Stop(){Left++;throw new StopBeforeZoneCreation();}
}
