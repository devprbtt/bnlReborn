#pragma warning disable CS9216
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.Servers;

const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
object Raw(Type t)=>RuntimeHelpers.GetUninitializedObject(t);
void Set(object o,string name,object? value)=>o.GetType().GetField(name,flags)!.SetValue(o,value);
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
var group=(CustomGamePlayerGroup)Raw(typeof(CustomGamePlayerGroup));
var roster=new List<CustomGamePlayer>{new(){Id=1,Owner=true,Team=TeamType.Team1},new(){Id=2,Team=TeamType.Team2}};
Set(group,"_lock",new System.Threading.Lock());Set(group,"_players",roster);Set(group,"_spectators",new List<uint>());
Set(group,"_changeTeamRequestsTeam1",new Queue<CustomGamePlayer>());Set(group,"_changeTeamRequestsTeam2",new Queue<CustomGamePlayer>());
Set(group,"<GameInfo>k__BackingField",new CustomGameInfo{MaxPlayers=4,Status=CustomGameStatus.Preparing});
var service=DispatchProxy.Create<IServiceMatchmaker,Sink>();Set(group,"<matchService>P",service);
Check(!group.GameInfo.TeamCombat,"off by default");
Check(!group.UpdateTeamCombat(2,true)&&!group.UpdateTeamCombat(99,true),"only room owner may change setting");
Check(group.UpdateTeamCombat(1,true)&&group.GetCustomGameUpdate().TeamCombat==true,"owner enables and join snapshot includes setting");
Check(((Sink)(object)service).Last?.TeamCombat==true,"room update broadcasts setting");
group.UpdateRandomHeroes(1,true);Check(!group.GameInfo.TeamCombat,"random mode reserves fixed team seats");
group.UpdateTeamCombat(1,true);Check(!group.GameInfo.RandomHeroes,"team combat disables random hero seats");
Check(!group.TrySwitchInMatch(1,out _),"in-game switch is rejected before match");
group.GameInfo.Status=CustomGameStatus.Match;
Check(!group.UpdateTeamCombat(1,false),"host cannot change active match setting");
Check(!group.TrySwitchInMatch(99,out _),"spectator or unknown player cannot switch");
Check(group.TrySwitchInMatch(1,out var team)&&team==TeamType.Team2&&group.GetTeamForPlayer(1)==TeamType.Team2,"switch updates authoritative room team");
roster.Add(new CustomGamePlayer{Id=3,Team=TeamType.Team1});
Check(!group.TrySwitchInMatch(3,out _),"full destination rejects switch");
Check(group.TrySwitchInMatch(1,out team)&&team==TeamType.Team1,"switch back to an available seat");
group.GameInfo.TeamCombat=false;Check(!group.TrySwitchInMatch(1,out _),"disabled setting rejects switching");
group.GameInfo.TeamCombat=true;

var zone=(GameZone)Raw(typeof(GameZone));Set(zone,"_gameInitiator",group);
var match=new CardMatch{Id="fixture_team_combat",Key=new Key("fixture_team_combat"),FriendlyFire=new FriedlyFire{
    DirectDamageReduction=1,SplashDamageReduction=1,SelfSplashDamageReduction=.5f,DevicesDirectDamageReduction=1,DevicesSplashDamageReduction=1,ObjectivesDamageReduction=1}};
((ServerCatalogue)Databases.Catalogue).Replicate(new List<Card>{match});
var data=(ZoneData)Raw(typeof(ZoneData));data.MatchKey=match.Key;Set(zone,"_zoneData",data);
var convert=typeof(GameZone).GetMethod("ConvertToDamageData",flags)!;
DamageData Damage(bool splash)=>(DamageData)convert.Invoke(zone,new object?[]{new Damage{PlayerDamage=100,WorldDamage=50,ObjectiveDamage=30},Vector3.Zero,Vector3.Zero,null,splash,false,1f,null})!;
Check(Damage(false).FriendDamage==100 && Damage(true).FriendDamage==100,"enabled direct and splash damage hit teammates at full strength");
Check(Damage(true).SelfDamage==50 && Damage(false).TeamDeviceDamage==0 && Damage(false).TeamObjectiveDamage==0,"self, friendly devices and objectives retain existing damage rules");
group.GameInfo.TeamCombat=false;Check(Damage(false).FriendDamage==0&&Damage(true).FriendDamage==0,"disabled retains friendly protection");
Set(zone,"_gameInitiator",Raw(typeof(DummyGameInitiator)));Check(!zone.AllowsCustomTeamCombat&&Damage(false).FriendDamage==0,"non-custom games cannot opt in");
Set(zone,"_gameInitiator",group);group.GameInfo.TeamCombat=true;
var targetCheck=typeof(GameZone).GetMethod("IsCustomFriendlyDamageTarget",flags)!;
Unit Actor(uint? id){var unit=(Unit)Raw(typeof(Unit));unit.PlayerId=id;unit.Team=TeamType.Team1;return unit;}
var source=Actor(1);var ally=Actor(3);
var effect=new InstEffectDamage{Targeting=new EffectTargeting{AffectedTeam=RelativeTeamType.Opponent}};
bool Target(Unit unit,InstEffect e)=>(bool)targetCheck.Invoke(zone,new object?[]{unit,source,TeamType.Team1,e})!;
Check(Target(ally,effect)&&!Target(source,effect),"opponent-only damage effects include allied players without adding self hits");
Check(!Target(Actor(null),effect),"target override excludes allied devices");
Check(!Target(ally,new InstEffectHeal{Targeting=effect.Targeting}),"healing and non-damage targeting remain unchanged");

var sender=DispatchProxy.Create<ISender,Sink>();var wire=(ServiceMatchmaker)Raw(typeof(ServiceMatchmaker));Set(wire,"<sender>P",sender);
wire.SendUpdateCustomGame(group.GetCustomGameUpdate());
var bytes=((Sink)(object)sender).Bytes!;
using(var reader=new BinaryReader(new MemoryStream(bytes))){reader.ReadByte();reader.ReadByte();var old=CustomGameUpdate.ReadRecord(reader);
Check(old.Players?.Count==3&&reader.BaseStream.Length-reader.BaseStream.Position==5,"legacy record remains readable with only an optional tail");
Check(reader.ReadUInt32()==0x54434D31u&&reader.ReadBoolean(),"new message tail carries enabled flag");}
Console.WriteLine($"Custom team combat: {checks} checks passed.");

public class Sink:DispatchProxy
{
    public CustomGameUpdate? Last;public byte[]? Bytes;
    protected override object? Invoke(MethodInfo? method,object?[]? args){
        if(method?.Name==nameof(IServiceMatchmaker.SendUpdateCustomGame))Last=(CustomGameUpdate)args![0]!;
        if(method?.Name=="Send"&&args![0] is BinaryWriter writer)Bytes=((MemoryStream)writer.BaseStream).ToArray();
        return method?.ReturnType==typeof(int)?0:null;
    }
}
