using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
int checks=0;
void Check(bool v,string n){if(!v)throw new Exception(n);checks++;Console.WriteLine("PASS "+n);}
void Field(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
var grantDirectory=Path.Combine(Path.GetTempPath(),"bnl-private-skin-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(grantDirectory);
var grantPath=Path.Combine(grantDirectory,"private_skin_grants.json");
var grantedSteam=76561198366278223UL;
File.WriteAllText(grantPath,"{\"skin_hunter_arctic_wolf_private\":[\""+grantedSteam+"\"]}");
Environment.SetEnvironmentVariable("BNL_PRIVATE_SKIN_GRANTS_PATH",grantPath);
var normal=new Key("skin_hunter_s1");var hunter=new Key("unit_hero_hunter");var boxer=new Key("unit_hero_boxer");
foreach(var pair in new[]{(PrivateSkinAccess.ArcticWolf,hunter),(PrivateSkinAccess.Demon,boxer)})
{
 foreach(ulong? steam in new ulong?[]{null,0,76561197990315751,grantedSteam,PrivateSkinAccess.TestSteamId})
 {
  var allowed=steam==PrivateSkinAccess.TestSteamId || (steam==grantedSteam && pair.Item1==PrivateSkinAccess.ArcticWolf);
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
File.WriteAllText(grantPath,"{\"skin_boxer_demon_private\":[\""+grantedSteam+"\"],\"ignored_public_skin\":[\""+grantedSteam+"\"]}");
File.SetLastWriteTimeUtc(grantPath,DateTime.UtcNow.AddSeconds(2));
Check(!PrivateSkinAccess.CanUse(grantedSteam,PrivateSkinAccess.ArcticWolf),"live file update revokes Nigel grant");
Check(PrivateSkinAccess.CanUse(grantedSteam,PrivateSkinAccess.Demon),"live file update grants Demon skin");
File.WriteAllText(grantPath,"not json");
File.SetLastWriteTimeUtc(grantPath,DateTime.UtcNow.AddSeconds(4));
Check(PrivateSkinAccess.CanUse(grantedSteam,PrivateSkinAccess.Demon),"invalid update preserves last valid grants");
File.WriteAllText(grantPath,"{\"skin_hunter_arctic_wolf_private\":[\""+grantedSteam+"\"]}");
File.SetLastWriteTimeUtc(grantPath,DateTime.UtcNow.AddSeconds(6));
var catalogue=(ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(new List<Card>{new CardUnit{Id="unit_hero_hunter",Key=hunter,Data=new UnitDataPlayer{Skins=new(){normal,PrivateSkinAccess.ArcticWolf}}},new CardSkin{Id="skin_hunter_s1",Key=normal},new CardSkin{Id="skin_hunter_arctic_wolf_private",Key=PrivateSkinAccess.ArcticWolf}});
Check(PrivateSkinAccess.SafeSkin(0,hunter,PrivateSkinAccess.ArcticWolf)==normal,"stale private loadout falls back to public skin");
Check(PrivateSkinAccess.SafeSkin(PrivateSkinAccess.TestSteamId,hunter,PrivateSkinAccess.ArcticWolf)==PrivateSkinAccess.ArcticWolf,"owner stored loadout preserved");
foreach(var owner in new[]{false,true})
{
 var p=new PlayerData{SteamId=owner?PrivateSkinAccess.TestSteamId:0,HeroLoadouts=new(){[hunter]=new LobbyLoadout{HeroKey=hunter,SkinKey=PrivateSkinAccess.ArcticWolf}}};
 p.SanitizeAgainstCatalogue();Check(p.HeroLoadouts.ContainsKey(hunter)==owner,"persistence sanitizer enforces account access "+owner);
}
var registration = new List<Card>{new CardUnit{Id="unit_hero_hunter",Data=new UnitDataPlayer{Skins=[normal]}},new CardUnit{Id="unit_hero_boxer",Data=new UnitDataPlayer{Skins=[]}},new CardSkin{Id="skin_hunter_s1",Prefab="original",Bundle="character_longshot"},new CardSkin{Id="skin_boxer_s6",Bundle="character_sweetscience"}};
PrivateSkinRegistration.Register(registration);
PrivateSkinRegistration.Register(registration);
foreach(var id in new[]{"skin_hunter_arctic_wolf_private","skin_boxer_demon_private"})
{
 Check(registration.Count(c=>c.Id==id)==1,"catalogue registration idempotent "+id);
 var skin=registration.OfType<CardSkin>().Single(c=>c.Id==id);
 Check(skin.Name?.Text!=null && skin.Prefab!=null && skin.FpsPrefab!=null && skin.IconPortrait!=null,"render and portrait routes present "+id);
 Check(registration.OfType<CardUnit>().SelectMany(c=>((UnitDataPlayer)c.Data!).Skins!).Count(k=>k==new Key(id))==1,"hero skin registration unique "+id);
}
Check(!registration.OfType<CardShopItem>().Any(),"registration adds no shop offers");
Check(registration.OfType<CardSkin>().Single(c=>c.Id=="skin_hunter_s1").Prefab=="original","stock skin route unchanged");
registration.Add(new CardGlobalLogic{Id="global_logic",AvailableHeroes=[hunter,boxer]});
catalogue.Replicate(registration);
var inventoryDatabase=(PlayerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(PlayerDatabase));
Field(inventoryDatabase,"_players",new System.Collections.Concurrent.ConcurrentDictionary<uint,PlayerData>(new[]{new KeyValuePair<uint,PlayerData>(1,new PlayerData{SteamId=PrivateSkinAccess.TestSteamId}),new KeyValuePair<uint,PlayerData>(2,new PlayerData{SteamId=76561197990315751}),new KeyValuePair<uint,PlayerData>(3,new PlayerData{SteamId=grantedSteam})}));
var inventoryMethod=typeof(PlayerDatabase).GetMethod("GetInventory",BindingFlags.Instance|BindingFlags.NonPublic)!;
foreach(uint playerId in new uint[]{1,2,3})
{
 var inventory=(List<InventoryItem>)inventoryMethod.Invoke(inventoryDatabase,[playerId])!;
 foreach(var key in new[]{PrivateSkinAccess.ArcticWolf,PrivateSkinAccess.Demon})
  Check(inventory.Any(i=>i.Item==key)==(playerId==1 || (playerId==3 && key==PrivateSkinAccess.ArcticWolf)),"real inventory private access "+playerId+" "+key);
 Check(inventory.Any(i=>i.Item==normal),"real inventory preserves public skin "+playerId);
}
Directory.Delete(grantDirectory,true);
Console.WriteLine($"PRIVATE_SKINS_PASS {checks}");
public class Recording:DispatchProxy{public int Calls;protected override object? Invoke(MethodInfo? m,object?[]? a){Calls++;return null;}}
