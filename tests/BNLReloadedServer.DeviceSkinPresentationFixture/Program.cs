using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
Key K(string id) => Catalogue.Key(id);
LocalizedString N(string value) => new() {Text=value, Data=[]};
var standard = new CardDevice {Id="device_magnus_tesla_coil", Name=N("Tesla"), GroupKey=K("family"), DeviceLevels=[K("stock1"),K("stock2")]};
var old = new CardDevice {Id="device_magnus_tesla_coil_disco", Name=N("Disco Tesla"), GroupKey=K("family"), DeviceLevels=[K("skin1"),K("skin2")]};
var encore = new CardDevice {Id="device_magnus_tesla_coil_disco_encore", Name=N("Encore Tesla"), GroupKey=K("family"),DeviceLevels=[K("skin1"),K("skin2")]};
var group = new CardDeviceGroup {Id="family",Devices=[K(standard.Id),K(old.Id),K(encore.Id)]};
var hero = new CardUnit {Id="hero",Data=new UnitDataPlayer {SpecialDevices=[K(standard.Id),K(old.Id),K(encore.Id)]}};
var stock1 = new CardUnit {Id="stock1",Name=N("Tesla level 1")};
var stock2 = new CardUnit {Id="stock2",Name=N("Tesla level 2")};
var skin1 = new CardUnit {Id="skin1",Name=N("Custom 1"),Prefab="DiscoEncore"};
var skin2 = new CardUnit {Id="skin2",Name=N("Custom 2"),Prefab="DiscoEncore"};
var shop = new CardShopItem {Id="shop",Items=[K(old.Id)],AvailableInShop=true};
Card[] cards=[standard,old,encore,group,hero,stock1,stock2,skin1,skin2,shop];
foreach(var card in cards) DeviceSkinPresentation.Apply(card,cards);
int count=0;void Check(bool value,string label) {if(!value)throw new Exception(label);count++;}
Check(old.Scope==ScopeType.Private,"old hidden");
Check(!group.Devices.Contains(K(old.Id))&&group.Devices.Contains(K(encore.Id)),"only disproportionate variant removed");
Check(!((UnitDataPlayer)hero.Data).SpecialDevices!.Contains(K(old.Id)),"hero picker retired");
Check(!shop.AvailableInShop,"retired shop disabled");
Check(old.Name==standard.Name&&encore.Name==standard.Name,"native device labels");
Check(skin1.Name==stock1.Name&&skin2.Name==stock2.Name,"matching level labels");
Check(skin1.Prefab=="DiscoEncore"&&skin2.Prefab=="DiscoEncore","cosmetic retained");
Check(DeviceSkinPresentation.IsRetired(K(old.Id))&&!DeviceSkinPresentation.IsRetired(K(encore.Id)),"retirement identity");
foreach(var card in cards) DeviceSkinPresentation.Apply(card,cards);
Check(group.Devices.Count==2&&skin1.Name==stock1.Name,"idempotent");
var blockBase = new CardBlock {Id="stockBlock", Visual=new BlockVisual {Name=N("Crate")}};
var blockSkin = new CardBlock {Id="skinBlock", BlockId=1106, Visual=new BlockVisual {Name=N("Christmas Present"), Icon="Christmas"}};
standard.DeviceLevels=[K(blockBase.Id)];encore.DeviceLevels=[K(blockSkin.Id)];
Card[] blockCards=[standard,encore,group,blockBase,blockSkin];
DeviceSkinPresentation.Apply(blockSkin,blockCards);
Check(blockSkin.Visual.Name==blockBase.Visual.Name,"block native name");
Check(blockSkin.BlockId==1106&&blockSkin.Visual.Icon=="Christmas","block identity and appearance retained");
if(args.Length>0)
{
    var real=System.Text.Json.JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]),BNLReloadedServer.ProtocolHelpers.JsonHelper.DefaultSerializerSettings)!;
    ((ServerCatalogue)Databases.Catalogue).Replicate(real);
    var magnus=real.OfType<CardUnit>().Single(c=>c.Data is UnitDataPlayer player&&player.SpecialDevices?.Contains(DeviceSkinPresentation.StockTesla)==true);
    var equipped=CatalogueHelper.GetDefaultDevices(K(magnus.Id!))!;
    equipped[1]=DeviceSkinPresentation.Retired;
    LoadoutRules.Sanitize(K(magnus.Id!),equipped,[]);
    Check(equipped[1]==DeviceSkinPresentation.StockTesla,"saved retired special device falls back to stock");
    Check(!LoadoutRules.CanUseDevice(K(magnus.Id!),DeviceSkinPresentation.Retired),"retired direct equip rejected");
    Check(!real.OfType<CardDeviceGroup>().Any(g=>g.Devices?.Contains(DeviceSkinPresentation.Retired)==true),"real catalogue picker removes retired model");
}
Console.WriteLine($"PASS {count} device presentation checks");
