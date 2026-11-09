using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

var raw = File.ReadAllText(args[0]);
List<Card> Read() => JsonSerializer.Deserialize<List<Card>>(raw, JsonHelper.DefaultSerializerSettings)!;
var cards = Read();
var server = (ServerCatalogue)Databases.Catalogue;
server.Replicate(cards);
int count = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; }
foreach (var id in RetiredHeroSkins.Ids)
{
    var key = Catalogue.Key(id);
    var skin = key.GetCard<CardSkin>()!;
    Check(skin != null, "legacy skin card retained");
    var hero = skin!.HeroKey;
    PlayerInventory.Set(42, [id]);
    Check(!PlayerInventory.Owns(42, key), "old grants cannot revive skin");
    Check(!PlayerInventory.CanEquipSkin(42, hero, key), "direct equip blocked");
    Check(!PlayerInventory.Build(42, PlayerRole.Core).Any(i => i.Item == key), "inventory hidden");
    Check(!((UnitDataPlayer)hero.GetCard<CardUnit>()!.Data!).Skins!.Contains(key), "hero picker hidden");
    Check(!Catalogue.Key("shop_" + id).GetCard<CardShopItem>()!.AvailableInShop, "shop hidden");
    var replacement = PlayerInventory.SafeSkin(42, hero, key);
    Check(replacement != key && PlayerInventory.CanEquipSkin(42, hero, replacement), "safe fallback");
    var loadout = new LobbyLoadout { HeroKey = hero, SkinKey = key,
        Devices = CatalogueHelper.GetDefaultDevices(hero), Perks = [] };
    var expectedDevices = loadout.Devices!.ToDictionary();
    var player = new PlayerData { PlayerId = 42, HeroLoadouts = { [hero] = loadout } };
    Check(player.SanitizeAgainstCatalogue(), "login migration detected");
    Check(player.HeroLoadouts.ContainsKey(hero) && loadout.SkinKey == replacement, "login keeps loadout");
    Check(loadout.Devices!.Count == expectedDevices.Count && expectedDevices.All(kv => loadout.Devices[kv.Key] == kv.Value), "devices preserved");
    Check(loadout.Perks!.Count == 0, "perks preserved");
    // Real incremental updates cannot restore the original public skin, hero list or shop listing.
    foreach (var incoming in Read().Where(c => c.Id == id || c.Id == "shop_" + id || Catalogue.Key(c.Id!) == hero))
        server.UpdateCard(incoming);
    Check(!((UnitDataPlayer)hero.GetCard<CardUnit>()!.Data!).Skins!.Contains(key), "incremental hero removal");
    Check(!Catalogue.Key("shop_" + id).GetCard<CardShopItem>()!.AvailableInShop, "incremental shop removal");
    Check(!PlayerInventory.Owns(42, key), "incremental skin still retired");
}
var unrelated = CatalogueHelper.GetCards<CardSkin>(CardCategory.Skin).Where(c => !RetiredHeroSkins.IsRetired(c.Key)).ToList();
foreach (var skin in unrelated)
    Check(PlayerInventory.Owns(77, skin.Key) == !PlayerInventory.RequiresGrant(skin), "unrelated skin ownership unchanged");
Console.WriteLine($"RETIRED_HERO_SKINS PASS {count} checks, four removals, {unrelated.Count} other skins unchanged");
