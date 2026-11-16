using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ControlPanel;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;
using Moserware.Skills;
const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
Check(MapBuildPhasePolicy.Seconds(1)==180,"one-cube map: 3 minutes");
Check(MapBuildPhasePolicy.Seconds(2)==240,"two-cube map: 4 minutes");
Check(MapBuildPhasePolicy.Seconds(3)==360,"three-cube map: 6 minutes");
Check(MapBuildPhasePolicy.Seconds(0)==null,"non-objective map retains existing duration");
var mm=(Matchmaker)RuntimeHelpers.GetUninitializedObject(typeof(Matchmaker));
var field=typeof(Matchmaker).GetField("_queues",Hidden)!;
var queues=Activator.CreateInstance(field.FieldType)!;field.SetValue(mm,queues);
var service=DispatchProxy.Create<IServiceMatchmaker,Capture>();var capture=(Capture)(object)service;
mm.RefreshIdleState(7,service);Check(capture.Updates.Single().State?.State==MatchmakerStateType.None,"menu return clears stale client queue state");
var qtype=typeof(Matchmaker).GetNestedType("QueueData",BindingFlags.NonPublic)!;
var q=RuntimeHelpers.GetUninitializedObject(qtype);
qtype.GetProperty("Players")!.SetValue(q,new List<PlayerQueueData>{new(7,Guid.NewGuid(),new Rating(25,8),DateTimeOffset.UtcNow,null,new Key("fixture"))});
queues.GetType().GetMethod("TryAdd")!.Invoke(queues,[new Key("fixture"),q]);
capture.Updates.Clear();mm.RefreshIdleState(7,service);Check(capture.Updates.Count==0,"real queue/acceptance state is preserved");
mm.RefreshIdleState(8,service);Check(capture.Updates.Single().State?.State==MatchmakerStateType.None,"another player's queue does not block idle reconciliation");
var sender=DispatchProxy.Create<ISender,Capture>();var sent=(Capture)(object)sender;sent.Player=77;
var scene=DispatchProxy.Create<IServiceScene,Capture>();var time=DispatchProxy.Create<IServiceTime,Capture>();
var playerService=(ServicePlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServicePlayer));
foreach(var f in typeof(ServicePlayer).GetFields(Hidden).Where(f=>f.FieldType==typeof(ISender)))f.SetValue(playerService,sender);
var database=DispatchProxy.Create<IRegionServerDatabase,Capture>();var db=(Capture)(object)database;
typeof(ServicePlayer).GetField("_serverDatabase",Hidden)!.SetValue(playerService,database);
using var reader=new BinaryReader(new MemoryStream());
var start=typeof(ServicePlayer).GetMethod("ReceiveStartTutorial",Hidden)!;
start.Invoke(playerService,[reader]);Check(db.Tutorials.SequenceEqual(new uint[]{77}),"Practice Grounds wire handler starts authenticated player's tutorial");
sent.Player=null;start.Invoke(playerService,[reader]);Check(db.Tutorials.Count==1,"unauthenticated tutorial request ignored");
var route=typeof(ControlPanelServer).GetMethod("IsPublicReadRequest",BindingFlags.Static|BindingFlags.NonPublic)!;
Check((bool)route.Invoke(null,["GET","/api/replays/9223372036854775809"])!,"shared replay metadata read uses public archive access");
Check(!(bool)route.Invoke(null,["POST","/api/replays/9223372036854775809"])!,"replay metadata does not enable public writes");
Console.WriteLine("NINE_TOPICS_SERVER_OK checks="+checks);
public class Capture:DispatchProxy
{
 public uint? Player;
 public List<MatchmakerUpdate> Updates=[];public List<uint> Tutorials=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  if(method!.Name=="get_AssociatedPlayerId")return Player;
  if(method.Name=="SendMatchmakerUpdate")Updates.Add((MatchmakerUpdate)args![0]!);
  if(method.Name=="StartTutorialGame"){Tutorials.Add((uint)args![0]!);return true;}
  var type=method.ReturnType;return type==typeof(void)||!type.IsValueType?null:Activator.CreateInstance(type);
 }
}
