using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;
using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Service;
using System.Runtime.CompilerServices;

int checks=0;
void Check(bool ok,string name){ if(!ok)throw new Exception(name);checks++; }
var classes=new[]{new Key("brawn"),new Key("skill"),new Key("brains")};
var pool=Enumerable.Range(0,15).Select(i=>(Hero:new Key("hero"+i),Class:classes[i/5])).ToArray();
var seen=new HashSet<string>();
for(int seed=0;seed<1000;seed++)
{
    var roster=new RandomHeroRoster(pool,classes,new Random(seed));
    Check(roster.Heroes.Count==5 && roster.Heroes.Distinct().Count()==5,"five distinct heroes");
    Check(classes.All(c=>roster.Heroes.Any(h=>pool.Single(p=>p.Hero==h).Class==c)),"every class represented");
    var a=Enumerable.Range(1,5).Select(i=>roster.Assign((uint)i,TeamType.Team1)).ToArray();
    var b=Enumerable.Range(6,5).Select(i=>roster.Assign((uint)i,TeamType.Team2)).ToArray();
    Check(a.ToHashSet().SetEquals(b) && a.Distinct().Count()==5,"identical team roster without duplicates");
    Check(roster.Assign(1,TeamType.Team1)==a[0],"reconnect keeps hero");
    seen.Add(string.Join(",",a));
}
Check(seen.Count>100,"assignments vary across random seeds");
foreach(bool? enabled in new bool?[]{null,false,true})
foreach(bool? third in new bool?[]{null,false,true})
{
    var update=new CustomGameUpdate{RandomHeroes=enabled,ForceThirdPerson=third,Status=CustomGameStatus.Preparing,GameName="Fixture"};
    using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);update.Write(writer);stream.Position=0;
    var copy=CustomGameUpdate.ReadRecord(new BinaryReader(stream));
    Check(copy.RandomHeroes==enabled && copy.ForceThirdPerson==third && copy.GameName=="Fixture" && stream.Position==stream.Length,"wire roundtrip");
}
// Legacy seven-bit updates still occupy one byte and must leave the new field unset.
using(var stream=new MemoryStream(new byte[]{0})) Check(CustomGameUpdate.ReadRecord(new BinaryReader(stream)).RandomHeroes==null,"legacy wire payload");
var group=(CustomGamePlayerGroup)RuntimeHelpers.GetUninitializedObject(typeof(CustomGamePlayerGroup));
void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(obj,value);
Set(group,"_lock",new System.Threading.Lock());
Set(group,"<GameInfo>k__BackingField",new CustomGameInfo{HeroSwitch=true,RandomHeroes=true,Status=CustomGameStatus.Preparing});
Set(group,"_players",new List<CustomGamePlayer>{new(){Id=1,Owner=true},new(){Id=2}});
Check(!group.CanSwitchHero(),"random mode overrides hero switching");
group.UpdateRandomHeroes(2,false);Check(group.GameInfo.RandomHeroes,"non-owner cannot change setting");
group.GameInfo.Status=CustomGameStatus.Match;
group.UpdateRandomHeroes(1,false);Check(group.GameInfo.RandomHeroes,"cannot change active match setting");
var lobby=(GameLobby)RuntimeHelpers.GetUninitializedObject(typeof(GameLobby));
Set(lobby,"_randomHeroes",new RandomHeroRoster(pool,classes,new Random(1)));
var canPick=typeof(GameLobby).GetMethod("CanPickHero",BindingFlags.Instance|BindingFlags.NonPublic)!;
Check(!(bool)canPick.Invoke(lobby,new object[]{new PlayerLobbyState{Hero=pool[0].Hero},pool[1].Hero})!,"server rejects another hero before ownership checks");
group.GameInfo.Status=CustomGameStatus.Preparing;
var service=DispatchProxy.Create<IServiceMatchmaker, Sink>();
Set(group,"<matchService>P",service);
group.UpdateRandomHeroes(1,false);
Check(!group.GameInfo.RandomHeroes && group.CanSwitchHero(),"owner disables mode and switching preference is restored");
Check(((Sink)(object)service).Last?.RandomHeroes==false,"setting broadcast to room");
group.UpdateRandomHeroes(1,true);Check(group.GameInfo.RandomHeroes,"owner enables mode");
if(args.Length>0)
{
    var cards=JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]),JsonHelper.DefaultSerializerSettings)!;
    ((ServerCatalogue)Databases.Catalogue).Replicate(cards);
    var real=RandomHeroRoster.CreateFromCatalogue();
    Check(real.Heroes.Count==5 && real.Heroes.All(h=>CatalogueHelper.GetHeroes().Contains(h)),"deployed catalogue yields five playable heroes");
    Check(real.Heroes.Select(h=>(h.GetCard<CardUnit>()!.Data as UnitDataPlayer)!.Class).Distinct().Count()==3,"deployed catalogue covers all three classes");
}
Console.WriteLine($"Random heroes fixture passed: {checks} checks.");

public class Sink:DispatchProxy
{
    public CustomGameUpdate? Last;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name==nameof(IServiceMatchmaker.SendUpdateCustomGame)) Last=(CustomGameUpdate)args![0]!;
        return null;
    }
}

