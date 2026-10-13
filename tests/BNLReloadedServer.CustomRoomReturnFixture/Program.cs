using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
foreach(var method in new[]{"ReceiveExitMatch","ReceiveZoneLeave"})
foreach(var scenario in new[]{"room survives","room closed","room closes during exit","public match","spectator"})
{
 var events=new List<string>();bool exists=scenario!="room closed" && scenario!="public match";
 var room=new CustomGameUpdate{GameName="private custom",Password="fixture-only",Players=[new(){Id=42,Owner=true,Team=TeamType.Team2}],Status=CustomGameStatus.Match};
 var instance=DispatchProxy.Create<IGameInstance,Stub>();
 ((Stub)(object)instance).Call=(m,a)=>{if(m=="PlayerLeftInstance") {Check((uint)a![0]! ==42 && (KickReason)a[1]! ==KickReason.MatchQuit,"correct departing player and reason");events.Add("detach");if(scenario=="room closes during exit" || scenario=="spectator")exists=false;}return null;};
 var db=DispatchProxy.Create<IRegionServerDatabase,Stub>();
 ((Stub)(object)db).Call=(m,a)=>m switch {
  "GetGameInstance"=>instance,
  "GetFullCustomGameUpdate"=>RecordRoom(),
  "RemoveFromCustomGame"=>RemoveRoom(),
  _=>null
 };
 object? RecordRoom(){events.Add("lookup");return exists?room:null;}
 object RemoveRoom(){events.Add("remove");return true;}
 Databases.SetRegionDatabase(db);
 var service=new ServiceZone(new Sender{AssociatedPlayerId=42});
 typeof(ServiceZone).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[new BinaryReader(new MemoryStream())]);
 Check(events.SequenceEqual(exists?new[]{"detach","lookup"}:new[]{"detach","lookup","remove"}),method+" "+scenario);
 if(exists)Check(room.Players![0].Owner && room.Players[0].Team==TeamType.Team2 && room.Password=="fixture-only","membership/team/ownership preserved");
}
// Explicit Leave Room remains a separate command and always removes membership.
var explicitCalls=0;var explicitDb=DispatchProxy.Create<IRegionServerDatabase,Stub>();
((Stub)(object)explicitDb).Call=(m,a)=>{if(m=="RemoveFromCustomGame")explicitCalls++;return true;};
Databases.SetRegionDatabase(explicitDb);
var matchmaker=new ServiceMatchmaker(new Sender{AssociatedPlayerId=42});
typeof(ServiceMatchmaker).GetMethod("ReceiveLeaveCustomGame",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(matchmaker,[new BinaryReader(new MemoryStream())]);
Check(explicitCalls==1,"explicit Leave Room still leaves membership");
Console.WriteLine($"CUSTOM_ROOM_RETURN PASS {checks} checks");
public class Stub:DispatchProxy {
 public Func<string,object?[]?,object?> Call=null!;
 protected override object? Invoke(MethodInfo? m,object?[]? a)=>Call(m!.Name,a);
}
public class Sender:ISender {
 public uint? AssociatedPlayerId{get;set;}public int SenderCount=>1;
 public void Send(BinaryWriter w){}public void Send(byte[] b){}public void SendExcept(BinaryWriter w,List<Guid> e){}
 public void Subscribe(Guid g){}public void Unsubscribe(Guid g){}public void UnsubscribeAll(){}
}
