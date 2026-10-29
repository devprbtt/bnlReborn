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
        new CardSkin{Id="skin_weapon_sarge_stone_tommy_gun",HeroKey=sarge,Scope=ScopeType.Private},
        // New in the shop: is_new, on sale, priced and selling a private card. Not new: Blackout's (no is_new), a public skin's.
        new CardShopItem{Id="shop_item_skin_weapon_hunter_ice_rifle",IsNew=true,AvailableInShop=true,PriceVirtual=2000,Items=[new Key("skin_weapon_hunter_ice_rifle")]},
        new CardShopItem{Id="shop_item_skin_weapon_hunter_blackout",IsNew=false,AvailableInShop=true,PriceVirtual=2000,Items=[new Key("skin_weapon_hunter_blackout")]},
        new CardShopItem{Id="shop_item_skin_hunter_s1",IsNew=true,AvailableInShop=true,PriceVirtual=2000,Items=[new Key("skin_hunter_s1")]}]);
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
    // Skins put on sale stay with the accounts that had them while public: one grant each, once.
    var salePath=Path.Combine(Path.GetTempPath(),"bnl-sale-skins-"+Guid.NewGuid().ToString("N")+".db");
    using(var sale=new SQLiteConnection(salePath))
    {
        sale.CreateTable<PlayerRecord>();sale.CreateTable<InventoryGrantRecord>();
        for(ulong steam=1;steam<=3;steam++)sale.Insert(new PlayerRecord{SteamId=steam});
        sale.Execute("INSERT INTO InventoryGrants (player_id, item, granted_at, granted_by, note) VALUES (1, 'skin_abe_christmas', 0, 'test', '')");
        var first=MasterServerDatabase.GrantSaleSkins(sale,DateTimeOffset.UtcNow);
        var again=MasterServerDatabase.GrantSaleSkins(sale,DateTimeOffset.UtcNow);
        var rows=sale.Table<InventoryGrantRecord>().ToList();
        Check(first==(3,8) && again==(3,0) && rows.Count==9 && MasterServerDatabase.SaleSkins.All(s=>rows.Count(g=>g.Item==s)==3),
            "every existing account keeps the skins put on sale, granted once ("+first+", then "+again+", "+rows.Count+" rows)");
    }
    File.Delete(salePath);

    using(var db=new SQLiteConnection(path)){db.CreateTable<PlayerRecord>();db.CreateTable<WeaponSkinSelectionRecord>();db.CreateTable<SeenShopItemRecord>();}
    var asyncDb=new SQLiteAsyncConnection(path);
    var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
    typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
    typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
    WeaponSkinSelections.Load([]);

    // NewItems: new shop items the player neither owns nor has opened, as shop item keys.
    ShopNewItems.Load([]);
    var ice=K("shop_item_skin_weapon_hunter_ice_rifle");
    string N(uint player)=>string.Join(",",ShopNewItems.For(player).Select(k=>k.GetCard<CardShopItem>()!.Id));
    Check(N(other)=="shop_item_skin_weapon_hunter_ice_rifle","only an is_new, priced item selling a private card is new ("+N(other)+")");
    Check(N(owner)=="","an item the player already owns is not new to them");
    Check(ShopNewItems.Resolve(K("skin_weapon_hunter_ice_rifle"))?.Key==ice && ShopNewItems.Resolve(K("shop_item_skin_weapon_hunter_blackout"))==null,
        "a shown skin key resolves to the new shop item selling it; an item not new resolves to nothing");
    Check((await master.MarkShopItemSeen(other,K("skin_weapon_hunter_ice_rifle")))?.Key==ice && N(other)=="","opening the item takes it out of the player's NewItems");
    Check(await master.MarkShopItemSeen(other,ice)==null && await asyncDb.Table<SeenShopItemRecord>().CountAsync()==1,"opening it again changes nothing; one stored row");
    ShopNewItems.Load(await asyncDb.Table<SeenShopItemRecord>().ToListAsync());
    Check(N(other)=="","seen items stay seen after a server restart");

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
