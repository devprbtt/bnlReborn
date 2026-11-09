using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// Keep legacy cards/block IDs readable, but never offer or equip national crate skins.
public static class RetiredCountryCrates
{
    public static readonly string[] Countries = ["br", "us", "gb", "de", "pl", "fr", "ua", "ru", "ro", "mx", "ca", "fo", "ge"];
    private static readonly HashSet<Key> Devices = Countries.Select(c => Catalogue.Key("device_block_crate_reborn_flag_" + c)).ToHashSet();
    public static readonly Key Stock = Catalogue.Key("device_block_crate");
    public static bool IsRetired(Key key) => Devices.Contains(key);

    public static void Apply(Card card, IEnumerable<Card> cards)
    {
        if (card is CardDeviceGroup group) group.Devices?.RemoveAll(IsRetired);
        if (card is CardShopItem shop && shop.Items?.Any(IsRetired) == true)
            shop.AvailableInShop = false;
        bool device = card is CardDevice && card.Id?.StartsWith("device_block_crate_reborn_flag_", StringComparison.Ordinal) == true;
        bool block = card is CardBlock && card.Id?.StartsWith("block_crate_reborn_flag_", StringComparison.Ordinal) == true;
        if (!device && !block) return;
        var stock = cards.OfType<CardDevice>().FirstOrDefault(c => c.Id == "device_block_crate")
            ?? throw new InvalidOperationException("Retired crates require the stock crate device");
        if (card is CardDevice retired)
        {
            retired.Scope = ScopeType.Private;
            retired.Name = stock.Name;
            retired.Icon = stock.Icon;
            retired.DeviceLevels = stock.DeviceLevels?.ToList();
        }
        else if (card is CardBlock oldBlock)
        {
            var stockBlock = cards.OfType<CardBlock>().First(c => Catalogue.Key(c.Id!) == stock.DeviceLevels![0]);
            oldBlock.Visual = stockBlock.Visual;
        }
    }
}
