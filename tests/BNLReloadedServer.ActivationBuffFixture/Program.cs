using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
void Near(float actual,float expected,string text)=>Check(MathF.Abs(actual-expected)<.0001f,text);
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
foreach(var variant in new[]{("kira","",10f,10f), ("kira","_idoru",16f,10f),
    ("kira","_thrills_for_days",10f,6f), ("trondson","",10f,12f), ("trondson","_singin_in_the_mine",16f,12f)})
{
    var (hero,suffix,radius,duration)=variant; bool kira=hero=="kira",thrills=suffix=="_thrills_for_days";
    string family=kira?"kira_jpop_star":"trondson_heigh_ho";
    var players=new ConcurrentDictionary<uint,PlayerLobbyState>();
    for(uint id=1;id<=5;id++)players[id]=new PlayerLobbyState{PlayerId=id,Team=id==4?TeamType.Team2:TeamType.Team1,
        Hero=new Key("unit_hero_"+(id==1?hero:"hunter")),Nickname="fixture"+id};
    var map=new MapData{Match=MatchType.ShieldCapture,Properties=new MapDataProps(),Size=new Vector3s(64,32,64),
        BlocksData=new byte[64*32*64*6].Zip(0).ToArray(),SpawnPoints=[
            new MapSpawnPoint{Team=TeamType.Team1,Label=SpawnPointLabel.Base,Position=new Vector3(20,4,20)},
            new MapSpawnPoint{Team=TeamType.Team2,Label=SpawnPointLabel.Base,Position=new Vector3(50,4,50)}]};
    var service=Stub<IServiceZone>();
    var zone=new GameZone(service,service,Stub<IBuffer>(),Stub<ISender>(),map,
        Stub<IGameInitiator>(new(){["GetGameMode"]=new Key("game_mode_friendly"),["get_GameInstanceId"]="activation-fixture"}),players);
    object? Call(string name,params object?[] args)=>typeof(GameZone).GetMethod(name,Hidden)!.Invoke(zone,args);
    T OnZone<T>(Func<T> action){var t=new TaskCompletionSource<T>();zone.EnqueueAction(()=>{try{t.SetResult(action());}catch(Exception e){t.SetException(e);}});return t.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();}
    try
    {
        OnZone(()=>
        {
            var units=players.Keys.Order().Select(id=>(Unit)Call("CreatePlayerUnit",id,service)!).ToArray();
            var caster=units[0];var ally=units[1];var late=units[2];var enemy=units[3];var dead=units[4];
            foreach(var u in units)u.ZoneService=service;
            ((ZoneData)typeof(GameZone).GetField("_zoneData",Hidden)!.GetValue(zone)!).Phase=new ZonePhase{PhaseType=ZonePhaseType.Assault};
            var origin=new Vector3(20,4,20);
            var octree=typeof(GameZone).GetField("_unitOctree",Hidden)!.GetValue(zone)!;
            void Place(Unit u,Vector3 p){octree.GetType().GetMethod("Remove",[typeof(Unit)])!.Invoke(octree,[u]);u.Transform.Position=p;Call("AddUnitToOctree",u,u.Transform);}
            Place(caster,origin);Place(ally,origin+new Vector3(radius-1,0,0));Place(late,origin+new Vector3(radius+2,0,0));
            Place(enemy,origin+Vector3.UnitZ);Place(dead,origin+Vector3.UnitX);dead.IsDead=true;
            var updater=(UnitUpdater)typeof(GameZone).GetField("_defaultUnitUpdater",Hidden)!.GetValue(zone)!;
            Unit? drill=null;
            if(!kira)drill=CatalogueFactory.CreateUnit(1000,new Key("unit_device_trondson_torpedo"),
                new ZoneTransform{Position=origin+new Vector3(0,0,2)},TeamType.Team1,caster,updater)!;
            var all=drill==null?units:units.Append(drill).ToArray();
            void Buffs(){for(int i=0;i<3;i++)foreach(var u in all)u.ApplyBuffEffects(.2f);}
            var marker=new Key("effect_hero_"+family+"_cast"+suffix);
            bool Has(Unit u)=>u.ActiveEffects.Any(e=>e.Key==marker);
            var ability=new Key("ability_"+family+suffix);caster.AbilityKey=ability;caster.AbilityCharges=1;
            zone.ReceivedAbilityCastRequest(1,1,new AbilityCastData{ShotPos=origin+new Vector3(100,0,0),
                Shots=[new ShotData{ShotId=4242,TargetPos=origin+new Vector3(100,0,0)}]},service);
            Check(caster.AbilityCharges==0&&Has(ally),"native cast grants buff immediately using server origin");
            Check(!Has(late)&&!Has(enemy)&&!Has(dead),"late, enemy and dead players excluded");
            Check(kira?Has(caster):caster.ActiveEffects.Any(e=>e.Key==new Key("effect_hero_trondson_heigh_ho_buff_self")),"caster's existing F bonuses retained");
            var end=ally.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime!.Value;
            Check(Math.Abs(((long)end-DateTimeOffset.Now.ToUnixTimeMilliseconds())/1000f-duration)<.5f,"native variant duration retained");
            Buffs();
            var ledger=(ActionScoreLedger)typeof(GameZone).GetProperty("ActionScores",Hidden)!.GetValue(zone)!;
            if(kira)
            {
                Near(ally.GetBuff(BuffType.WeaponReload),suffix=="_idoru"?1:.5f,"allied reload strength retained");
                Near(caster.GetBuff(BuffType.WeaponReload),suffix=="_idoru"?1:.5f,"self reload strength retained");
                Near(caster.GetBuff(BuffType.InfiniteAmmo),1,"Kira retains own unlimited ammo");
                Near(ally.GetBuff(BuffType.InfiniteAmmo),0,"unlimited ammo is not shared with teammates");
                Check(ledger.Breakdown(1).GetValueOrDefault("Reload boost")==5,"existing caster reload-assist scoring preserved");
            }
            else
            {
                Near(ally.GetBuff(BuffType.ToolWorldDamage),2,"allied digging strength retained");
                Near(ally.GetBuff(BuffType.MiningHealthRefill),.05f,"digging health refill retained");
                Near(ally.GetBuff(BuffType.SplashBlockDamageBonus),.5f,"allied blast bonus retained");
                Near(caster.GetBuff(BuffType.ToolWorldDamage),2,"caster digging bonus retained");
                Near(ally.GetBuff(BuffType.MiningBonus),suffix.Length>0?.2f:0,"mining perk strength retained");
                Near(drill!.GetBuff(BuffType.WorldDamage),2,"nearby drill gets native damage bonus");
                ally.ReportHeroBoostAction("effect_hero_trondson_heigh_ho_buff","Demolition boost assist",ally.GetMidpoint());
                Check(ledger.Breakdown(1).GetValueOrDefault("Demolition boost assist")==10,"existing digging-assist source credit preserved");
            }
            Check(ledger.Breakdown(2).GetValueOrDefault("Build boost received")==0,"Tony-only receipt scoring not added to other heroes");
            Check(caster.AuraEffects.Keys.All(a=>a.ConstantEffects?.All(k=>
                !k.GetCard<CardEffect>()!.Id!.StartsWith("effect_hero_"+family))!=false),"F leaves no carried aura");
            Place(ally,origin+new Vector3(radius+8,0,0));Place(late,origin+Vector3.UnitX);
            if(drill!=null)Place(drill,origin+new Vector3(radius+8,0,2));
            Buffs();Check(Has(ally)&&!Has(late),"leaving retains buff; entering later grants nothing");
            Check(ally.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime==end,"moving never refreshes duration");
            zone.ReceivedAbilityCastRequest(9,1,new AbilityCastData(),service);
            Check(!Has(late)&&caster.AbilityCharges==0,"rejected cooldown cast cannot grant buffs");
            zone.ReceivedHit((ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds(),new(){[4242]=new HitData{InsidePoint=origin}});
            Check(!Has(late),"delayed client hit cannot add recipients");
            if(thrills)
            {
                // Exercise the native opposing-player kill path while the original ally is out of range.
                ally.ActiveEffects.Single(e=>e.Key==marker).UpdateDuration(2f);
                var oldEnd=ally.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime;
                enemy.Killed(caster.CreateImpactData());Buffs();
                Check(ally.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime>oldEnd,"kill refreshes original distant ally");
                Check(!Has(late),"kill refresh does not recruit nearby late entrants");
                Check(caster.ActiveEffects.Any(e=>e.Key==new Key("effect_hero_kira_jpop_star_self_thrills_for_days_on_kill")),"kill refresh keeps native on-kill chain");
                // Death/respawn must not allow the original grant to cross into the recipient's next life.
                ally.Killed(ally.CreateBlankImpactData());Check(ally.Respawn(origin+Vector3.UnitX,Quaternion.Identity),"recipient respawn succeeds");
                Check(enemy.Respawn(origin+new Vector3(0,0,2),Quaternion.Identity),"kill target respawns");
                enemy.Killed(caster.CreateImpactData());Buffs();
                Check(!Has(ally)&&!Has(late),"later kill does not buff respawned or newly nearby players");
                var contexts=(System.Collections.IDictionary)typeof(GameZone).GetField("_thrillsRecipients",Hidden)!.GetValue(zone)!;
                var context=contexts[1u]!;
                context.GetType().GetProperty("ExpiresAt")!.SetValue(context,0UL);
                Check(enemy.Respawn(origin+new Vector3(0,0,2),Quaternion.Identity),"second kill target respawn succeeds");
                var selfEnd=caster.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime;
                enemy.Killed(caster.CreateImpactData());
                Check(contexts.Count==0&&caster.ActiveEffects.Single(e=>e.Key==marker).ExpirationTime==selfEnd,"expired cast cannot be resurrected by stale kill callback");
            }
            else
            {
                caster.AbilityCharges=1;zone.ReceivedAbilityCastRequest(2,1,new AbilityCastData(),service);Buffs();
                Check(Has(late),"a new accepted cast selects the new nearby recipient set");
                caster.Killed(caster.CreateBlankImpactData());Buffs();
                Check(Has(late),"caster death does not revoke a granted recipient buff");
            }
            foreach(var u in all)
                foreach(var e in u.ActiveEffects.Where(e=>e.Key==marker||e.Key==new Key("effect_hero_trondson_heigh_ho_drill_cast")))e.UpdateDuration(0UL);
            for(int i=0;i<3;i++)foreach(var u in all){u.CleanUpExpired();u.ApplyBuffEffects(.2f);}
            Near(ally.GetBuff(kira?BuffType.WeaponReload:BuffType.ToolWorldDamage),0,"wrapper expiry removes allied child buffs");
            Near(late.GetBuff(kira?BuffType.WeaponReload:BuffType.ToolWorldDamage),0,"late recipient's new-cast buff expires");
            if(kira)Near(caster.GetBuff(BuffType.WeaponReload),0,"self reload never outlives variant duration");
            else Near(drill!.GetBuff(BuffType.WorldDamage),0,"drill bonus expires after leaving range");
            return true;
        });
    }
    finally{zone.Stop();}
    Console.WriteLine("ACTIVATION "+family+suffix+" PASS");
}
Console.WriteLine("ACTIVATION_BUFFS PASS "+checks+" checks");

static T Stub<T>(Dictionary<string,object?>? returns=null)where T:class
{var proxy=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)proxy).Returns=returns??[];return proxy;}
public class StubProxy:DispatchProxy
{
    public Dictionary<string,object?> Returns=[];
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {if(Returns.TryGetValue(method!.Name,out var value))return value;var type=method.ReturnType;if(type==typeof(void))return null;
     if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";return type.IsValueType?Activator.CreateInstance(type):null;}
}
