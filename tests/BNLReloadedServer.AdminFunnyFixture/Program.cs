using System.Collections.Concurrent;
using MatchType=BNLReloadedServer.BaseTypes.MatchType;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
int checks=0;
void Check(bool condition,string name) {if(!condition) throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
Check(!AdminFunnyRules.IsAdmin(PlayerRole.User),"user cannot enable");
Check(!AdminFunnyRules.IsAdmin(PlayerRole.Moderator),"moderator cannot enable");
Check(AdminFunnyRules.IsAdmin(PlayerRole.Admin)&&AdminFunnyRules.IsAdmin(PlayerRole.Core),"admin/core allowed");
foreach(var direction in new[]{new Vector3(float.NaN,0,1),new Vector3(float.PositiveInfinity,0,1),Vector3.Zero,new Vector3(0,0,9)})
 Check(!AdminFunnyRules.ValidAim(direction,5,100),"reject invalid direction "+direction);
Check(!AdminFunnyRules.ValidAim(Vector3.UnitZ,float.NaN,100),"reject NaN distance");
Check(!AdminFunnyRules.ValidAim(Vector3.UnitZ,101,100),"reject outside map distance");
Check(AdminFunnyRules.ValidAim(Vector3.UnitZ,99,100),"no short grab range limit");
Check(AdminFunnyRules.Launch(Vector3.UnitZ,2).Z==120,"full charge 120 m/s");
Check(AdminFunnyRules.Launch(Vector3.UnitZ,20)==AdminFunnyRules.Launch(Vector3.UnitZ,2),"charge capped");
Check(AdminFunnyRules.Launch(Vector3.UnitZ,0).Z==15,"tap throw weaker");
var gear=new CardGear {Id="fixture_dig",Tools=[new ToolShot {HitEffect=new InstEffectDamage {Damage=new Damage {Mining=true}}}]};
var weapon=new CardGear {Id="fixture_weapon",Tools=[]};
var hero=new CardUnit {Id="fixture_hero",Data=new UnitDataPlayer(),Size=new Vector3s(1,2,1),PivotType=UnitPivotType.CenterBottom,
 Health=new UnitHealth {Health=new Health {MaxHealth=300,HealthType=HealthType.Player}}};
var markerEffect=new CardEffect {Id="fixture_nameplate_marker",Effect=new ConstEffectBuff {Buffs=new() {[BuffType.AdminHideNameplate]=1,[BuffType.RunSpeed]=.1f}}};
var mode=new CardGameMode {Id="game_mode_friendly"};
((ServerCatalogue)Databases.Catalogue).Replicate([gear,weapon,hero,mode,markerEffect,new CardGameMode {Id="game_mode_custom"},
 new CardMatch {Id="fixture_match",Data=new MatchDataShieldCapture()},new CardGlobalLogic {Id="global_logic"},
 new CardBlock {Id="fixture_wall",BlockId=1,Passable=BlockPassableType.None,Solid=true,Grounded=true},
 new CardBlock {Id="fixture_air",BlockId=0,Passable=BlockPassableType.Any,Transparent=true,LightTransparent=true,SkylightTransparent=true}]);
((UnitDataPlayer)hero.Data!).Gears=[gear.Key,weapon.Key];
var players=new ConcurrentDictionary<uint,PlayerLobbyState>();
foreach(var id in new[]{1u,2u,3u}) players[id]=new PlayerLobbyState {PlayerId=id,Team=id==3?TeamType.Team2:TeamType.Team1,Hero=hero.Key,Nickname="fixture"};
var dbPlayers=(ConcurrentDictionary<uint,PlayerData>)typeof(PlayerDatabase).GetField("_players",Any)!.GetValue(Databases.PlayerDatabase)!;
foreach(var id in players.Keys) dbPlayers[id]=new PlayerData {PlayerId=id,Role=id==1?PlayerRole.Admin:PlayerRole.User};
var map=new MapData {Match=MatchType.ShieldCapture,Properties=new MapDataProps(),Size=new Vector3s(32,32,32),BlocksData=new byte[32*32*32*6].Zip(0).ToArray(),
 SpawnPoints=[new MapSpawnPoint {Team=TeamType.Team1,Label=SpawnPointLabel.Base,Position=new Vector3(8,5,4)},new MapSpawnPoint {Team=TeamType.Team2,Label=SpawnPointLabel.Base,Position=new Vector3(8,5,8)}]};
var service=Stub<IServiceZone>();
var zone=new GameZone(service,Stub<IServiceZone>(),Stub<IBuffer>(),Stub<ISender>(),map,
 Stub<IGameInitiator>(new(){["GetGameMode"]=mode.Key,["get_GameInstanceId"]="funny-fixture"}),players);
object? Call(string name,params object?[] args)=>typeof(GameZone).GetMethod(name,Any)!.Invoke(zone,args);
T OnZone<T>(Func<T> body) {var done=new TaskCompletionSource<T>();zone.EnqueueAction(()=>{try{done.SetResult(body());}catch(Exception e){done.SetException(e);}});return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}
var sender=new CaptureSender(); var adminService=new ServiceZone(sender); var targetSender=new CaptureSender();var targetService=new ServiceZone(targetSender);
void Cap(ServiceZone value,bool supported)=>typeof(ServiceZone).GetProperty("SupportsAdminFunnyMode")!.SetValue(value,supported);
Cap(adminService,true);Cap(targetService,true);
var admin=OnZone(()=> (Unit)Call("CreatePlayerUnit",1u,adminService)!);
var teammate=OnZone(()=> (Unit)Call("CreatePlayerUnit",2u,targetService)!);
var enemy=OnZone(()=> (Unit)Call("CreatePlayerUnit",3u,targetService)!);
var modes=(System.Collections.IDictionary)typeof(GameZone).GetField("_funnyModes",Any)!.GetValue(zone)!;
void Cmd(string action,Unit? target=null,string distance="4")=>zone.ReceivedDebugCommand(1,"admin_funny",[action,(target?.Id??0).ToString(),distance,"0","0","1"]);
void Position(Unit u,Vector3 pos) {u.Transform.Position=pos;u.IsActive=true;}
OnZone(()=>{
 admin.ZoneService=adminService;teammate.ZoneService=targetService;enemy.ZoneService=targetService;
 Position(admin,new Vector3(8,5,4));Position(teammate,new Vector3(8,5,8));Position(enemy,new Vector3(8,5,8));
 dbPlayers[1].Role=PlayerRole.User;Cmd("on");Check(modes.Count==0,"world thread rejects nonadmin");dbPlayers[1].Role=PlayerRole.Admin;
 Cap(adminService,false);Cmd("on");Check(modes.Count==0,"old admin client rejected");Cap(adminService,true);
 Cmd("on");Check(modes.Count==1 && admin.AdminFunnyMode,"mode enabled in normal friendly match");
 Check(admin.GetBuff(BuffType.AdminHideNameplate)==1,"mode sets replicated nameplate marker");
 var snapshot=admin.GetUpdateData();Check(snapshot.Buffs?.GetValueOrDefault(BuffType.AdminHideNameplate)==1,"late observer snapshot contains marker");
 using(var stream=new MemoryStream())
 {
  UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=snapshot.Buffs,Health=2000});
  if(args.Length>0){Directory.CreateDirectory(args[0]);File.WriteAllBytes(Path.Combine(args[0],"mode-on.bin"),stream.ToArray());}
  stream.Position=0;Check(UnitUpdate.ReadRecord(new BinaryReader(stream)).Buffs?.GetValueOrDefault(BuffType.AdminHideNameplate)==1,"nameplate marker survives wire serialization");
 }
 admin.AddEffects([new ConstEffectInfo(markerEffect.Key)],admin.Team,admin.GetSelfSource());
 teammate.AddEffects([new ConstEffectInfo(markerEffect.Key)],teammate.Team,teammate.GetSelfSource());
 Check(admin.GetBuff(BuffType.AdminHideNameplate)==1,"effect recomputation preserves authorized mode marker");
 Check(!teammate.IsBuff(BuffType.AdminHideNameplate),"catalogue effects cannot impersonate admin nameplate mode");
 Check(Math.Abs(admin.UnitMaxHealth(300)-2000)<.01f && Math.Abs(admin.HealthPercentage-1)<.001,"exact 2000 maximum and current health");
 Cmd("grab",teammate);Check(teammate.AdminFunnyHeld,"teammate grabbable");Check(teammate.IsBuff(BuffType.Root)&&teammate.IsBuff(BuffType.Disarm),"held target rooted/disarmed");
 var old=teammate.Transform.Position;zone.ReceivedMoveRequest(teammate.Id,999999,new ZoneTransform {Position=new Vector3(20,20,20)});Check(teammate.Transform.Position==old,"held player movement spoof ignored");
 Cmd("drop");Check(!teammate.AdminFunnyHeld,"RMB drop releases teammate");Check(!teammate.IsBuff(BuffType.Root),"drop removes only synthetic root");
 Cap(targetService,false);Cmd("grab",enemy);Check(!enemy.AdminFunnyHeld,"legacy target safely rejected");Cap(targetService,true);
 Position(enemy,new Vector3(8,5,14));
 var mapBinary=(MapBinary)typeof(GameZone).GetProperty("MapBinary",Any)!.GetValue(zone)!;
 for(short x=0;x<32;x++) for(short y=0;y<32;y++) {var block=mapBinary[x,y,12];block.Id=1;}
 Cmd("grab",enemy,"10");Check(!enemy.AdminFunnyHeld,"solid wall blocks selection");
 Position(enemy,new Vector3(8,5,8));Cmd("grab",enemy);Check(enemy.AdminFunnyHeld,"enemy grabbable");
 Cmd("aim",enemy,"20");Call("TickAdminFunny");Check(enemy.Transform.Position.Z<12,"swept capsule cannot tunnel through wall");
 var firstClock=enemy.LastMoveUpdateTime;Call("TickAdminFunny");Check(enemy.LastMoveUpdateTime>firstClock,"held observer movement timestamps advance");
 Cmd("aim",enemy,"4");Call("TickAdminFunny");
 var modesCount=modes.Count;Cmd("grab",admin);Check(modes.Count==modesCount && !admin.AdminFunnyHeld,"no self-grab or hold replacement");
 Cmd("throw");Check(enemy.AdminFunnyHeld,"throw without charge rejected");
 Cmd("charge");var hold=modes[admin.Id]!;hold.GetType().GetField("ChargeStart")!.SetValue(hold,(DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(-3));
 Cmd("throw");Check(!enemy.AdminFunnyHeld,"charged throw releases target");
 var launch=targetSender.Packets.Last(p=>p.Length>12 && p[1]==116 && p[11]==2);
 using(var reader=new BinaryReader(new MemoryStream(launch))) {reader.BaseStream.Position=24;Check(reader.ReadSingle()==0 && reader.ReadSingle()==30 && reader.ReadSingle()==120,"target receives full launch vector");}
 Position(enemy,new Vector3(8,5,8));Cmd("grab",enemy);admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Call("TickAdminFunny");Check(!enemy.AdminFunnyHeld,"switching away from dig releases");admin.UpdateData(new UnitUpdate {CurrentGear=gear.Key});
 Cmd("grab",enemy);enemy.IsActive=false;Call("TickAdminFunny");Check(!enemy.AdminFunnyHeld,"target disconnect releases");enemy.IsActive=true;
 Cmd("grab",enemy);hold.GetType().GetField("LastUpdate")!.SetValue(hold,DateTimeOffset.UtcNow.AddSeconds(-2));Call("TickAdminFunny");
 Check(modes.Count==0 && !enemy.AdminFunnyHeld && !admin.AdminFunnyMode,"lost admin heartbeat disables and releases");
 Check(!admin.IsBuff(BuffType.AdminHideNameplate),"lease expiry removes nameplate marker");
 Check(admin.UnitMaxHealth(300)==300 && admin.HealthPercentage<=1,"normal health restored without excess health");
 Cmd("on");Cmd("grab",enemy);dbPlayers[1].Role=PlayerRole.Moderator;Call("TickAdminFunny");Check(modes.Count==0 && !enemy.AdminFunnyHeld,"role revocation disables and releases");dbPlayers[1].Role=PlayerRole.Admin;
 Cmd("on");Cmd("grab",enemy);admin.IsActive=false;Call("TickAdminFunny");Check(modes.Count==0 && !enemy.AdminFunnyHeld,"admin disconnect releases and disables");admin.IsActive=true;
 Cmd("on");Cmd("grab",enemy);enemy.IsDead=true;Call("TickAdminFunny");Check(!enemy.AdminFunnyHeld,"target death releases");enemy.IsDead=false;
 Cmd("off");Cmd("on");admin.IsDead=true;Call("TickAdminFunny");Check(modes.Count==0 && admin.IsDead,"admin death disables without revival");admin.IsDead=false;
 Cmd("on");Cmd("off");Check(modes.Count==0 && !admin.AdminFunnyMode,"explicit off resets mode");
 Check(!admin.IsBuff(BuffType.AdminHideNameplate) && admin.GetUpdateData().Buffs?.ContainsKey(BuffType.AdminHideNameplate)!=true,"off removes marker from current and future observer snapshots");
 if(args.Length>0){using var stream=new MemoryStream();UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=admin.GetUpdateData().Buffs,Health=admin.UnitMaxHealth(300)});File.WriteAllBytes(Path.Combine(args[0],"mode-off.bin"),stream.ToArray());}
 return true;
});
var oldSender=new CaptureSender();new ServiceZone(oldSender).SendAdminFunny(1,2,true,0,Vector3.Zero,Vector3.Zero,0);Check(oldSender.Packets.Count==0,"no extension packets to old clients");
foreach(var version in new[]{14,15})
{
 var negotiated=new ServiceZone(new CaptureSender());using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
 writer.Write(0x42504E47u);writer.Write(version);stream.Position=0;
 typeof(ServiceZone).GetMethod("ReceiveZoneReady",Any)!.Invoke(negotiated,[new BinaryReader(stream)]);
 Check(negotiated.SupportsAdminFunnyMode==(version>=15),"capability negotiation v"+version);
}
var rejectedSender=new CaptureSender {AssociatedPlayerId=2};var debug=new ServiceDebug(rejectedSender);
using(var stream=new MemoryStream())
{
 using var writer=new BinaryWriter(stream);writer.Write((ushort)1);writer.Write("admin_funny");writer.Write((int)6);
 foreach(var arg in new[]{"on","0","4","0","0","1"})writer.Write(arg);stream.Position=0;
 typeof(ServiceDebug).GetMethod("ReceiveExecuteArgs",Any)!.Invoke(debug,[new BinaryReader(stream)]);
 Check(System.Text.Encoding.UTF8.GetString(rejectedSender.Packets.Single()).Contains("Admin role required"),"debug transport rejects nonadmin before world dispatch");
}
var rejectedZone=new ServiceZone(rejectedSender);Cap(rejectedZone,true);
using(var stream=new MemoryStream())
{
 using var writer=new BinaryWriter(stream);writer.Write((byte)0);writer.Write(0u);writer.Write(4f);writer.Write(0f);writer.Write(0f);writer.Write(1f);stream.Position=0;
 typeof(ServiceZone).GetMethod("ReceiveAdminFunny",Any)!.Invoke(rejectedZone,[new BinaryReader(stream)]);
 Check(modes.Count==0,"zone transport rejects nonadmin before game lookup");
}
Console.WriteLine($"{checks} admin funny checks passed");Environment.Exit(0);

static T Stub<T>(Dictionary<string,object?>? returns=null) where T:class {var value=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)value).Returns=returns??[];return value;}
public class StubProxy:DispatchProxy
{
 public Dictionary<string,object?> Returns=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args) {if(method==null)return null;if(Returns.TryGetValue(method.Name,out var value))return value;var type=method.ReturnType;if(type==typeof(void))return null;if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";return type.IsValueType?Activator.CreateInstance(type):null;}
}
public sealed class CaptureSender:ISender
{
 public uint? AssociatedPlayerId {get;set;}
 public int SenderCount=>1;
 public List<byte[]> Packets=[];
 public void Send(BinaryWriter writer)=>Packets.Add(((MemoryStream)writer.BaseStream).ToArray());
 public void Send(byte[] buffer)=>Packets.Add(buffer);
 public void SendExcept(BinaryWriter writer,List<Guid> excluded)=>Send(writer);
 public void Subscribe(Guid id){} public void Unsubscribe(Guid id){} public void UnsubscribeAll(){}
}
