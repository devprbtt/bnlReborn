using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var path=Path.Combine(Path.GetTempPath(),"bnl-shop-"+Guid.NewGuid()+".db");
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
try {
    var catalogue=(ServerCatalogue)Databases.Catalogue;
    var nigel=new Key("unit_hero_hunter");
    CardSkin Skin(string id,ScopeType scope)=>new(){Id=id,Scope=scope,HeroKey=nigel};
    CardShopItem Item(string id,float? gold,float? plat,params string[] items)=>new(){Id=id,AvailableInShop=true,PriceVirtual=gold,PriceReal=plat,Items=items.Select(i=>new Key(i)).ToList()};
    var sale=Item("shop_skin_frost",1500,null,"skin_frost");
    var bundle=Item("shop_bundle_frost_walnut",2000,200,"skin_frost","skin_walnut");
    var promo=Item("shop_skin_walnut_promo",1000,null,"skin_walnut");promo.Promotion=new ShopItemPromotion{From=DateTime.UtcNow.AddDays(-1),To=DateTime.UtcNow.AddDays(1),VirtualDiscount=.25f};
    var expired=Item("shop_skin_walnut_expired",1000,null,"skin_walnut");expired.Promotion=new ShopItemPromotion{From=DateTime.UtcNow.AddDays(-3),To=DateTime.UtcNow.AddDays(-2),VirtualDiscount=.5f};
    var hidden=Item("shop_skin_hidden",10,null,"skin_frost");hidden.AvailableInShop=false;
    var publicOnly=Item("shop_skin_public",10,null,"skin_default");
    var free=Item("shop_skin_free",null,null,"skin_frost");
    catalogue.Replicate([new CardGlobalLogic{Id="global_logic"},Skin("skin_frost",ScopeType.Private),Skin("skin_walnut",ScopeType.Private),Skin("skin_default",ScopeType.Public),
        sale,bundle,promo,expired,hidden,publicOnly,free]);
    Key K(string id)=>Catalogue.Key(id);

    // Pricing.
    Check(ShopPurchases.Price(promo,false,DateTime.UtcNow)==750,"active promotion discounts the Gold price");
    Check(ShopPurchases.Price(expired,false,DateTime.UtcNow)==1000,"expired promotion is ignored");
    Check(ShopPurchases.Price(free,false,DateTime.UtcNow)==null && ShopPurchases.Price(sale,true,DateTime.UtcNow)==null,"an item without that currency's price cannot be bought with it");
    Check(ShopPurchases.Grants(publicOnly).Count==0,"public cards are never sold");

    using(var db=new SQLiteConnection(path)){db.CreateTable<PlayerRecord>();db.CreateTable<InventoryGrantRecord>();db.CreateTable<ShopPurchaseRecord>();}
    var asyncDb=new SQLiteAsyncConnection(path);
    var master=(MasterServerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MasterServerDatabase));
    typeof(MasterServerDatabase).GetField("_playerDb",flags)!.SetValue(master,asyncDb);
    typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.SetValue(master,new SemaphoreSlim(1,1));
    var online=(System.Collections.Concurrent.ConcurrentDictionary<uint,PlayerData>)typeof(PlayerDatabase).GetField("_players",flags)!.GetValue(Databases.PlayerDatabase)!;
    var region=DispatchProxy.Create<IRegionServerDatabase,RegionProbe>();Databases.SetRegionDatabase(region);var probe=(RegionProbe)(object)region;
    async Task<PlayerRecord> NewPlayer(long gold,long plat){var r=new PlayerData().ToPlayerRecord();r.Gold=gold;r.Platinum=plat;await asyncDb.InsertAsync(r);online[r.PlayerId]=PlayerData.FromPlayerRecord(r);PlayerInventory.Set(r.PlayerId,[]);return r;}
    async Task<long> Gold(uint id)=>(await asyncDb.FindAsync<PlayerRecord>(id)).Gold;

    var poor=await NewPlayer(1499,0);
    Check(await master.PurchaseShopItem(poor.PlayerId,sale.Key,false)==PurchaseResult.InsufficientFunds,"one Gold short is rejected");
    Check(await Gold(poor.PlayerId)==1499 && !PlayerInventory.Owns(poor.PlayerId,K("skin_frost")) && await asyncDb.Table<ShopPurchaseRecord>().CountAsync()==0,"rejected purchase charges nothing, grants nothing, writes no receipt");

    var buyer=await NewPlayer(5000,300);
    Check(await master.PurchaseShopItem(buyer.PlayerId,sale.Key,false)==PurchaseResult.Purchased,"purchase with enough Gold succeeds");
    Check(await Gold(buyer.PlayerId)==3500 && PlayerInventory.Owns(buyer.PlayerId,K("skin_frost")),"price deducted and skin owned");
    Check(probe.Wallet![CurrencyType.Virtual]==3500 && probe.Inventory!.Any(i=>i.Item==K("skin_frost")),"client is told the new balance and inventory");
    var receipt=(await asyncDb.Table<ShopPurchaseRecord>().ToListAsync()).Single();
    Check(receipt.PlayerId==buyer.PlayerId && receipt.ShopItem=="shop_skin_frost" && receipt.Price==1500 && receipt.Items=="skin_frost" && receipt.Currency==CurrencyType.Virtual,"receipt records who bought what for how much");
    var grant=(await asyncDb.Table<InventoryGrantRecord>().ToListAsync()).Single();
    Check(grant.GrantedBy=="shop" && grant.Note=="shop_skin_frost","grant is attributed to the shop item");
    Check(await master.PurchaseShopItem(buyer.PlayerId,sale.Key,false)==PurchaseResult.AlreadyOwned && await Gold(buyer.PlayerId)==3500,"buying an owned skin again is refused without charge");

    Check(await master.PurchaseShopItem(buyer.PlayerId,bundle.Key,true)==PurchaseResult.Purchased,"bundle bought with Platinum");
    var after=await asyncDb.FindAsync<PlayerRecord>(buyer.PlayerId);
    Check(after.Platinum==100 && after.Gold==3500 && PlayerInventory.Owns(buyer.PlayerId,K("skin_walnut")),"Platinum price deducts only Platinum");
    Check((await asyncDb.Table<ShopPurchaseRecord>().ToListAsync()).Last().Items=="skin_walnut","bundle grants only what was missing");

    Check(await master.PurchaseShopItem(buyer.PlayerId,hidden.Key,false)==PurchaseResult.NotForSale,"item not available in the shop cannot be bought");
    Check(await master.PurchaseShopItem(buyer.PlayerId,publicOnly.Key,false)==PurchaseResult.NotForSale,"item selling only public cards is not for sale");
    Check(await master.PurchaseShopItem(buyer.PlayerId,free.Key,false)==PurchaseResult.NotForSale,"item without a price is not for sale");
    Check(await master.PurchaseShopItem(buyer.PlayerId,new Key("shop_missing"),false)==PurchaseResult.UnknownItem,"unknown item");
    Check(await master.PurchaseShopItem(9999,sale.Key,false)==PurchaseResult.UnknownPlayer,"unknown player");

    var promoBuyer=await NewPlayer(750,0);
    Check(await master.PurchaseShopItem(promoBuyer.PlayerId,promo.Key,false)==PurchaseResult.Purchased && await Gold(promoBuyer.PlayerId)==0,"promotion price is what is charged");

    // Double-click: concurrent requests for the same item charge once.
    var racer=await NewPlayer(3000,0);
    var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>master.PurchaseShopItem(racer.PlayerId,sale.Key,false)));
    Check(results.Count(r=>r==PurchaseResult.Purchased)==1 && results.Count(r=>r==PurchaseResult.AlreadyOwned)==7 && await Gold(racer.PlayerId)==1500,"eight simultaneous purchases charge exactly once");

    // A wallet credited by a match while the purchase waits is not overwritten.
    var gate=(SemaphoreSlim)typeof(MasterServerDatabase).GetField("_asyncLock",flags)!.GetValue(master)!;
    var late=await NewPlayer(1500,0);
    await gate.WaitAsync();
    var pending=master.PurchaseShopItem(late.PlayerId,sale.Key,false);
    await asyncDb.ExecuteAsync("UPDATE Users SET gold = gold + 200 WHERE id = ?",late.PlayerId);
    gate.Release();
    Check(await pending==PurchaseResult.Purchased && await Gold(late.PlayerId)==200,"deduction applies to the stored balance, keeping Gold credited meanwhile");

    using(var reopened=new SQLiteConnection(path))
        Check(reopened.Table<InventoryGrantRecord>().Count(g=>g.PlayerId==buyer.PlayerId)==2 && reopened.Find<PlayerRecord>(buyer.PlayerId).Gold==3500,"ownership and wallet persist");
    foreach(var id in online.Keys.ToList())online.TryRemove(id,out _);
    await asyncDb.CloseAsync();
} finally {SQLiteAsyncConnection.ResetPool();File.Delete(path);}
Console.WriteLine($"PASS shop purchases: {checks} checks");

public class RegionProbe : DispatchProxy
{
    public Dictionary<CurrencyType,float>? Wallet;
    public List<InventoryItem>? Inventory;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if(method!.Name=="NotifyCurrency")Wallet=new((Dictionary<CurrencyType,float>)args![1]!);
        if(method.Name=="NotifyInventory")Inventory=(List<InventoryItem>)args![1]!;
        return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
}
