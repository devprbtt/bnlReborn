using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.ProtocolHelpers;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
var stats=new Dictionary<PlayerMatchStatType,int>{{PlayerMatchStatType.Destroyed,1437},{PlayerMatchStatType.Earned,343},{PlayerMatchStatType.ZonesCaptured,2},{PlayerMatchStatType.ZoneTimeSeconds,150}};
DeathmatchFollowup.AddDestruction(stats,new(){{ScoreType.WorldDestroyedResource,10},{ScoreType.BlocksDestroyedResource,20},{ScoreType.DevicesDestroyedResource,30},{ScoreType.HeroBlocksDestroyedResource,40},{ScoreType.DamagePlayerByHero,1437}});
Check(stats[PlayerMatchStatType.Destruction]==100 && stats[PlayerMatchStatType.Destroyed]==1437 && stats[PlayerMatchStatType.Earned]==343 && stats[PlayerMatchStatType.ZonesCaptured]==2,"destruction augments combat and Conquest statistics");
Check(DeathmatchFollowup.AddDestruction(null,null)[PlayerMatchStatType.Destruction]==0,"no destruction produces explicit zero");
using(var wire=new MemoryStream()) {
 new EndMatchPlayerStats{Stats=stats}.Write(new BinaryWriter(wire));wire.Position=0;var copy=EndMatchPlayerStats.ReadRecord(new BinaryReader(wire));
 Check(copy.Stats![PlayerMatchStatType.Destruction]==100 && copy.Stats[PlayerMatchStatType.Destroyed]==1437,"new destruction wire stat preserves damage");
}
using(var wire=new MemoryStream()) {
 new ZoneUpdate{BlockOwnersJson="{\"blocks\":[{\"x\":1,\"y\":2,\"z\":3,\"owner\":42}]}",ResourceCap=1000}.Write(new BinaryWriter(wire));wire.Position=0;
 var packet=ZoneUpdate.ReadRecord(new BinaryReader(wire));Check(packet.BlockOwnersJson!.Contains("42") && packet.ResourceCap==1000 && wire.Position==wire.Length,"block-owner snapshot round trip");
}
foreach(var target in new uint?[]{1,2,null})foreach(var owner in new uint?[]{1,2,null}) {
 bool same=target.HasValue && owner.HasValue && target==owner;
 Check(Unit.DoesCombatRelationshipApply(true,RelativeTeamType.Opponent,TeamType.Neutral,target,TeamType.Neutral,owner)==!same,"FFA trap relation uses placer ownership");
}
Check(Unit.DoesCombatRelationshipApply(false,RelativeTeamType.Opponent,TeamType.Team1,1,TeamType.Team2,1),"normal team traps retain team rules");
var region=(RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(RegionServerDatabase));
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
typeof(RegionServerDatabase).GetField("_waitingArenaInstanceId",flags)!.SetValue(region,"arena");
var infoType=typeof(RegionServerDatabase).GetNestedType("ConnectionInfo",BindingFlags.NonPublic)!;
var info=Activator.CreateInstance(infoType,Guid.NewGuid(),new ChatPlayer(),false)!;
void Set(string name,object? value)=>infoType.GetProperty(name)!.SetValue(info,value);
bool CanChat()=>(bool)typeof(RegionServerDatabase).GetMethod("CanUseGlobalChat",flags)!.Invoke(region,[info])!;
Set("ActiveScene",new SceneMainMenu());Check(CanChat(),"menu global chat allowed");
Set("GameInstanceId","arena");Set("ActiveScene",new SceneZone());Check(CanChat(),"deathmatch global chat allowed");
Set("ActiveScene",new SceneLobby());Check(CanChat(),"deathmatch hero lobby global chat allowed");
Set("GameInstanceId","normal-match");Check(!CanChat(),"normal match cannot use global chat");
Set("GameInstanceId","arena");Set("Online",false);Check(!CanChat(),"offline arena user cannot chat");
var json=JsonSerializer.Serialize(new PublicQueuePlayer(1,"Player",123,"Casual",true),JsonHelper.DefaultSerializerSettings);
Check(JsonDocument.Parse(json).RootElement.GetProperty("in_deathmatch").GetBoolean() && JsonDocument.Parse(json).RootElement.GetProperty("joined_at").GetInt64()==123,"public queue advertises arena without changing queue age");
var catalogue=(ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]),JsonHelper.DefaultSerializerSettings)!);
var map=Databases.MapDatabase.LoadMapData(new Key("map_sr2_search_and_destroy"))!;var original=map.BlocksData!.ToArray();
var updater=new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true);
var normal=new MapBinary(map.Schema,map.BlocksData!,map.Size,map.Properties?.PlanePosition??0,updater);
int removed=0,unchanged=0;
var arena=new MapBinary(map.Schema,map.BlocksData!,map.Size,map.Properties?.PlanePosition??0,updater,DeathmatchFollowup.RemoveMapForcefields);
for(int x=0;x<map.Size.x;x++)for(int y=0;y<map.Size.y;y++)for(int z=0;z<map.Size.z;z++) {
 var pos=new Vector3s(x,y,z);
 if(normal[pos].Id==44){if(arena[pos].Id!=0)throw new Exception("forcefield remains");removed++;}
 else {if(normal[pos].Id!=arena[pos].Id)throw new Exception("unrelated terrain changed");unchanged++;}
}
Check(removed>0 && unchanged>0,"actual Search and Destroy forcefields removed; other blocks preserved ("+removed+")");
Check(original.SequenceEqual(map.BlocksData),"shared map source bytes unchanged");
var late=new MapBinary(arena.ToBinary(),map.Properties?.PlanePosition??0,updater);
Check(late.Size==arena.Size,"late join receives valid edited map snapshot");
// Exercise the actual block publication path with separate buffered/immediate services.
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var zd=(ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));zd.BlocksData=arena;
void SetZoneField(string name,object value)=>typeof(GameZone).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(zone,value);
SetZoneField("_zoneData",zd);
SetZoneField("_gameInitiator",RuntimeHelpers.GetUninitializedObject(typeof(WaitingArenaInitiator)));
SetZoneField("<BeginningZoneInitData>k__BackingField",new ZoneInitData());
var immediate=DispatchProxy.Create<BNLReloadedServer.Service.IServiceZone,RecordingZone>();
var buffered=DispatchProxy.Create<BNLReloadedServer.Service.IServiceZone,RecordingZone>();
SetZoneField("_unbufferedZone",immediate);SetZoneField("_serviceZone",buffered);
var publish=typeof(GameZone).GetMethod("DoBlockUpdate",BindingFlags.NonPublic|BindingFlags.Instance)!;
publish.Invoke(zone,new object[]{new Dictionary<Vector3s,BlockUpdate>{{new Vector3s(1,2,3),new BlockUpdate{Id=10}}}});
Check(((RecordingZone)(object)immediate).Calls.SequenceEqual(new[]{"SendUpdateZone","SendBlockUpdates"}),"ownership precedes block creation on the immediate ordered stream");
Check(((RecordingZone)(object)buffered).Calls.Count==0,"no delayed ownership packet is queued");
((RecordingZone)(object)immediate).Calls.Clear();
publish.Invoke(zone,new object[]{new Dictionary<Vector3s,BlockUpdate>()});
Check(((RecordingZone)(object)immediate).Calls.SequenceEqual(new[]{"SendBlockUpdates"}),"unchanged ownership avoids redundant snapshots");
Console.WriteLine($"Deathmatch followup server passed: {checks} checks.");

public class RecordingZone : DispatchProxy
{
 public readonly List<string> Calls=new();
 protected override object? Invoke(MethodInfo? method,object?[]? args){Calls.Add(method!.Name);return method.ReturnType==typeof(bool)?false:null;}
}
