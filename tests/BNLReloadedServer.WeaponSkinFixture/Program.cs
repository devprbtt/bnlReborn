using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using SQLite;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var path=Path.Combine(Path.GetTempPath(),"bnl-weapon-skins-"+Guid.NewGuid()+".db");
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
try {
    var nigel=new Key("unit_hero_hunter");var sarge=new Key("unit_hero_sarge_stone");
    ((ServerCatalogue)Databases.Catalogue).Replicate([
        new CardGlobalLogic{Id="global_logic"},
        new CardSkin{Id="skin_hunter_s1",HeroKey=nigel},new CardSkin{Id="skin_hunter_s3",HeroKey=nigel},new CardSkin{Id="skin_sarge_s1",HeroKey=sarge},
        new CardSkin{Id="skin_weapon_hunter_ice_rifle",HeroKey=nigel,Scope=ScopeType.Private},
        new CardSkin{Id="skin_weapon_hunter_blackout",HeroKey=nigel,Scope=ScopeType.Private},
        new CardSkin{Id="skin_hunter_arctic_wolf_private",HeroKey=nigel,Scope=ScopeType.Private}]);
    Key K(string id)=>Catalogue.Key(id);
    const uint owner=1,other=2;
    PlayerInventory.Set(owner,["skin_weapon_hunter_ice_rifle","skin_weapon_hunter_blackout","skin_hunter_arctic_wolf_private"]);
    PlayerInventory.Set(other,[]);

    Check(WeaponSkinSelections.Validate(owner,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.Set,"owned weapon skin on its hero's character skin");
    Check(WeaponSkinSelections.Validate(owner,K("skin_hunter_s1"),Key.None)==WeaponSkinChange.Cleared,"clearing back to stock weapons");
    Check(WeaponSkinSelections.Validate(other,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.NotOwned,"unowned weapon skin refused");
    Check(WeaponSkinSelections.Validate(owner,K("skin_sarge_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.WrongHero,"another hero's weapon skin refused");
    Check(WeaponSkinSelections.Validate(owner,K("skin_hunter_s1"),K("skin_hunter_arctic_wolf_private"))==WeaponSkinChange.NotWeaponSkin,"a character skin is not a weapon skin");
    Check(WeaponSkinSelections.Validate(owner,K("skin_weapon_hunter_blackout"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.UnknownSkin,"a weapon skin cannot host another");
    Check(WeaponSkinSelections.Validate(owner,new Key("skin_missing"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.UnknownSkin,"unknown character skin refused");

    using(var db=new SQLiteConnection(path)){db.CreateTable<PlayerRecord>();db.CreateTable<WeaponSkinSelectionRecord>();}
    var asyncDb=new SQLiteAsyncConnection(path);
    var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
    typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
    typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
    WeaponSkinSelections.Load([]);

    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.Set,"save a choice");
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s3"),K("skin_weapon_hunter_blackout"))==WeaponSkinChange.Set,"save a second character skin's choice");
    Check(WeaponSkinSelections.For(owner,K("skin_hunter_s1"))==K("skin_weapon_hunter_ice_rifle") && WeaponSkinSelections.For(owner,K("skin_hunter_s3"))==K("skin_weapon_hunter_blackout"),"each character skin keeps its own weapon skin");
    Check(WeaponSkinSelections.For(owner,K("skin_hunter_s2"))==Key.None,"other character skins keep stock weapons");
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),K("skin_weapon_hunter_blackout"))==WeaponSkinChange.Set && WeaponSkinSelections.For(owner,K("skin_hunter_s1"))==K("skin_weapon_hunter_blackout"),"changing a choice replaces it");
    Check(await asyncDb.Table<WeaponSkinSelectionRecord>().CountAsync()==2,"one stored row per character skin");
    Check(await master.SetWeaponSkin(other,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.NotOwned && WeaponSkinSelections.For(other,K("skin_hunter_s1"))==Key.None,"refused choice is not stored");
    Check(WeaponSkinSelections.Json(owner)=="{\"skin_hunter_s1\":\"skin_weapon_hunter_blackout\",\"skin_hunter_s3\":\"skin_weapon_hunter_blackout\"}" || WeaponSkinSelections.Json(owner)=="{\"skin_hunter_s3\":\"skin_weapon_hunter_blackout\",\"skin_hunter_s1\":\"skin_weapon_hunter_blackout\"}","client receives its choices as card ids");

    WeaponSkinSelections.Load(await asyncDb.Table<WeaponSkinSelectionRecord>().ToListAsync());
    Check(WeaponSkinSelections.For(owner,K("skin_hunter_s1"))==K("skin_weapon_hunter_blackout"),"choices survive a server restart");
    PlayerInventory.Set(owner,["skin_weapon_hunter_ice_rifle"]);
    Check(WeaponSkinSelections.For(owner,K("skin_hunter_s1"))==Key.None && !WeaponSkinSelections.Of(owner).ContainsKey("skin_hunter_s1"),"a revoked weapon skin falls back to stock weapons");
    PlayerInventory.Set(owner,["skin_weapon_hunter_ice_rifle","skin_weapon_hunter_blackout"]);
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),Key.None)==WeaponSkinChange.Cleared && WeaponSkinSelections.For(owner,K("skin_hunter_s1"))==Key.None && await asyncDb.Table<WeaponSkinSelectionRecord>().CountAsync()==1,"clearing removes the stored choice");
    await asyncDb.CloseAsync();

    // Wire: the field is last, so a client that reads 13 bits stays aligned and ignores it.
    byte[] Bytes(ZoneUpdate u){using var ms=new MemoryStream();using var w=new BinaryWriter(ms);u.Write(w);return ms.ToArray();}
    var only=Bytes(new ZoneUpdate{WeaponSkinsJson="{\"3\":\"skin_weapon_hunter_ice_rifle\"}"});
    using(var r=new BinaryReader(new MemoryStream(only))){var old=new BitField(13);old.Read(r);
        Check(Enumerable.Range(0,13).All(i=>!old[i]),"released client sees an empty update");}
    var back=ZoneUpdate.ReadRecord(new BinaryReader(new MemoryStream(only)));
    Check(back.WeaponSkinsJson=="{\"3\":\"skin_weapon_hunter_ice_rifle\"}","new client reads the weapon skins");
    var both=Bytes(new ZoneUpdate{ActionScoresJson="{\"a\":1}",WeaponSkinsJson="{}"});
    using(var r=new BinaryReader(new MemoryStream(both))){var old=new BitField(13);old.Read(r);
        Check(old[12] && r.ReadString()=="{\"a\":1}","released client still reads the fields before it");}
} finally {SQLiteAsyncConnection.ResetPool();File.Delete(path);}
Console.WriteLine($"PASS weapon skins: {checks} checks");
