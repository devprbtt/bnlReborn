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
var secondary=new CardGear {Id="fixture_secondary",Tools=[]};
var nativePullEffect=new CardEffect {Id="fixture_native_pull",Effect=new ConstEffectPull {Force=8}};
var hero=new CardUnit {Id="fixture_hero",Data=new UnitDataPlayer(),Size=new Vector3s(1,2,1),PivotType=UnitPivotType.CenterBottom,
 Health=new UnitHealth {Health=new Health {MaxHealth=300,HealthType=HealthType.Player}}};
var markerEffect=new CardEffect {Id="fixture_nameplate_marker",Effect=new ConstEffectBuff {Buffs=new() {[BuffType.AdminHideNameplate]=1,[BuffType.AdminStormEpochHigh]=123,[BuffType.AdminStormEpochLow]=456,[BuffType.AdminInvisible]=1,[BuffType.RunSpeed]=.1f}}};
var mode=new CardGameMode {Id="game_mode_friendly"};
var entitySkin=new CardSkin {Id="fixture_entity_skin"};
((ServerCatalogue)Databases.Catalogue).Replicate([gear,weapon,secondary,nativePullEffect,hero,mode,markerEffect,entitySkin,new CardGameMode {Id="game_mode_custom"},
 new CardMatch {Id="fixture_match",Data=new MatchDataShieldCapture()},new CardGlobalLogic {Id="global_logic"},
 new CardBlock {Id="fixture_wall",BlockId=1,Passable=BlockPassableType.None,Solid=true,Grounded=true},
 new CardBlock {Id="fixture_beam_wall",BlockId=2,Passable=BlockPassableType.None,Solid=true,Grounded=true,Destructible=true,Health=new Health {MaxHealth=1000}},
 new CardBlock {Id="fixture_air",BlockId=0,Passable=BlockPassableType.Any,Transparent=true,LightTransparent=true,SkylightTransparent=true}]);
((UnitDataPlayer)hero.Data!).Gears=[gear.Key,weapon.Key,secondary.Key];
var players=new ConcurrentDictionary<uint,PlayerLobbyState>();
foreach(var id in new[]{1u,2u,3u}) players[id]=new PlayerLobbyState {PlayerId=id,Team=id==3?TeamType.Team2:TeamType.Team1,Hero=hero.Key,Nickname="fixture"};
var dbPlayers=(ConcurrentDictionary<uint,PlayerData>)typeof(PlayerDatabase).GetField("_players",Any)!.GetValue(Databases.PlayerDatabase)!;
foreach(var id in players.Keys) dbPlayers[id]=new PlayerData {PlayerId=id,Role=id==1?PlayerRole.Admin:PlayerRole.User};
var map=new MapData {Match=MatchType.ShieldCapture,Properties=new MapDataProps(),Size=new Vector3s(32,32,32),BlocksData=new byte[32*32*32*6].Zip(0).ToArray(),
 SpawnPoints=[new MapSpawnPoint {Team=TeamType.Team1,Label=SpawnPointLabel.Base,Position=new Vector3(8,5,4)},new MapSpawnPoint {Team=TeamType.Team2,Label=SpawnPointLabel.Base,Position=new Vector3(8,5,8)}]};
var service=Stub<IServiceZone>();
var zone=new GameZone(service,Stub<IServiceZone>(),Stub<IBuffer>(),Stub<ISender>(),map,
 Stub<IGameInitiator>(new(){["GetGameMode"]=mode.Key,["get_GameInstanceId"]="funny-fixture"}),players);
