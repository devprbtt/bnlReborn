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
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
foreach(var wider in new[]{false,true})
{
    var players=new ConcurrentDictionary<uint,PlayerLobbyState>();
    for(uint id=1;id<=5;id++)players[id]=new PlayerLobbyState {PlayerId=id,Team=id==4?TeamType.Team2:TeamType.Team1,
        Hero=new Key(id==1?"unit_hero_engineer":"unit_hero_hunter"),Nickname="fixture"+id};
    var map=new MapData {Match=MatchType.ShieldCapture,Properties=new MapDataProps(),Size=new Vector3s(64,32,64),
        BlocksData=new byte[64*32*64*6].Zip(0).ToArray(),SpawnPoints=[
            new MapSpawnPoint{Team=TeamType.Team1,Label=SpawnPointLabel.Base,Position=new Vector3(20,4,20)},
            new MapSpawnPoint{Team=TeamType.Team2,Label=SpawnPointLabel.Base,Position=new Vector3(50,4,50)}]};
    var service=Stub<IServiceZone>();
    var zone=new GameZone(service,Stub<IServiceZone>(),Stub<IBuffer>(),Stub<ISender>(),map,
        Stub<IGameInitiator>(new(){["GetGameMode"]=new Key("game_mode_friendly"),["get_GameInstanceId"]="tony-fixture"}),players);
    object? Call(string name,params object?[] args)=>typeof(GameZone).GetMethod(name,Hidden)!.Invoke(zone,args);
    T OnZone<T>(Func<T> action){var t=new TaskCompletionSource<T>();zone.EnqueueAction(()=>{try{t.SetResult(action());}catch(Exception e){t.SetException(e);}});return t.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();}
    try
    {
        OnZone(()=>
        {
            var units=players.Keys.Order().Select(id=>(Unit)Call("CreatePlayerUnit",id,Stub<IServiceZone>())!).ToArray();
            var tony=units[0];var ally=units[1];var late=units[2];var enemy=units[3];var dead=units[4];
            var data=(ZoneData)typeof(GameZone).GetField("_zoneData",Hidden)!.GetValue(zone)!;
            data.Phase=new ZonePhase{PhaseType=ZonePhaseType.Assault};
            var origin=new Vector3(20,4,20);float radius=wider?11:5;
            void Move(Unit unit,Vector3 position){unit.Transform.Position=position;Call("AddUnitToOctree",unit,unit.Transform);}
            // Remove old indexed positions before setting each fixture transform.
            var octree=typeof(GameZone).GetField("_unitOctree",Hidden)!.GetValue(zone)!;
            void Place(Unit unit,Vector3 position){octree.GetType().GetMethod("Remove",[typeof(Unit)])!.Invoke(octree,[unit]);Move(unit,position);}
            Place(tony,origin);Place(ally,origin+new Vector3(radius-1,0,0));Place(late,origin+new Vector3(radius+2,0,0));
            Place(enemy,origin+Vector3.UnitZ);Place(dead,origin+Vector3.UnitX);dead.IsDead=true;
            var updater=(UnitUpdater)typeof(GameZone).GetField("_defaultUnitUpdater",Hidden)!.GetValue(zone)!;
            var turret=CatalogueFactory.CreateUnit(1000,new Key("unit_device_engineer_turret"),new ZoneTransform{Position=origin+new Vector3(0,0,2)},TeamType.Team1,tony,updater)!;
            var ability=new Key(wider?"ability_engineer_bfs_wider":"ability_engineer_bfs");
            tony.AbilityKey=ability;tony.AbilityCharges=1;
            zone.ReceivedAbilityCastRequest(1,1,new AbilityCastData{ShotPos=origin+new Vector3(100,0,0),
                Shots=[new ShotData{ShotId=4242,TargetPos=origin+new Vector3(100,0,0)}]},service);
            var reward=(ActionScoreLedger)typeof(GameZone).GetProperty("ActionScores",Hidden)!.GetValue(zone)!;
            bool Has(Unit u)=>u.ActiveEffects.Any(e=>e.Key==new Key(Unit.TonyBuildCastEffectId));
            Check(tony.AbilityCharges==0,"native cast route spends its charge");
            Check(Has(tony)&&Has(ally),"Tony and allies in activation range receive timed build buff");
            Check(!Has(late)&&!Has(enemy)&&!Has(dead),"late, enemy and dead players do not receive cast buff");
            Check(reward.Total(1)==5&&reward.Total(2)==5&&reward.Total(3)==0&&reward.Total(4)==0&&reward.Total(5)==0,
                "five points go to each affected player rather than all points going to Tony");
            for(int i=0;i<3;i++)foreach(var u in units.Append(turret))u.ApplyBuffEffects(.2f);
            Check(ally.GetBuff(BuffType.BuildSpeed)==1 && tony.GetBuff(BuffType.BuildSpeed)==1,"native full build-speed bonus applied");
            Check(turret.GetBuff(BuffType.PlayerDamage)==.5f,"original turret damage bonus preserved as timed snapshot");
            Check(tony.AuraEffects.Keys.All(a=>!a.ConstantEffects!.Any(k=>k==new Key("effect_hero_engineer_bfs_build"))),"cast does not leave a build aura on Tony");
            var end=ally.ActiveEffects.Single(e=>e.Key==new Key(Unit.TonyBuildCastEffectId)).ExpirationTime!.Value;
            Place(ally,origin+new Vector3(radius+8,0,0));Place(late,origin+new Vector3(1,0,0));Place(turret,origin+new Vector3(radius+8,0,2));
            for(int i=0;i<10;i++)foreach(var u in units.Append(turret))u.ApplyBuffEffects(.2f);
            Check(Has(ally)&&ally.GetBuff(BuffType.BuildSpeed)==1,"leaving the cast radius retains the buff");
            Check(!Has(late)&&late.GetBuff(BuffType.BuildSpeed)==0,"entering range after activation never grants the buff");
            zone.ReceivedHit((ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds(),new(){[4242]=new HitData{InsidePoint=origin}});
            zone.ReceivedHit((ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds(),new(){[4242]=new HitData{InsidePoint=origin}});
            Check(!Has(late)&&reward.Total(3)==0,"delayed or repeated client hit reports cannot change recipients or award points");
            Check(ally.ActiveEffects.Single(e=>e.Key==new Key(Unit.TonyBuildCastEffectId)).ExpirationTime==end,"movement does not refresh duration");
            Check(reward.Total(1)==5&&reward.Total(2)==5,"persistent buff ticks never repeat score");
            zone.ReceivedAbilityCastRequest(2,1,new AbilityCastData{ShotPos=origin},service);
            Check(reward.Total(1)==5&&reward.Total(2)==5,"rejected recast pays nothing");
            // A second accepted activation rewards its own recipient set once.
            tony.AbilityCharges=1;zone.ReceivedAbilityCastRequest(3,1,new AbilityCastData{ShotPos=origin},service);
            Check(reward.Total(1)==10&&reward.Total(2)==5&&reward.Total(3)==5,"new cast rewards current recipients, not departed recipients");
            foreach(var u in units.Append(turret))
                foreach(var effect in u.ActiveEffects.Where(e=>e.Key==new Key(Unit.TonyBuildCastEffectId)||e.Key==new Key("effect_hero_engineer_bfs_turret_cast")))effect.UpdateDuration(0UL);
            for(int i=0;i<3;i++)foreach(var u in units.Append(turret)){u.CleanUpExpired();u.ApplyBuffEffects(.2f);}
            Check(ally.GetBuff(BuffType.BuildSpeed)==0&&late.GetBuff(BuffType.BuildSpeed)==0&&tony.GetBuff(BuffType.BuildSpeed)==0,"timed wrapper expiry removes child build buffs");
            Check(turret.GetBuff(BuffType.PlayerDamage)==0,"timed turret bonus expires normally");
            Check(reward.Breakdown(2).GetValueOrDefault("Build boost received")==5,"receipt reason appears in score breakdown");
            return true;
        });
    }
    finally{zone.Stop();}
    Console.WriteLine("TONY_CAST "+(wider?"Wider 11m":"base 5m")+" PASS");
}
Console.WriteLine("TONY_CAST_BUFF PASS "+checks+" checks");

static T Stub<T>(Dictionary<string,object?>? returns=null)where T:class
{var proxy=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)proxy).Returns=returns??[];return proxy;}
public class StubProxy:DispatchProxy
{
    public Dictionary<string,object?> Returns=[];
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {if(Returns.TryGetValue(method!.Name,out var value))return value;var type=method.ReturnType;if(type==typeof(void))return null;
     if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";return type.IsValueType?Activator.CreateInstance(type):null;}
}
