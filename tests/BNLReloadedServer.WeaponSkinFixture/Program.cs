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
        new CardSkin{Id="skin_weapon_hunter_old_school_cricket_bat",HeroKey=nigel,Scope=ScopeType.Private},
        new CardSkin{Id="skin_weapon_hunter_arctic_wolf_rifle",HeroKey=nigel,Scope=ScopeType.Private},
        new CardSkin{Id="skin_hunter_arctic_wolf_private",HeroKey=nigel,Scope=ScopeType.Private},
        new CardSkin{Id="skin_sarge_stone_s1",HeroKey=sarge},new CardSkin{Id="skin_sarge_stone_s3",HeroKey=sarge},
        new CardSkin{Id="skin_weapon_sarge_stone_m60",HeroKey=sarge,Scope=ScopeType.Private},
        new CardSkin{Id="skin_weapon_sarge_stone_tommy_gun",HeroKey=sarge,Scope=ScopeType.Private}]);
    Key K(string id)=>Catalogue.Key(id);
    string F(uint player,string character)=>WeaponSkinSelections.Ids(WeaponSkinSelections.For(player,K(character)));
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
    // Derived: a character skin's own weapon is owned with that skin. Public skins are everyone's, so Old School
    // Nigel's bat is too; a private skin's weapons need its grant.
    Check(WeaponSkinSelections.Validate(other,K("skin_hunter_s1"),K("skin_weapon_hunter_old_school_cricket_bat"))==WeaponSkinChange.Set,"a public skin's own weapon is everyone's");
    PlayerInventory.Set(other,["skin_hunter_arctic_wolf_private"]);
    Check(WeaponSkinSelections.Validate(other,K("skin_hunter_s1"),K("skin_weapon_hunter_arctic_wolf_rifle"))==WeaponSkinChange.Set,"a private skin's own weapon is owned with its grant");
    PlayerInventory.Set(other,[]);
    Check(WeaponSkinSelections.Validate(other,K("skin_hunter_s1"),K("skin_weapon_hunter_arctic_wolf_rifle"))==WeaponSkinChange.NotOwned,"without the grant it is not");
    Check(!PlayerInventory.CanEquipSkin(owner,K("unit_hero_hunter"),K("skin_weapon_hunter_ice_rifle")) && PlayerInventory.CanEquipSkin(owner,K("unit_hero_hunter"),K("skin_hunter_arctic_wolf_private")),"an owned weapon skin cannot be worn as the hero skin");
    // Every hero: Al Castone's Tommy Gun on Sarge's other skins, and the default M60 back on Al Castone.
    Check(WeaponSkinSelections.Validate(other,K("skin_sarge_stone_s1"),K("skin_weapon_sarge_stone_tommy_gun"))==WeaponSkinChange.Set
        && WeaponSkinSelections.Validate(other,K("skin_sarge_stone_s3"),K("skin_weapon_sarge_stone_m60"))==WeaponSkinChange.Set,"another hero's derived weapons follow their public skins");
    Check(WeaponSkinSelections.Validate(other,K("skin_hunter_s1"),K("skin_weapon_sarge_stone_tommy_gun"))==WeaponSkinChange.WrongHero,"a derived weapon stays on its own hero");
    Check(WeaponSkinSelections.Derived.Count==52 && WeaponSkinSelections.Derived.All(d=>d.Key.StartsWith(WeaponSkinSelections.Prefix) && !d.Value.StartsWith(WeaponSkinSelections.Prefix)),
        "every derived weapon skin names a character skin (52, as the client table)");

    using(var db=new SQLiteConnection(path)){db.CreateTable<PlayerRecord>();db.CreateTable<WeaponSkinSelectionRecord>();}
    var asyncDb=new SQLiteAsyncConnection(path);
    var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
    typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
    typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
    WeaponSkinSelections.Load([]);

    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.Set,"save a choice");
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s3"),K("skin_weapon_hunter_blackout"))==WeaponSkinChange.Set,"save a second character skin's choice");
    Check(F(owner,"skin_hunter_s1")=="skin_weapon_hunter_ice_rifle" && F(owner,"skin_hunter_s3")=="skin_weapon_hunter_blackout","each character skin keeps its own weapon skin");
    Check(F(owner,"skin_hunter_s2")=="","other character skins keep stock weapons");
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),K("skin_weapon_hunter_blackout"))==WeaponSkinChange.Set && F(owner,"skin_hunter_s1")=="skin_weapon_hunter_blackout","changing a choice replaces it");
    Check(await asyncDb.Table<WeaponSkinSelectionRecord>().CountAsync()==2,"one stored row per character skin");
    Check(await master.SetWeaponSkin(other,K("skin_hunter_s1"),K("skin_weapon_hunter_ice_rifle"))==WeaponSkinChange.NotOwned && F(other,"skin_hunter_s1")=="","refused choice is not stored");
    Check(WeaponSkinSelections.Json(owner)=="{\"skin_hunter_s1\":\"skin_weapon_hunter_blackout\",\"skin_hunter_s3\":\"skin_weapon_hunter_blackout\"}" || WeaponSkinSelections.Json(owner)=="{\"skin_hunter_s3\":\"skin_weapon_hunter_blackout\",\"skin_hunter_s1\":\"skin_weapon_hunter_blackout\"}","client receives its choices as card ids");

    // One weapon skin per weapon: the client sends the whole list for a character skin.
    Check(await master.SetWeaponSkins(owner,K("skin_hunter_s3"),[K("skin_weapon_hunter_ice_rifle"),K("skin_weapon_hunter_blackout")])==WeaponSkinChange.Set
        && F(owner,"skin_hunter_s3")=="skin_weapon_hunter_ice_rifle,skin_weapon_hunter_blackout","a character skin wears several weapon skins, in the order sent");
    Check((await asyncDb.Table<WeaponSkinSelectionRecord>().Where(r=>r.CharacterSkin=="skin_hunter_s3").FirstAsync()).WeaponSkin=="skin_weapon_hunter_ice_rifle,skin_weapon_hunter_blackout","stored as one row of comma-separated ids");
    Check(await master.SetWeaponSkins(owner,K("skin_hunter_s3"),[K("skin_weapon_hunter_ice_rifle"),K("skin_hunter_arctic_wolf_private")])==WeaponSkinChange.NotWeaponSkin
        && F(owner,"skin_hunter_s3")=="skin_weapon_hunter_ice_rifle,skin_weapon_hunter_blackout","a list with one bad entry is refused whole and changes nothing");
    Check(await master.SetWeaponSkins(owner,K("skin_hunter_s3"),Enumerable.Repeat(K("skin_weapon_hunter_ice_rifle"),9).ToList())==WeaponSkinChange.Set && F(owner,"skin_hunter_s3")=="skin_weapon_hunter_ice_rifle","repeats collapse to one entry");
    Check(await master.SetWeaponSkins(owner,K("skin_hunter_s3"),[K("skin_weapon_hunter_ice_rifle"),K("skin_weapon_hunter_blackout")])==WeaponSkinChange.Set,"restore the two-skin list");
    WeaponSkinSelections.Load(await asyncDb.Table<WeaponSkinSelectionRecord>().ToListAsync());
    Check(F(owner,"skin_hunter_s1")=="skin_weapon_hunter_blackout","choices survive a server restart");
    PlayerInventory.Set(owner,["skin_weapon_hunter_ice_rifle"]);
    Check(F(owner,"skin_hunter_s3")=="skin_weapon_hunter_ice_rifle","a revoked weapon skin drops out of a list; the others stay");
    Check(WeaponSkinSelections.For(owner,K("skin_hunter_s3")).Length==1 && WeaponSkinSelections.Of(owner)["skin_hunter_s3"]=="skin_weapon_hunter_ice_rifle","clients receive only the owned entries");
    Check(F(owner,"skin_hunter_s1")=="" && !WeaponSkinSelections.Of(owner).ContainsKey("skin_hunter_s1"),"a revoked weapon skin falls back to stock weapons");
    PlayerInventory.Set(owner,["skin_weapon_hunter_ice_rifle","skin_weapon_hunter_blackout"]);
    Check(await master.SetWeaponSkin(owner,K("skin_hunter_s1"),Key.None)==WeaponSkinChange.Cleared && F(owner,"skin_hunter_s1")=="" && await asyncDb.Table<WeaponSkinSelectionRecord>().CountAsync()==1,"clearing removes the stored choice");
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
