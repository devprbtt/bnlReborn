using System.Collections.Concurrent;
using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>A shop item a player has seen in the shop, so it no longer shows as new to them.</summary>
[Table("PlayerSeenShopItems")]
public sealed class SeenShopItemRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed(Name = "UX_PlayerSeenShopItems", Order = 1, Unique = true), Column("player_id")] public uint PlayerId { get; set; }
    [Indexed(Name = "UX_PlayerSeenShopItems", Order = 2, Unique = true), Column("shop_item")] public string ShopItem { get; set; } = string.Empty;
    [Column("seen_at")] public DateTimeOffset SeenAt { get; set; }
}

/// <summary>
/// The player's NewItems: shop items new in the shop (is_new, on sale, priced, selling private cards: the Reborn
/// Catalogue's weapon and character skins) that the player neither owns outright nor has seen. They are sent as the
/// shop items' keys (CardCategory.ShopItem), which no stock new-items counter counts, so only the Catalogue path to the
/// item shows NEW. MessageMarkItemAsShown, sent when the player opens an item, records it as seen.
/// </summary>
public static class ShopNewItems
{
    private static readonly ConcurrentDictionary<uint, HashSet<string>> Seen = new();

    public static bool IsNewInShop(CardShopItem item) =>
        item.IsNew && item.AvailableInShop && (item.PriceVirtual is > 0 || item.PriceReal is > 0) && ShopPurchases.Grants(item).Count > 0;

    /// <summary>The new shop items a player has neither seen nor fully owns, as shop item keys.</summary>
    public static List<Key> For(uint playerId)
    {
        Seen.TryGetValue(playerId, out var seen);
        var result = new List<Key>();
        foreach (var item in Databases.Catalogue.All.OfType<CardShopItem>())
        {
            if (item.Id is not { } id || !IsNewInShop(item)) continue;
            if (seen != null && seen.Contains(id)) continue;
            if (ShopPurchases.Grants(item).All(card => PlayerInventory.Owns(playerId, card.Key))) continue;
            result.Add(item.Key);
        }
        return result;
    }

    /// <summary>
    /// The new shop item a shown key names: the shop item itself, or one selling the card (the stock hero skin cards
    /// report the skin). Null when the key names nothing new in the shop.
    /// </summary>
    public static CardShopItem? Resolve(Key key)
    {
        if (key.GetCard<Card>() is CardShopItem direct) return IsNewInShop(direct) ? direct : null;
        return Databases.Catalogue.All.OfType<CardShopItem>().FirstOrDefault(item => IsNewInShop(item) && item.Items != null && item.Items.Contains(key));
    }

    /// <summary>Records a shop item as seen; false when it already was.</summary>
    public static bool Remember(uint playerId, string shopItemId)
    {
        var seen = Seen.GetOrAdd(playerId, _ => new HashSet<string>(StringComparer.Ordinal));
        lock (seen) return seen.Add(shopItemId);
    }

    public static void Load(IEnumerable<SeenShopItemRecord> records)
    {
        Seen.Clear();
        foreach (var group in records.GroupBy(r => r.PlayerId))
            Seen[group.Key] = new HashSet<string>(group.Select(r => r.ShopItem), StringComparer.Ordinal);
    }
}
