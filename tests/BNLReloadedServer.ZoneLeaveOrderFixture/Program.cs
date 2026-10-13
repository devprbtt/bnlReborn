using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;

// A player leaving a match must stop receiving its lobby/zone broadcasts before the client is sent
// out of the match scene; otherwise in-flight BlockUpdates reach a client with no zone listener.
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}

// 1. RegionServerDatabase.UpdateScene detaches before the scene change, only when leaving to the main menu.
foreach(var (scene,expectDetach) in new (Scene,bool)[]{(new SceneMainMenu(),true),(new SceneZone(),false)})
{
 var events=new List<string>();
 var instance=DispatchProxy.Create<IGameInstance,Stub>();
 ((Stub)(object)instance).Call=(m,a)=>{events.Add(m+":"+a![0]);return null;};
 var sceneService=DispatchProxy.Create<IServiceScene,Stub>();
 ((Stub)(object)sceneService).Call=(m,a)=>{events.Add(m);return null;};
 var region=(RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(RegionServerDatabase));
 FillCollections(region);
 Field(region,"_playerDatabase").SetValue(region,DispatchProxy.Create<IPlayerDatabase,Stub>().With((m,a)=>throw new InvalidOperationException("friends lookup not under test")));
 var infoType=typeof(RegionServerDatabase).GetNestedType("ConnectionInfo",BindingFlags.NonPublic)!;
 var info=Activator.CreateInstance(infoType,Guid.NewGuid(),RuntimeHelpers.GetUninitializedObject(typeof(ChatPlayer)),false)!;
 infoType.GetProperty("GameInstanceId")!.SetValue(info,"inst-1");
 ((IDictionary)Field(region,"_connectedUsers").GetValue(region)!)[42u]=info;
 ((IDictionary)Field(region,"_gameInstances").GetValue(region)!)["inst-1"]=instance;
 region.UpdateScene(42,scene,sceneService,false);
 var name=scene.GetType().Name;
 Console.WriteLine("  "+name+": "+string.Join(" > ",events));
 var change=events.IndexOf("SendChangeScene");
 Check(change>=0,name+": scene change sent");
 if(expectDetach)
 {
  var detach=events.IndexOf("DetachFromZoneStreams:42");
  Check(detach>=0 && detach<change,name+": streams detached before the scene change");
  Check(events.IndexOf("PlayerLeftInstance:42")>change,name+": instance departure still follows the scene change");
 }
 else Check(!events.Any(e=>e.StartsWith("DetachFromZoneStreams")),name+": no detach when staying in the match");
}

// 2. GameInstance.PlayerLeftInstance detaches the session itself instead of leaving it to queued
// lobby/zone work. Lobby and Zone are null here, so only a synchronous detach can pass.
{
 var game=(GameInstance)RuntimeHelpers.GetUninitializedObject(typeof(GameInstance));
 FillCollections(game);
 var zoneSender=new SessionSender(null!);var lobbySender=new SessionSender(null!);
 Field(game,"_zoneSender").SetValue(game,zoneSender);Field(game,"_lobbySender").SetValue(game,lobbySender);
 Field(game,"_serverDatabase").SetValue(game,DispatchProxy.Create<IRegionServerDatabase,Stub>().With((m,a)=>null));
 Field(game,"<GameInitiator>k__BackingField").SetValue(game,DispatchProxy.Create<IGameInitiator,Stub>().With((m,a)=>null));
 var connType=typeof(GameInstance).GetNestedType("MatchConnectionInfo",BindingFlags.NonPublic)!;
 var users=(IDictionary)Field(game,"_connectedUsers").GetValue(game)!;
 Guid leaver=Guid.NewGuid(),stayer=Guid.NewGuid();
 users[42u]=Activator.CreateInstance(connType,leaver,Guid.NewGuid(),TeamType.Team1,null)!;
 users[43u]=Activator.CreateInstance(connType,stayer,Guid.NewGuid(),TeamType.Team2,null)!; // keeps the instance alive
 foreach(var s in new[]{zoneSender,lobbySender}){Sessions(s)[leaver]=null!;Sessions(s)[stayer]=null!;}

 game.DetachFromZoneStreams(99); // unknown player: no-op
 Check(zoneSender.SenderCount==2 && lobbySender.SenderCount==2,"unknown player detaches nobody");

 game.PlayerLeftInstance(42,KickReason.MatchQuit);
 Check(!Sessions(zoneSender).ContainsKey(leaver),"leaver detached from the zone stream immediately");
 Check(!Sessions(lobbySender).ContainsKey(leaver),"leaver detached from the lobby stream immediately");
 Check(Sessions(zoneSender).ContainsKey(stayer) && Sessions(lobbySender).ContainsKey(stayer),"remaining player still subscribed");

 game.DetachFromZoneStreams(43);
 Check(!Sessions(zoneSender).ContainsKey(stayer) && !Sessions(lobbySender).ContainsKey(stayer),"DetachFromZoneStreams detaches a connected player");
 Check(users.Contains(43u),"DetachFromZoneStreams does not remove the player from the instance");
}
Console.WriteLine($"ZONE_LEAVE_ORDER PASS {checks} checks");

static FieldInfo Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingFieldException(o.GetType().Name,name);
static IDictionary<Guid,AsyncSenderTask> Sessions(SessionSender s)=>(IDictionary<Guid,AsyncSenderTask>)typeof(SessionSender).GetField("_sessions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
// GetUninitializedObject skips field initializers; recreate the empty collections the code under test touches.
static void FillCollections(object o)
{
 foreach(var f in o.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
  if(f.GetValue(o)==null && f.FieldType.IsGenericType && f.FieldType.Name.StartsWith("ConcurrentDictionary"))
   f.SetValue(o,Activator.CreateInstance(f.FieldType));
}
public class Stub:DispatchProxy {
 public Func<string,object?[]?,object?> Call=null!;
 protected override object? Invoke(MethodInfo? m,object?[]? a){
  var r=Call(m!.Name,a);
  if(r==null && m.ReturnType.IsValueType && m.ReturnType!=typeof(void))return Activator.CreateInstance(m.ReturnType);
  return r;
 }
}
public static class StubExt{public static T With<T>(this T proxy,Func<string,object?[]?,object?> call) where T:class{((Stub)(object)proxy).Call=call;return proxy;}}
