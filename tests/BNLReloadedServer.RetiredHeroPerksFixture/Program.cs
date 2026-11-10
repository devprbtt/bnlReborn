using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

var raw = File.ReadAllText(args[0]);
List<Card> Read() => JsonSerializer.Deserialize<List<Card>>(raw, JsonHelper.DefaultSerializerSettings)!;
var server = (ServerCatalogue)Databases.Catalogue;
server.Replicate(Read());
int count = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; }
foreach (var retiredId in new[] { RetiredPerks.ShinobiTactics, RetiredPerks.WalkItOff })
{
    var key = Catalogue.Key(retiredId);
    var hero = Catalogue.Key("unit_hero_ninja");
    void Verify()
    {
        Check(key.GetCard<CardPerk>() is { Scope: ScopeType.Private, PerkMods.Count: 0 }, "retained card has no gameplay modifiers");
        Check(!PlayerInventory.Owns(42, key), "old grants cannot revive perk");
        Check(!LoadoutRules.CanUsePerk(hero, key), "anonymous equip blocked");
        Check(!LoadoutRules.CanUsePerk(hero, key, 42), "owned equip blocked");
        Check(!PlayerInventory.Build(42, PlayerRole.Core).Any(i => i.Item == key), "inventory hidden");
        Check(!server.All.OfType<CardGlobalLogic>().Any(g => g.Perks?.Heroes?.Values.Any(p => p.Contains(key)) == true), "hero selection hidden");
        Check(!server.All.OfType<CardGlobalLogic>().Any(g => g.Perks?.Defensive?.Contains(key) == true || g.Perks?.Offensive?.Contains(key) == true), "general perk selection hidden");
        Check(!Catalogue.Key("shop_item_" + retiredId).GetCard<CardShopItem>()!.AvailableInShop, "shop hidden");
    }
    PlayerInventory.Set(42, [retiredId]);
    Verify();
    var other = Catalogue.Key("perk_hero_ninja_mystical_teleport");
    var loadout = new LobbyLoadout { HeroKey = hero, SkinKey = PlayerInventory.SafeSkin(42, hero, Key.None), Devices = CatalogueHelper.GetDefaultDevices(hero), Perks = [key, other] };
    var player = new PlayerData { PlayerId = 42, HeroLoadouts = { [hero] = loadout } };
    Check(player.SanitizeAgainstCatalogue(), "saved loadout migrated");
    Check(loadout.Perks.SequenceEqual(new[] { other }), "only retired perk removed");
    Check(!player.SanitizeAgainstCatalogue(), "migration idempotent");
    var perks = new List<Key> { key, other };
    LoadoutRules.Sanitize(hero, null, perks);
    Check(perks.SequenceEqual(new[] { other }), "match loadout sanitized");
    foreach (var incoming in Read().Where(c => c.Id == retiredId || c is CardGlobalLogic || c.Id == "shop_item_" + retiredId))
        server.UpdateCard(incoming);
    Verify();
}
foreach (var original in Read().OfType<CardPerk>().Where(p => !RetiredPerks.IsRetired(Catalogue.Key(p.Id!))))
    Check(JsonSerializer.Serialize(original.PerkMods, JsonHelper.DefaultSerializerSettings) == JsonSerializer.Serialize(Catalogue.Key(original.Id!).GetCard<CardPerk>()!.PerkMods, JsonHelper.DefaultSerializerSettings), "other perk modifiers unchanged");
Console.WriteLine($"RETIRED_PERKS PASS {count} checks");
