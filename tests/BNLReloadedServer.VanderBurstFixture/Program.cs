using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType=BNLReloadedServer.BaseTypes.MatchType;

const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
var players=new ConcurrentDictionary<uint,PlayerLobbyState>();
players[1]=new(){PlayerId=1,Team=TeamType.Team1,Hero=new Key("unit_hero_magnus"),Nickname="fixture"};
var map=new MapData {Match=MatchType.ShieldCapture,Properties=new(),Size=new Vector3s(32,16,32),BlocksData=new byte[32*16*32*6].Zip(0).ToArray(),SpawnPoints=[
 new(){Team=TeamType.Team1,Label=SpawnPointLabel.Base,Position=new(4,4,4)},new(){Team=TeamType.Team2,Label=SpawnPointLabel.Base,Position=new(24,4,24)}]};
var service=Stub<IServiceZone>();
var zone=new GameZone(service,service,Stub<IBuffer>(),Stub<ISender>(new(){["get_SenderCount"]=1}),map,
 Stub<IGameInitiator>(new(){["GetGameMode"]=new Key("game_mode_friendly"),["get_GameInstanceId"]="vander-burst"}),players);
var completion=new TaskCompletionSource();
zone.EnqueueAction(()=>{try{
 var unit=(Unit)typeof(GameZone).GetMethod("CreatePlayerUnit",Hidden)!.Invoke(zone,[1u,service])!;unit.ZoneService=service;
 var data=(ZoneData)typeof(GameZone).GetField("_zoneData",Hidden)!.GetValue(zone)!;data.Phase=new(){PhaseType=ZonePhaseType.Assault};
 ulong id=500000;
 foreach(string name in new[]{"gear_magnus_ball_lightning","gear_magnus_ball_lightning_ball_to_the_wall"}){
  var gear=unit.GetGearByKey(new Key(name));if(gear==null){gear=new GearData(unit,new Key(name),unit.Gears.Count);unit.Gears.Add(gear);}unit.SetGear(gear.Key);
  var tool=gear.Tools[1];Check(VanderBallLightning.IsBurst(tool)&&!VanderBallLightning.IsBurst(gear.Tools[0]),"only RMB is special");
  Check(((ToolShot)tool.Tool!).HitEffect is InstEffectSplashDamage{Damage.PlayerDamage:12},"per-ball damage unchanged");
  var shots=(Dictionary<ulong,ShotInfo>)typeof(GameZone).GetField("_shotInfo",Hidden)!.GetValue(zone)!;
  for(int ammo=0;ammo<=6;ammo++){
   int count=Math.Min(ammo,5);gear.Ammo[0].Mag=ammo;int before=shots.Count;
   var cast=new CastData{ToolIndex=1,ShotPos=new(4,4,4),Shots=Enumerable.Range(0,Math.Max(1,count)).Select(_=>new ShotData{ShotId=id++,TargetPos=new(5,4,4)}).ToList()};
   zone.ReceivedCastRequest(1,cast);
   Check(gear.Ammo[0].Mag==ammo-count,"exact partial ammo spent");
   Check(shots.Count==before+count,"server registers exactly affordable projectiles");
  }
  gear.Ammo[0].Mag=2;int prior=shots.Count;
  zone.ReceivedCastRequest(1,new(){ToolIndex=1,ShotPos=new(4,4,4),Shots=Enumerable.Range(0,5).Select(_=>new ShotData{ShotId=id++,TargetPos=new(5,4,4)}).ToList()});
  Check(gear.Ammo[0].Mag==2&&shots.Count==prior,"forged five balls with two ammo rejected");
  zone.ReceivedCastRequest(1,new(){ToolIndex=1,ShotPos=new(4,4,4),Shots=[new(){ShotId=id,TargetPos=new(5,4,4)},new(){ShotId=id,TargetPos=new(5,4,4)}]});
  Check(gear.Ammo[0].Mag==2&&shots.Count==prior,"duplicate shot IDs rejected");
 }
 Check(VanderBallBurstMath.Count(0,0)==5,"infinite-ammo full burst");
 foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1})Check(VanderBallBurstMath.Count(invalid,1)==0,"invalid/negative ammo blocked");
 completion.SetResult();
 }catch(Exception e){completion.SetException(e);}});
try{completion.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();}finally{zone.Stop();}
Console.WriteLine("VANDER_BURST PASS "+checks+" checks");
static T Stub<T>(Dictionary<string,object?>? returns=null)where T:class{var proxy=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)proxy).Returns=returns??[];return proxy;}
public class StubProxy:DispatchProxy{
 public Dictionary<string,object?> Returns=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args){if(Returns.TryGetValue(method!.Name,out var value))return value;var type=method.ReturnType;if(type==typeof(void))return null;if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";return type.IsValueType?Activator.CreateInstance(type):null;}
}
