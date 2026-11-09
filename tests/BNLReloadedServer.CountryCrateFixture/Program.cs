using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
// Populate key-to-ID resolution before serializing cards for the fixture.
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonHelper.DefaultSerializerSettings), JsonHelper.DefaultSerializerSettings)!;
Key K(string id) => Catalogue.Key(id);
var stock = cards.OfType<CardDevice>().Single(c => c.Id == "device_block_crate");
var stockBlock = cards.OfType<CardBlock>().Single(c => K(c.Id!) == stock.DeviceLevels![0]);
// The archived pre-beta91 snapshot lacks the last two crates; include both explicitly.
foreach (var code in RetiredCountryCrates.Countries)
{
    if (cards.Any(c => c.Id == "device_block_crate_reborn_flag_" + code)) continue;
    var device = Clone(stock); device.Id = "device_block_crate_reborn_flag_" + code;
    var block = Clone(stockBlock); block.Id = "block_crate_reborn_flag_" + code;
    block.BlockId = (ushort)(code == "fo" ? 1133 : 1134);
    block.Visual!.Icon = "block_skin_flag_" + code;
    device.DeviceLevels = [K(block.Id)];
    cards.Add(device); cards.Add(block);
    cards.OfType<CardDeviceGroup>().Single(g => K(g.Id!) == stock.GroupKey).Devices!.Add(K(device.Id));
}
var originals = cards.OfType<CardBlock>().Where(c => c.Id!.StartsWith("block_crate_reborn_flag_")).ToDictionary(c => c.Id!, c => c.BlockId);
var untouched = cards.Where(c => c.Id!.StartsWith("device_block_crate_reborn_hero_") || c.Id == "device_block_crate_reborn_christmas")
    .ToDictionary(c => c.Id!, c => JsonSerializer.Serialize(c, JsonHelper.DefaultSerializerSettings));
var server = (ServerCatalogue)Databases.Catalogue;
server.Replicate(cards);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
foreach (var code in RetiredCountryCrates.Countries)
{
    var key = K("device_block_crate_reborn_flag_" + code);
    Check(!PlayerInventory.Owns(42, key), "retired not owned " + code);
    PlayerInventory.Set(42, ["device_block_crate_reborn_flag_" + code]);
    Check(!PlayerInventory.Owns(42, key), "grant cannot revive retired " + code);
    Check(!PlayerInventory.Build(42, PlayerRole.Core).Any(i => i.Item == key), "inventory " + code);
    Check(!cards.OfType<CardDeviceGroup>().Any(g => g.Devices!.Contains(key)), "picker " + code);
    var block = K("block_crate_reborn_flag_" + code).GetCard<CardBlock>()!;
    Check(block.BlockId == originals[block.Id!] && server.GetBlockCard(block.BlockId) == block, "legacy numeric ID " + code);
    Check(JsonSerializer.Serialize(block.Visual) == JsonSerializer.Serialize(stockBlock.Visual), "legacy stock visual " + code);
    foreach (var hero in CatalogueHelper.GetHeroes())
    {
        if (!LoadoutRules.CanUseDevice(hero, RetiredCountryCrates.Stock)) continue;
        var loadout = CatalogueHelper.GetDefaultDevices(hero)!;
        loadout[2] = key;
        LoadoutRules.Sanitize(hero, loadout, [], 42);
        Check(loadout[2] == RetiredCountryCrates.Stock, "saved loadout fallback " + code);
        Check(!LoadoutRules.CanUseDevice(hero, key), "direct equip blocked " + code);
    }
    // Incremental catalogue updates must not revive a skin or a shop entry.
    var incoming = Clone(stock); incoming.Id = "device_block_crate_reborn_flag_" + code;
    incoming.Icon = "block_skin_flag_" + code;
    server.UpdateCard(incoming);
    Check(incoming.Icon == stock.Icon && incoming.Scope == ScopeType.Private, "incremental device " + code);
    var shop = new CardShopItem { Items = [key], AvailableInShop = true };
    RetiredCountryCrates.Apply(shop, cards);
    Check(!shop.AvailableInShop, "shop blocked " + code);
}
foreach (var (id, json) in untouched)
{
    var after = JsonSerializer.Serialize(cards.Single(c => c.Id == id), JsonHelper.DefaultSerializerSettings);
    Check(after == json, "unrelated crate " + id);
}
Console.WriteLine($"COUNTRY_CRATES PASS {checks} checks, all 13 countries; {untouched.Count} unrelated variants unchanged");
