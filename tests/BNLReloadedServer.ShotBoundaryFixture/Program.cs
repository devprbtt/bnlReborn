using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var sandbox = Path.Combine(AppContext.BaseDirectory, "fixture-sandbox");
Directory.CreateDirectory(Path.Combine(sandbox, "Configs"));
File.WriteAllText(Path.Combine(sandbox,"Configs/configs.json"), """{"master_host":"127.0.0.1","master_public_host":"127.0.0.1","region_name":"fixture","region_icon":"fixture"}""");
Directory.SetCurrentDirectory(sandbox);
((ServerCatalogue)Databases.Catalogue).Replicate([
    new CardBlock { Id="air", BlockId=0, Replaceable=true },
    new CardBlock { Id="block_metal", BlockId=10, Solid=true, Destructible=false },
    new CardBlock { Id="block_force_gate", BlockId=50, Solid=true, Destructible=true },
    new CardBlock { Id="block_force_gate_disco", BlockId=67, Solid=true, Destructible=true }
]);
var size = new Vector3s(12,12,12);
var map = new MapBinary(3,new byte[12*12*12*6].Zip(0).ToArray(),size,-1,
    new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true));
void Put(int x,int y,int z,ushort id) { var block=map[new Vector3s(x,y,z)]; block.Id=id; }
var from = new Vector3(2.5f,5.5f,5.5f);
Put(5,5,5,10);
Check(map.TraceShotBoundary(from,new Vector3(9,5.5f,5.5f),out var point,out var normal) && point.X==5 && normal.X==-1,"fast shot stops at metal entrance");
Check(map.TraceShotBoundary(new Vector3(5.5f,5.5f,5.5f),from,out point,out _),"shot spawned inside metal cannot escape");
Put(5,5,5,50); Put(6,5,5,67);
Check(!map.TraceShotBoundary(from,new Vector3(5.5f,5.5f,5.5f),out _,out _),"shot can reach a target inside gate");
Check(!map.TraceShotBoundary(new Vector3(5.5f,5.5f,5.5f),new Vector3(6.5f,5.5f,5.5f),out _,out _),"connected normal and disco gates form one volume");
Check(map.TraceShotBoundary(from,new Vector3(9,5.5f,5.5f),out point,out normal) && point.X==7 && normal.X==1,"outside shot stops at far gate boundary");
Check(map.TraceShotBoundary(new Vector3(5.5f,5.5f,5.5f),from,out point,out normal) && point.X==5 && normal.X==-1,"inside shot stops at near boundary");
Check(!map.TraceShotBoundary(from,from,out _,out _),"zero length segment is safe");
foreach(var dir in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ}) {
    var origin=new Vector3(5.5f);
    Check(map.TraceShotBoundary(origin,origin+dir*5,out point,out normal) && Vector3.Dot(normal,dir)>.99f,"gate exits along " + dir);
}
// Exercise the server's actual hit correction, including removal of a target behind cover.
const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var data=(ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData)); data.BlocksData=map;
typeof(GameZone).GetField("_zoneData",Flags)!.SetValue(zone,data);
var constrain=typeof(GameZone).GetMethod("ConstrainShotHit",Flags)!;
var corrected=(HitData)constrain.Invoke(zone,[from,new HitData{InsidePoint=new Vector3(9,5.5f,5.5f),TargetId=99,Crit=true}])!;
Check(corrected.TargetId==null && corrected.Crit==false && Math.Abs(corrected.InsidePoint.X-6.99f)<.001f,"server removes damage target beyond gate and hits gate instead");
Put(6,5,5,0);
// A metal wall, with an exposed player and a covered player in blast radius.
for(int y=0;y<12;y++)for(int z=0;z<12;z++)Put(5,y,z,10);
Unit Dummy(Vector3s cell) { var unit=(Unit)RuntimeHelpers.GetUninitializedObject(typeof(Unit));unit.OverlappingMapBlocks=[cell];return unit; }
var exposed=Dummy(new Vector3s(3,5,5)); var covered=Dummy(new Vector3s(6,5,5));
var damage=DamageData.ZeroDamage with { EnemyDamage=100 };
foreach(bool raycast in new[]{false,true}) {
    typeof(ConfigDatabase).GetField("_configs",Flags)!.SetValue(Databases.ConfigDatabase,
        new Configs{MasterHost="127.0.0.1",MasterPublicHost="127.0.0.1",RegionName="fixture",RegionIcon="fixture",UseRaycastExplosions=raycast});
    var result=map.SplashDamageBlocks([new Vector3(5.01f,5.5f,5.5f),new Vector3(4.96f,5.5f,5.5f)],damage,new ImpactData(),4,[exposed,covered],null,TeamType.Team1);
    Check(result.hitUnits.Contains(exposed),"front of metal takes splash, raycast="+raycast);
    Check(!result.hitUnits.Contains(covered),"behind metal takes no splash, raycast="+raycast);
    result=map.SplashDamageBlocks([new Vector3(5.5f)],damage,new ImpactData(),4,[exposed,covered],null,TeamType.Team1);
    Check(result.hitUnits.Count==0,"blast entirely inside metal cannot leak, raycast="+raycast);
}
Console.WriteLine($"PASS {checks} checks");
