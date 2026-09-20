using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
int checks=0;
void Check(bool v,string n){if(!v)throw new Exception(n);checks++;Console.WriteLine("PASS "+n);}
void Field(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
var normal=new Key("skin_hunter_s1");var hunter=new Key("unit_hero_hunter");var boxer=new Key("unit_hero_boxer");
foreach(var pair in new[]{(PrivateSkinAccess.ArcticWolf,hunter),(PrivateSkinAccess.Demon,boxer)})
{
 foreach(ulong? steam in new ulong?[]{null,0,76561197990315751,PrivateSkinAccess.TestSteamId})
 {
  var allowed=steam==PrivateSkinAccess.TestSteamId;
  Check(PrivateSkinAccess.CanUse(steam,pair.Item1)==allowed,"private ownership "+steam);
  Check(PrivateSkinAccess.CanEquip(steam,pair.Item2,pair.Item1)==allowed,"private equip "+steam);
  var lobby=(GameLobby)RuntimeHelpers.GetUninitializedObject(typeof(GameLobby));var data=new LobbyData();
  data.Players[1]=new PlayerLobbyState{SteamId=steam,Hero=pair.Item2,SkinKey=normal};
  var service=DispatchProxy.Create<IServiceLobby,Recording>();Field(lobby,"<LobbyData>k__BackingField",data);Field(lobby,"_serviceLobby",service);
  lobby.SelectSkin(1,pair.Item1);
  Check(data.Players[1].SkinKey==(allowed?pair.Item1:normal),"actual lobby rejects forged selection "+steam);
  Check(((Recording)(object)service).Calls==(allowed?1:0),"rejected selection not broadcast "+steam);
 }
 Check(!PrivateSkinAccess.CanEquip(PrivateSkinAccess.TestSteamId,new Key("unit_hero_other"),pair.Item1),"owner cannot equip wrong hero");
}
Check(PrivateSkinAccess.CanUse(null,normal),"public skin unaffected");
var catalogue=(ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(new List<Card>{new CardUnit{Id="unit_hero_hunter",Key=hunter,Data=new UnitDataPlayer{Skins=new(){normal,PrivateSkinAccess.ArcticWolf}}},new CardSkin{Id="skin_hunter_s1",Key=normal},new CardSkin{Id="skin_hunter_arctic_wolf_private",Key=PrivateSkinAccess.ArcticWolf}});
Check(PrivateSkinAccess.SafeSkin(0,hunter,PrivateSkinAccess.ArcticWolf)==normal,"stale private loadout falls back to public skin");
Check(PrivateSkinAccess.SafeSkin(PrivateSkinAccess.TestSteamId,hunter,PrivateSkinAccess.ArcticWolf)==PrivateSkinAccess.ArcticWolf,"owner stored loadout preserved");
foreach(var owner in new[]{false,true})
{
 var p=new PlayerData{SteamId=owner?PrivateSkinAccess.TestSteamId:0,HeroLoadouts=new(){[hunter]=new LobbyLoadout{HeroKey=hunter,SkinKey=PrivateSkinAccess.ArcticWolf}}};
 p.SanitizeAgainstCatalogue();Check(p.HeroLoadouts.ContainsKey(hunter)==owner,"persistence sanitizer enforces account access "+owner);
}
Console.WriteLine($"PRIVATE_SKINS_PASS {checks}");
public class Recording:DispatchProxy{public int Calls;protected override object? Invoke(MethodInfo? m,object?[]? a){Calls++;return null;}}