var beamEvents=new List<object?[]>();
zone.AdminBeamSender=(admin,phase,time,origin,end,charge)=>beamEvents.Add([admin,phase,time,origin,end,charge]);
var gravityEvents=new List<object?[]>();zone.AdminGravitySender=(admin,phase,time,origin,radius,strength)=>gravityEvents.Add([admin,phase,time,origin,radius,strength]);
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
long Epoch(Unit u)=>(long)u.GetBuff(BuffType.AdminStormEpochHigh)*1048576L+(long)u.GetBuff(BuffType.AdminStormEpochLow);
void Position(Unit u,Vector3 pos) {u.Transform.Position=pos;u.IsActive=true;}
OnZone(()=>{
 admin.ZoneService=adminService;teammate.ZoneService=targetService;enemy.ZoneService=targetService;
 Position(admin,new Vector3(8,5,4));Position(teammate,new Vector3(8,5,8));Position(enemy,new Vector3(8,5,8));
 dbPlayers[1].Role=PlayerRole.User;Cmd("on");Check(modes.Count==0,"world thread rejects nonadmin");dbPlayers[1].Role=PlayerRole.Admin;
 Cap(adminService,false);Cmd("on");Check(modes.Count==0,"old admin client rejected");Cap(adminService,true);
 Cmd("on");Check(modes.Count==1 && admin.AdminFunnyMode,"mode enabled in normal friendly match");
 Check(admin.GetBuff(BuffType.AdminHideNameplate)==1,"mode sets replicated nameplate marker");
 var firstEpoch=Epoch(admin);
 Check(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-firstEpoch)<2000,"storm epoch retains exact server milliseconds");
 Cmd("on");Check(Epoch(admin)==firstEpoch,"duplicate on request does not restart storm");
 dbPlayers[2].Role=PlayerRole.Admin;
 zone.ReceivedDebugCommand(2,"admin_funny",["on","0","4","0","0","1"]);
 Check(Epoch(admin)>firstEpoch && Epoch(admin)==Epoch(teammate),"new transformation synchronizes epoch across active admins");
 var sharedEpoch=Epoch(admin);
 zone.ReceivedDebugCommand(2,"admin_funny",["off","0","4","0","0","1"]);
 Check(Epoch(admin)==sharedEpoch && teammate.GetBuff(BuffType.AdminStormEpochHigh)==0 && teammate.GetBuff(BuffType.AdminStormEpochLow)==0,"remaining admin retains current global storm after newest exits");
 dbPlayers[2].Role=PlayerRole.User;
 var snapshot=admin.GetUpdateData();Check(snapshot.Buffs?.GetValueOrDefault(BuffType.AdminHideNameplate)==1,"late observer snapshot contains marker");
 using(var stream=new MemoryStream())
 {
  UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=snapshot.Buffs,Health=2000});
  if(args.Length>0){Directory.CreateDirectory(args[0]);File.WriteAllBytes(Path.Combine(args[0],"mode-on.bin"),stream.ToArray());File.WriteAllText(Path.Combine(args[0],"storm-epoch.txt"),Epoch(admin).ToString());}
  stream.Position=0;Check(UnitUpdate.ReadRecord(new BinaryReader(stream)).Buffs?.GetValueOrDefault(BuffType.AdminHideNameplate)==1,"nameplate marker survives wire serialization");
 }
 admin.AddEffects([new ConstEffectInfo(markerEffect.Key)],admin.Team,admin.GetSelfSource());
 teammate.AddEffects([new ConstEffectInfo(markerEffect.Key)],teammate.Team,teammate.GetSelfSource());
 Check(admin.GetBuff(BuffType.AdminHideNameplate)==1,"effect recomputation preserves authorized mode marker");
 Check(!teammate.IsBuff(BuffType.AdminHideNameplate),"catalogue effects cannot impersonate admin nameplate mode");
 Check(Epoch(admin)==sharedEpoch,"effect recomputation preserves authoritative weather epoch");
 Check(!teammate.IsBuff(BuffType.AdminStormEpochHigh) && !teammate.IsBuff(BuffType.AdminStormEpochLow),"catalogue effects cannot inject storm epoch");
 Check(!teammate.IsBuff(BuffType.AdminInvisible) && !admin.IsBuff(BuffType.AdminInvisible),"catalogue effects cannot inject invisibility");
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
 Check(!admin.IsBuff(BuffType.AdminStormEpochHigh) && !admin.IsBuff(BuffType.AdminStormEpochLow),"mode cleanup removes both storm epoch fields");
 Check(!admin.IsBuff(BuffType.AdminHideNameplate) && admin.GetUpdateData().Buffs?.ContainsKey(BuffType.AdminHideNameplate)!=true,"off removes marker from current and future observer snapshots");
 if(args.Length>0){using var stream=new MemoryStream();UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=admin.GetUpdateData().Buffs,Health=admin.UnitMaxHealth(300)});File.WriteAllBytes(Path.Combine(args[0],"mode-off.bin"),stream.ToArray());}
 // Beam tests use the same authorized world-thread path as the wire transport.
 Check(AdminBeamRules.Charge(3)==1 && AdminBeamRules.Charge(-1)==0,"beam server charge clamps to 2.5 seconds");
 Check(AdminBeamRules.Radius(1)==7 && AdminBeamRules.Damage(1).BlockDamage==25000,"full beam has seven-metre destruction radius and 25000 block damage");
 Check(!AdminBeamRules.Damage(1).IgnoreInvincibility && AdminBeamRules.Damage(1).SelfDamage==0 && AdminBeamRules.Damage(1).FriendDamage==0,"beam preserves map protection and normal friendly-fire policy");
 Check(AdminBeamRules.SegmentDistance(new Vector3(2,0,4),Vector3.Zero,new Vector3(0,0,8))==2,"beam geometry measures finite segment distance");
 Position(admin,new Vector3(8,20,4));Position(teammate,new Vector3(24,20,24));Position(enemy,new Vector3(24,20,24));
 admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Cmd("on");
 var beamHold=modes[admin.Id]!;var beamField=beamHold.GetType().GetField("BeamStart")!;
 Cmd("beam_charge");Check(beamField.GetValue(beamHold)==null,"legacy admin cannot start beam");
 typeof(ServiceZone).GetProperty("SupportsAdminBeam")!.SetValue(adminService,true);
 var beamCalls=beamEvents;
 int beforeBeam=beamCalls.Count;Cmd("beam_fire");Check(beamCalls.Count==beforeBeam,"release without a server charge does not fire");
 Cmd("beam_charge");var beamStart=beamField.GetValue(beamHold);Check(beamStart!=null,"weapon slot starts authorized beam charge");
 Cmd("beam_charge");Check(Equals(beamField.GetValue(beamHold),beamStart),"duplicate beam charge does not restart timer");
 Cmd("beam_cancel");Check(beamField.GetValue(beamHold)==null && beamCalls.Last()[1] is (byte)2,"beam cancel clears charge and broadcasts cleanup");
 admin.UpdateData(new UnitUpdate {CurrentGear=gear.Key});Cmd("beam_charge");Check(beamField.GetValue(beamHold)==null,"dig tool remains reserved for gravity grab");
 admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Cmd("beam_charge");
 for(short x=6;x<=10;x++) for(short y=18;y<=22;y++) {var block=mapBinary[x,y,12];block.Id=2;}
 {var block=mapBinary[8,20,13];block.Id=1;block=mapBinary[24,20,12];block.Id=2;}
 beamField.SetValue(beamHold,DateTimeOffset.UtcNow.AddSeconds(-3));Cmd("beam_fire");
 Check(beamField.GetValue(beamHold)==null && beamCalls.Last()[1] is (byte)1,"beam fire consumes charge and broadcasts shot");
 Check(mapBinary[8,20,12].IsAir && mapBinary[6,18,12].IsAir,"charged beam actually destroys a wide patch of native terrain");
 Check(mapBinary[8,20,13].Id==1 && mapBinary[24,20,12].Id==2,"beam respects indestructible and out-of-radius terrain");
 var shotCall=beamCalls.Last();Check((float)shotCall[5]! ==1 && ((Vector3)shotCall[4]!).Z<12.3f,"server derives capped charge and first solid impact endpoint");
 Cmd("beam_charge");Check(beamField.GetValue(beamHold)==null,"server cooldown rejects immediate repeated beam");
 beamHold.GetType().GetField("NextBeam")!.SetValue(beamHold,DateTimeOffset.MinValue);
 Cmd("beam_charge");admin.UpdateData(new UnitUpdate {CurrentGear=gear.Key});Call("TickAdminFunny");Check(beamField.GetValue(beamHold)==null,"tool switch cancels beam charge");
 admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Cmd("beam_charge");Cmd("off");Check(beamField.GetValue(beamHold)==null && modes.Count==0,"mode exit cancels pending beam");
 Cmd("on");beamHold=modes[admin.Id]!;
 var balls=(System.Collections.IList)typeof(GameZone).GetField("_entityFireballs",Any)!.GetValue(zone)!;
 Position(teammate,new Vector3(8,20,7));Position(enemy,new Vector3(10,20,10));
 Cmd("fireball");Check(balls.Count==1,"secondary creates one server-owned projectile");
 var ball=balls[0]!;var ballType=ball.GetType();Check(ballType.GetField("Target")!.GetValue(ball)==enemy,"homing selects visible enemy and ignores nearer teammate");
 Cmd("fireball");Check(balls.Count==1,"fireball server cooldown prevents spam");
 ballType.GetField("Last")!.SetValue(ball,DateTimeOffset.UtcNow.AddSeconds(-.1));Call("TickAdminFunny");
 Check(((Vector3)ballType.GetField("Direction")!.GetValue(ball)!).X>0 && ((Vector3)ballType.GetField("Position")!.GetValue(ball)!).Z>4,"server projectile advances and turns toward enemy");
 enemy.IsDead=true;ballType.GetField("Last")!.SetValue(ball,DateTimeOffset.UtcNow.AddSeconds(-.1));Call("TickAdminFunny");Check(balls.Count==1,"lost homing target keeps finite forward flight");enemy.IsDead=false;
 ballType.GetField("Born")!.SetValue(ball,DateTimeOffset.UtcNow.AddSeconds(-6));Call("TickAdminFunny");Check(balls.Count==0,"fireball expires after bounded lifetime");
 beamHold.GetType().GetField("NextFireball")!.SetValue(beamHold,DateTimeOffset.MinValue);
 for(short x=6;x<=10;x++)for(short y=19;y<=22;y++){var block=mapBinary[x,y,6];block.Id=1;}
 Cmd("fireball");ball=balls[0]!;ballType=ball.GetType();Check(ballType.GetField("Target")!.GetValue(ball)==null,"solid terrain blocks initial homing lock");
 ballType.GetField("Last")!.SetValue(ball,DateTimeOffset.UtcNow.AddSeconds(-.1));Call("TickAdminFunny");Check(balls.Count==0 && mapBinary[8,20,6].Id==1,"swept fireball hits solid wall without removing protected terrain");
 beamHold.GetType().GetField("NextFireball")!.SetValue(beamHold,DateTimeOffset.MinValue);Cmd("fireball");Cmd("off");Call("TickAdminFunny");Check(balls.Count==0,"mode exit removes in-flight fireballs");
 Position(enemy,new Vector3(8,20,5));Position(teammate,new Vector3(8,20,5));enemy.SpawnProtectionTime=null;teammate.SpawnProtectionTime=null;
 enemy.UpdateData(new UnitUpdate {Health=300});teammate.UpdateData(new UnitUpdate {Health=300});
 var damageFrom=admin.GetMidpoint();var damageTo=enemy.GetMidpoint();
 Call("ApplyEntityDamage",admin,damageFrom,damageTo,AdminBeamRules.Damage(0) with {EnemyDamage=100},0f,2f);
 Check(Math.Abs(enemy.HealthPercentage-2f/3)<.001f && teammate.HealthPercentage==1,"entity damage reaches enemy through native health rules without hurting teammate");
 enemy.UpdateData(new UnitUpdate {Health=300});enemy.SpawnProtectionTime=DateTimeOffset.Now.AddSeconds(30);
 Call("ApplyEntityDamage",admin,damageFrom,damageTo,AdminBeamRules.Damage(1),0f,2f);Check(enemy.HealthPercentage==1,"entity beam respects spawn protection");enemy.SpawnProtectionTime=null;
 Position(enemy,new Vector3(8,20,9));Call("ApplyEntityDamage",admin,damageFrom,new Vector3(8,20.95f,5.8f),AdminBeamRules.Damage(1),7f,2f);Check(enemy.HealthPercentage==1,"solid terrain shields units from entity damage");
 // Secondary weapon: native pull aggregation and knockback, across both teams.
 Position(admin,new Vector3(8,5,4));Position(teammate,new Vector3(12,5,4));Position(enemy,new Vector3(8,5,9));
 admin.UpdateData(new UnitUpdate {CurrentGear=secondary.Key});Cmd("on");var gravityHold=modes[admin.Id]!;
 var wellField=gravityHold.GetType().GetField("WellActive")!;var repulseField=gravityHold.GetType().GetField("RepulseStart")!;
 Cmd("well_on");Check(!(bool)wellField.GetValue(gravityHold)!,"capability 16 cannot start new gravity actions");
 typeof(ServiceZone).GetProperty("SupportsAdminGravity")!.SetValue(adminService,true);
 admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Cmd("well_on");Check(!(bool)wellField.GetValue(gravityHold)!,"primary slot cannot activate secondary gravity well");
 admin.UpdateData(new UnitUpdate {CurrentGear=secondary.Key});Cmd("beam_charge");Check(gravityHold.GetType().GetField("BeamStart")!.GetValue(gravityHold)==null,"secondary slot cannot start primary beam");
 Cmd("well_on");Check((bool)wellField.GetValue(gravityHold)!,"secondary LMB starts gravity well");
 uint? Puller(Unit target)=>(uint?)typeof(Unit).GetField("_activePuller",Any)!.GetValue(target);
 Check(Puller(teammate)==admin.Id && Puller(enemy)==admin.Id,"large well pulls both teammate and enemy toward caster");
 Check((float)typeof(Unit).GetField("_activePullForce",Any)!.GetValue(enemy)! ==22,"well drives native pull at 22 metres per second");
 Position(teammate,new Vector3(31,5,31));Call("TickAdminFunny");Check(Puller(teammate)==null,"leaving 28-metre well removes only that target");
 Position(enemy,new Vector3(8,5,14));Call("TickAdminFunny");Check(Puller(enemy)==null,"solid wall blocks gravity attraction");
 Position(enemy,new Vector3(8,5,9));Call("TickAdminFunny");Check(Puller(enemy)==admin.Id,"entering visible well reacquires pull");
 var pullInfo=new ConstEffectInfo(nativePullEffect.Key);enemy.AddEffects([pullInfo],admin.Team,admin.GetSelfSource());
 Cmd("well_off");Check(Puller(enemy)==admin.Id && (float)typeof(Unit).GetField("_activePullForce",Any)!.GetValue(enemy)! ==8,"ending admin well preserves ordinary native pull effect");
 enemy.RemoveEffects([pullInfo],admin.Team,admin.GetSelfSource());Check(Puller(enemy)==null,"native pull clears when its own source ends");
 Position(teammate,new Vector3(12,5,4));Position(enemy,new Vector3(10,5,8));
 dbPlayers[2].Role=PlayerRole.Admin;teammate.UpdateData(new UnitUpdate {CurrentGear=secondary.Key});typeof(ServiceZone).GetProperty("SupportsAdminGravity")!.SetValue(targetService,true);
 Cmd("well_on");zone.ReceivedDebugCommand(2,"admin_funny",["on","0","4","0","0","1"]);zone.ReceivedDebugCommand(2,"admin_funny",["well_on","0","4","0","0","1"]);
 Cmd("well_off");Check(Puller(enemy)==teammate.Id,"overlapping wells retain remaining caster when first releases");
 zone.ReceivedDebugCommand(2,"admin_funny",["off","0","4","0","0","1"]);dbPlayers[2].Role=PlayerRole.User;Check(Puller(enemy)==null,"last well owner cleanup removes pull");
 Cmd("well_on");Cmd("repulse_charge");Check(!(bool)wellField.GetValue(gravityHold)! && Puller(enemy)==null && repulseField.GetValue(gravityHold)!=null,"RMB charge releases active attraction before charging repulsion");
 var repulseStart=repulseField.GetValue(gravityHold);Cmd("repulse_charge");Check(Equals(repulseStart,repulseField.GetValue(gravityHold)),"duplicate repulsion charge preserves server start time");
 repulseField.SetValue(gravityHold,DateTimeOffset.UtcNow.AddSeconds(-3));var nativeMoves=((StubProxy)(object)service).Maneuvers;int beforePush=nativeMoves.Count;
 Cmd("repulse_fire");var pushes=nativeMoves.Skip(beforePush).Where(entry=>entry.Item2 is ManeuverKnockback).ToArray();
 Check(pushes.Any(entry=>entry.Item1==teammate.Id) && pushes.Any(entry=>entry.Item1==enemy.Id) && pushes.All(entry=>entry.Item1!=admin.Id),"charged repulsion sends native knockback to both teams excluding caster");
 Check((float)gravityEvents.Last()[4]! ==32 && (float)gravityEvents.Last()[5]! ==1 && ((ManeuverKnockback)pushes[0].Item2).Force>30,"server caps repulsion charge and expands radius to 32 metres");
 Cmd("repulse_charge");Check(repulseField.GetValue(gravityHold)==null,"repulsion cooldown rejects repeated charge");
 int eventsAfter=gravityEvents.Count;Cmd("repulse_fire");Check(gravityEvents.Count==eventsAfter,"release without repulsion charge cannot explode");
 gravityHold.GetType().GetField("NextRepulse")!.SetValue(gravityHold,DateTimeOffset.MinValue);Cmd("repulse_charge");admin.UpdateData(new UnitUpdate {CurrentGear=weapon.Key});Call("TickAdminFunny");Check(repulseField.GetValue(gravityHold)==null,"weapon switch cancels pending repulsion without exploding");
 admin.UpdateData(new UnitUpdate {CurrentGear=secondary.Key});Cmd("well_on");Cmd("off");Check(Puller(enemy)==null && !(bool)wellField.GetValue(gravityHold)!,"mode exit releases all well targets");
 Cmd("on");gravityHold=modes[admin.Id]!;Cmd("well_on");gravityHold.GetType().GetField("LastUpdate")!.SetValue(gravityHold,DateTimeOffset.UtcNow.AddSeconds(-2));Call("TickAdminFunny");Check(Puller(enemy)==null && modes.Count==0,"heartbeat loss cleans up gravity well ownership");
 Cmd("on");Cmd("well_on");dbPlayers[1].Role=PlayerRole.Moderator;Call("TickAdminFunny");Check(Puller(enemy)==null && modes.Count==0,"role revocation cleans up well targets");dbPlayers[1].Role=PlayerRole.Admin;
 Cmd("on");gravityHold=modes[admin.Id]!;Cmd("repulse_charge");int beforeCancel=nativeMoves.Count;Cmd("gravity_cancel");Check(gravityHold.GetType().GetField("RepulseStart")!.GetValue(gravityHold)==null && nativeMoves.Count==beforeCancel,"cancelled repulsion emits no knockback");Cmd("off");
 Check(AdminGravityRules.Charge(20)==1 && AdminGravityRules.Charge(-1)==0 && AdminGravityRules.Force(1,32)==37.5f,"gravity tuning clamps charge and applies bounded edge falloff");
 if(args.Length>0){var gravityWireSender=new CaptureSender();var gravityWire=new ServiceZone(gravityWireSender);typeof(ServiceZone).GetProperty("SupportsAdminGravity")!.SetValue(gravityWire,true);gravityWire.SendAdminGravity(admin.Id,2,987654321,new Vector3(8,5,4),32,1);File.WriteAllBytes(Path.Combine(args[0],"gravity-blast.bin"),gravityWireSender.Packets.Single());}
 Cmd("on");Cmd("invis_toggle");Check(!admin.AdminInvisible,"older capability cannot toggle invisibility");
 typeof(ServiceZone).GetProperty("SupportsAdminInvisibility")!.SetValue(adminService,true);
 Cmd("invis_toggle");Check(admin.AdminInvisible && admin.GetBuff(BuffType.AdminInvisible)==1,"F enables authoritative invisibility marker");
 Check(admin.UnitMaxHealth(300)==2000 && admin.AdminFunnyMode,"invisibility preserves transformed health and mode");
 admin.SetAdminFunnyStormEpoch(Epoch(admin));Check(admin.GetBuff(BuffType.AdminInvisible)==1,"ordinary buff recomputation preserves invisibility");
 using(var stream=new MemoryStream()){var invisSnapshot=admin.GetUpdateData();UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=invisSnapshot.Buffs});stream.Position=0;Check(UnitUpdate.ReadRecord(new BinaryReader(stream)).Buffs?.GetValueOrDefault(BuffType.AdminInvisible)==1,"late-observer full snapshot serializes invisibility");if(args.Length>0)File.WriteAllBytes(Path.Combine(args[0],"invisible-on.bin"),stream.ToArray());}
 Cmd("on");Check(admin.AdminInvisible,"duplicate enable does not clear current invisibility");
 for(int i=0;i<20;i++)Cmd("invis_toggle");Check(admin.AdminInvisible,"rapid toggling has no cooldown or charge consumption");
 Cmd("invis_toggle");Check(!admin.AdminInvisible && !admin.IsBuff(BuffType.AdminInvisible),"F again immediately restores visibility");
 if(args.Length>0){using var stream=new MemoryStream();UnitUpdate.WriteRecord(new BinaryWriter(stream),new UnitUpdate {Buffs=admin.GetUpdateData().Buffs});File.WriteAllBytes(Path.Combine(args[0],"invisible-off.bin"),stream.ToArray());}
 Cmd("invis_toggle");Cmd("off");Check(!admin.AdminInvisible && !admin.IsBuff(BuffType.AdminInvisible),"mode exit clears invisibility state and buff");
 Cmd("on");Cmd("invis_toggle");var invisibleHold=modes[admin.Id]!;invisibleHold.GetType().GetField("LastUpdate")!.SetValue(invisibleHold,DateTimeOffset.UtcNow.AddSeconds(-2));Call("TickAdminFunny");Check(!admin.AdminInvisible,"lease expiry restores visibility");
 Cmd("on");Cmd("invis_toggle");dbPlayers[1].Role=PlayerRole.Moderator;Call("TickAdminFunny");Check(!admin.AdminInvisible,"role revocation restores visibility");dbPlayers[1].Role=PlayerRole.Admin;
 Cmd("on");Cmd("invis_toggle");admin.IsDead=true;Call("TickAdminFunny");Check(!admin.AdminInvisible && admin.IsDead,"death restores visibility without revival");admin.IsDead=false;
 admin.SetAdminInvisible(true);Check(!admin.AdminInvisible,"invisibility cannot be enabled outside transformed mode");
 if(args.Length>0) {var wireSender=new CaptureSender();var wireService=new ServiceZone(wireSender);typeof(ServiceZone).GetProperty("SupportsAdminBeam")!.SetValue(wireService,true);wireService.SendAdminBeam(admin.Id,1,123456789,Vector3.Zero,new Vector3(0,0,24),1);File.WriteAllBytes(Path.Combine(args[0],"beam-shot.bin"),wireSender.Packets.Single());}
 return true;
});
var oldSender=new CaptureSender();new ServiceZone(oldSender).SendAdminFunny(1,2,true,0,Vector3.Zero,Vector3.Zero,0);Check(oldSender.Packets.Count==0,"no extension packets to old clients");
foreach(var version in new[]{14,15,16,17,18,19})
{
 var negotiated=new ServiceZone(new CaptureSender());using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
 writer.Write(0x42504E47u);writer.Write(version);stream.Position=0;
 typeof(ServiceZone).GetMethod("ReceiveZoneReady",Any)!.Invoke(negotiated,[new BinaryReader(stream)]);
 Check(negotiated.SupportsAdminFunnyMode==(version>=15),"capability negotiation v"+version);
 Check(negotiated.SupportsAdminBeam==(version>=16),"beam capability negotiation v"+version);
 Check(negotiated.SupportsAdminGravity==(version>=17),"gravity capability negotiation v"+version);
 Check(negotiated.SupportsAdminInvisibility==(version>=18),"invisibility capability negotiation v"+version);
 Check(negotiated.SupportsAdminSpectator==(version>=19),"spectator entity capability negotiation v"+version);
 var gatedSender=new CaptureSender();var gated=new ServiceZone(gatedSender);typeof(ServiceZone).GetProperty("SupportsAdminBeam")!.SetValue(gated,version>=16);gated.SendAdminBeam(1,1,123,Vector3.Zero,Vector3.One,1);Check(gatedSender.Packets.Count==(version>=16?1:0),"beam observer packet gates v"+version);
 gated.SendAdminFireball(1,1,0,Vector3.Zero,Vector3.UnitZ);Check(gatedSender.Packets.Count==(version>=16?2:0),"fireball observer packet gates v"+version);
 typeof(ServiceZone).GetProperty("SupportsAdminGravity")!.SetValue(gated,version>=17);gated.SendAdminGravity(1,0,123,Vector3.Zero,28,1);Check(gatedSender.Packets.Count==(version>=17?3:version>=16?2:0),"gravity observer packet gates v"+version);
}
var rejectedSender=new CaptureSender {AssociatedPlayerId=2};var debug=new ServiceDebug(rejectedSender);
// Exercise the production recipient selection, rather than a shared buffered
// service whose capability flags are intentionally unset.
var observerInstance=(GameInstance)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameInstance));
var usersField=typeof(GameInstance).GetField("_connectedUsers",Any)!;
var observerUsers=Activator.CreateInstance(usersField.FieldType)!;usersField.SetValue(observerInstance,observerUsers);
var observerServices=new ConcurrentDictionary<Guid,Dictionary<ServiceId,IService>>();
typeof(GameInstance).GetField("_services",Any)!.SetValue(observerInstance,observerServices);
var observerSenders=new List<CaptureSender>();var observerZones=new List<ServiceZone>();
var connectionType=typeof(GameInstance).GetNestedType("MatchConnectionInfo",BindingFlags.NonPublic)!;
for(int i=0;i<4;i++)
{
 var guid=Guid.NewGuid();var capture=new CaptureSender();var observerService=new ServiceZone(capture);observerSenders.Add(capture);observerZones.Add(observerService);
 typeof(ServiceZone).GetProperty("SupportsAdminBeam")!.SetValue(observerService,i!=2);
 var connection=Activator.CreateInstance(connectionType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[guid,Guid.NewGuid(),TeamType.Neutral,null],null)!;
 var stage=connectionType.GetProperty("LoadStage")!;if(i!=3)stage.SetValue(connection,Enum.Parse(stage.PropertyType,"Finished"));
 observerUsers.GetType().GetMethod("TryAdd")!.Invoke(observerUsers,[(uint)(50+i),connection]);
 observerServices[guid]=new(){[ServiceId.ServiceZone]=observerService};
}
var recipients=((IEnumerable<IServiceZone>)typeof(GameInstance).GetMethod("EntityAttackObservers",Any)!.Invoke(observerInstance,null)!).ToArray();
Check(recipients.Length==2 && recipients.Contains(observerZones[0]) && recipients.Contains(observerZones[1]),"production relay includes loaded upgraded observers without requiring player units");
foreach(var observer in recipients){observer.SendAdminBeam(1,1,123,Vector3.Zero,Vector3.UnitZ,1);observer.SendAdminFireball(1,1,0,Vector3.Zero,Vector3.UnitZ);}
Check(observerSenders[0].Packets.Count==2 && observerSenders[1].Packets.Count==2,"beam and fireball packets reach both selected observer connections");
typeof(ServiceZone).GetProperty("SupportsAdminGravity")!.SetValue(observerZones[0],true);
foreach(var observer in recipients)observer.SendAdminGravity(1,0,123,Vector3.Zero,28,1);
Check(observerSenders[0].Packets.Count==3 && observerSenders[1].Packets.Count==2,"production connection relay sends gravity only to upgraded capability 17 observer");
Check(observerSenders[2].Packets.Count==0 && observerSenders[3].Packets.Count==0,"production relay excludes legacy and loading connections");
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
((UnitDataPlayer)hero.Data!).Skins=[entitySkin.Key];
dbPlayers[4]=new PlayerData {PlayerId=4,Role=PlayerRole.Admin,LastPlayedHero=hero.Key};
var spectatorInitiator=Stub<IGameInitiator>(new(){["GetGameMode"]=mode.Key,["get_GameInstanceId"]="spectator-entity-fixture",["IsPlayerSpectator"]=true});
var spectatorZone=new GameZone(Stub<IServiceZone>(),Stub<IServiceZone>(),Stub<IBuffer>(),Stub<ISender>(),map,spectatorInitiator,new());
T SpectatorThread<T>(Func<T> f){var done=new TaskCompletionSource<T>();spectatorZone.EnqueueAction(()=>{try{done.SetResult(f());}catch(Exception e){done.SetException(e);}});return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}
var spectatorPackets=new CaptureSender();var spectatorService=new ServiceZone(spectatorPackets);
foreach(string capability in new[]{"SupportsAdminFunnyMode","SupportsAdminBeam","SupportsAdminGravity","SupportsAdminInvisibility","SupportsAdminSpectator"})typeof(ServiceZone).GetProperty(capability)!.SetValue(spectatorService,true);
var spectatorUnits=(Dictionary<uint,Unit>)typeof(GameZone).GetField("_playerUnits",Any)!.GetValue(spectatorZone)!;
var spectatorData=(ZoneData)typeof(GameZone).GetField("_zoneData",Any)!.GetValue(spectatorZone)!;
string[] OnArgs=["on","0","6","0","0","1"],OffArgs=["off","0","6","0","0","1"];
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);return true;});
Check(spectatorUnits.Count==1 && spectatorUnits.Values.Single().AdminSpectatorEntity,"admin spectator spawns transient entity body");
var transient=spectatorUnits.Values.Single();
Check(transient.Team==TeamType.Neutral && transient.AdminFunnyMode,"spectator entity is neutral and transformed");
Check(transient.UnitMaxHealth(300)==2000 && transient.HealthPercentage==1,"spectator entity gets 2000 current and maximum health");
Check(spectatorData.PlayerStats.Count==0 && spectatorData.PlayerInfo.Count==0,"spectator entity never enters scoreboard or roster");
Check(spectatorInitiator.IsPlayerSpectator(4),"entity retains spectator membership");
Check(spectatorPackets.Packets.First(p=>p[1]==120)[2]==1,"owner enters control before spawn");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);return true;});Check(spectatorUnits.Count==1,"repeat activation does not duplicate entity");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OffArgs,spectatorService);return true;});
Check(spectatorUnits.Count==0 && spectatorPackets.Packets.Last(p=>p[1]==120)[2]==0,"mode off removes body and restores spectator");
Check(spectatorData.RespawnInfo.Count==0 && spectatorData.PlayerStats.Count==0,"exit leaves no respawn or score row");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);spectatorUnits.Values.Single().Killed(new ImpactData());return true;});
Check(spectatorUnits.Count==0 && spectatorPackets.Packets.Last(p=>p[1]==120)[2]==0,"entity death immediately returns to spectating without respawn");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);spectatorZone.PlayerLeft(4,KickReason.MatchQuit);return true;});
Check(spectatorUnits.Count==0,"leaving match removes spectator entity");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);var funny=(System.Collections.IDictionary)typeof(GameZone).GetField("_funnyModes",Any)!.GetValue(spectatorZone)!;var active=funny[spectatorUnits.Values.Single().Id]!;active.GetType().GetField("LastUpdate")!.SetValue(active,DateTimeOffset.UtcNow.AddSeconds(-2));typeof(GameZone).GetMethod("TickAdminFunny",Any)!.Invoke(spectatorZone,null);return true;});
Check(spectatorUnits.Count==0,"spectator entity lease expiry returns to observation");
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);dbPlayers[4].Role=PlayerRole.User;typeof(GameZone).GetMethod("TickAdminFunny",Any)!.Invoke(spectatorZone,null);return true;});
Check(spectatorUnits.Count==0,"role revocation removes spectator entity");dbPlayers[4].Role=PlayerRole.Admin;
((StubProxy)(object)spectatorInitiator).Returns["IsPlayerSpectator"]=false;
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);return true;});Check(spectatorUnits.Count==0,"participant cannot invoke spectator spawn path");
((StubProxy)(object)spectatorInitiator).Returns["IsPlayerSpectator"]=true;
typeof(ServiceZone).GetProperty("SupportsAdminSpectator")!.SetValue(spectatorService,false);
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);return true;});Check(spectatorUnits.Count==0,"old client cannot spawn spectator entity");
typeof(ServiceZone).GetProperty("SupportsAdminSpectator")!.SetValue(spectatorService,true);dbPlayers[4].Role=PlayerRole.Moderator;
SpectatorThread(()=>{spectatorZone.ReceivedAdminSpectator(4,OnArgs,spectatorService);return true;});Check(spectatorUnits.Count==0,"moderator spectator cannot spawn entity");
Console.WriteLine($"{checks} admin funny checks passed");Environment.Exit(0);

static T Stub<T>(Dictionary<string,object?>? returns=null) where T:class {var value=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)value).Returns=returns??[];return value;}
public class StubProxy:DispatchProxy
{
 public Dictionary<string,object?> Returns=[];
 public List<(uint,Maneuver)> Maneuvers=[];
 public List<object?[]> Calls=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args) {if(method==null)return null;if(method.Name=="SendUnitManeuver")Maneuvers.Add(((uint)args![0]!, (Maneuver)args[1]!));if(method.Name=="SendAdminBeam")Calls.Add(args??[]);if(Returns.TryGetValue(method.Name,out var value))return value;var type=method.ReturnType;if(type==typeof(void))return null;if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";return type.IsValueType?Activator.CreateInstance(type):null;}
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
