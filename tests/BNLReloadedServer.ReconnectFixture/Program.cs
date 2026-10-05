using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
void Field(object o,string n,object? v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
var updater=(UnitUpdater)RuntimeHelpers.GetUninitializedObject(typeof(UnitUpdater));
Field(updater,"<OnUnitInit>k__BackingField",(OnUnitInit)((_,_)=>{}));
Unit Player(uint id,uint player,bool dead=false)=>new(id,new UnitInit{PlayerId=player,Team=TeamType.Team1,Transform=new ZoneTransform{Position=new(12,15,18)}},updater){IsDead=dead};
var own=Player(17,1);var other=Player(18,2);var dead=Player(19,3,true);
var units=new Dictionary<uint,Unit>{{17,own},{18,other},{19,dead}};
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));Field(zone,"_playerUnits",units);
var snapshot=typeof(GameZone).GetMethod("SendPlayerSnapshots",BindingFlags.NonPublic|BindingFlags.Instance)!;
var service=DispatchProxy.Create<IServiceZone,Packets>();var packets=(Packets)(object)service;
var replacement=DispatchProxy.Create<IServiceZone,Packets>();
snapshot.Invoke(zone,[service,replacement,1u]);
Check(packets.Creates.Count==2,"alive local and remote heroes both included; dead hero omitted");
Check(packets.Creates[0].id==17 && packets.Creates[0].init.Controlled,"replacement client receives ownership of existing hero");
Check(packets.Creates[0].init.Transform!.Position==own.Transform.Position,"rejoin preserves authoritative non-origin position");
Check(!packets.Creates[1].init.Controlled && !own.Controlled,"ownership is recipient-specific, not global");
Check(packets.Updates.SequenceEqual(new uint[]{17,18}),"create followed by complete updates for both heroes");
Check(ReferenceEquals(own.ZoneService,replacement) && units.Count==3,"new service bound without duplicate unit");
packets.Creates.Clear();packets.Updates.Clear();own.IsDead=true;
snapshot.Invoke(zone,[service,replacement,1u]);
Check(packets.Creates.Count==1 && packets.Creates[0].id==18,"dead reconnect waits for authoritative respawn, no ghost hero");
packets.Creates.Clear();packets.Updates.Clear();own.IsDead=false;
snapshot.Invoke(zone,[service,replacement,99u]);
Check(packets.Creates.All(p=>!p.init.Controlled),"spectator does not claim another player's hero");
var instance=(GameInstance)RuntimeHelpers.GetUninitializedObject(typeof(GameInstance));
var connection=typeof(GameInstance).GetNestedType("MatchConnectionInfo",BindingFlags.NonPublic)!;
object Connection(Guid id)=>Activator.CreateInstance(connection,[id,Guid.NewGuid(),TeamType.Team1,null])!;
var old=Connection(Guid.NewGuid());var current=Connection(Guid.NewGuid());
var field=typeof(GameInstance).GetField("_connectedUsers",BindingFlags.NonPublic|BindingFlags.Instance)!;
var users=Activator.CreateInstance(field.FieldType)!;field.SetValue(instance,users);
var item=field.FieldType.GetProperty("Item")!;item.SetValue(users,old,[1u]);
var guard=typeof(GameInstance).GetMethod("IsCurrentConnection",BindingFlags.NonPublic|BindingFlags.Instance)!;
Check((bool)guard.Invoke(instance,[1u,old])!,"queued work accepts current session");
item.SetValue(users,current,[1u]);
Check(!(bool)guard.Invoke(instance,[1u,old])! && (bool)guard.Invoke(instance,[1u,current])!,"delayed old disconnect/load work rejected after replacement");
Console.WriteLine("RECONNECT_FIXTURE_OK checks="+checks);

public class Packets:DispatchProxy{
 public List<(uint id,UnitInit init)> Creates=[];public List<uint> Updates=[];
 protected override object? Invoke(MethodInfo? m,object?[]? args){
  if(m!.Name=="SendUnitCreate")Creates.Add(((uint)args![0]!, (UnitInit)args[1]!));
  if(m.Name=="SendUnitUpdate")Updates.Add((uint)args![0]!);
  return null;
 }
}
