using BNLReloadedServer.BaseTypes;
namespace BNLReloadedServer.Database;

public static class DeviceSkinPresentation
{
    public static readonly Key Retired = Catalogue.Key("device_magnus_tesla_coil_disco");
    public static readonly Key StockTesla = Catalogue.Key("device_magnus_tesla_coil");
    public static bool IsRetired(Key key) => key == Retired;

    public static void Apply(Card card, IEnumerable<Card> source)
    {
        var cards = source.Where(c => c.Id != null).ToList();
        if (card is CardDeviceGroup group) group.Devices?.RemoveAll(IsRetired);
        if (card is CardUnit { Data: UnitDataPlayer hero }) hero.SpecialDevices?.RemoveAll(IsRetired);
        if (card is CardShopItem shop && shop.Items?.Any(IsRetired) == true) shop.AvailableInShop = false;
        if (card.Id == "device_magnus_tesla_coil_disco") card.Scope = ScopeType.Private;

        // Device groups list the standard device first. Names of deployed cosmetic
        // units/blocks follow that device's matching level.
        if (card is CardDevice skin)
        {
            var groupCard = cards.OfType<CardDeviceGroup>().FirstOrDefault(g => Catalogue.Key(g.Id!) == skin.GroupKey);
            if (groupCard?.Devices is { Count: > 0 })
            {
                var stock = cards.OfType<CardDevice>().FirstOrDefault(d => Catalogue.Key(d.Id!) == groupCard.Devices[0]);
                if (stock != null) skin.Name = stock.Name;
            }
            return;
        }
        if (card.Id == null || (card is not CardUnit && card is not CardBlock)) return;
        var key = Catalogue.Key(card.Id!);
        foreach (var device in cards.OfType<CardDevice>())
        {
            int level = device.DeviceLevels?.IndexOf(key) ?? -1;
            if (level < 0) continue;
            var family = cards.OfType<CardDeviceGroup>().FirstOrDefault(g => Catalogue.Key(g.Id!) == device.GroupKey);
            if (family?.Devices is not { Count: > 0 }) continue;
            var standard = cards.OfType<CardDevice>().FirstOrDefault(d => Catalogue.Key(d.Id!) == family.Devices[0]);
            if (standard?.DeviceLevels is not { Count: > 0 }) continue;
            var standardKey = standard.DeviceLevels[Math.Min(level, standard.DeviceLevels.Count - 1)];
            if (key == standardKey) return;
            var original = cards.FirstOrDefault(c => Catalogue.Key(c.Id!) == standardKey);
            if (card is CardUnit unit && original is CardUnit baseUnit) unit.Name = baseUnit.Name;
            if (card is CardBlock block && original is CardBlock baseBlock && block.Visual != null && baseBlock.Visual != null)
                block.Visual.Name = baseBlock.Visual.Name;
            return;
        }
    }
}
