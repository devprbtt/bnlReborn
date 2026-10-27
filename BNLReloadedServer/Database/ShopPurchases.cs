using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

public enum PurchaseResult { Purchased, UnknownPlayer, UnknownItem, NotForSale, AlreadyOwned, InsufficientFunds }

/// <summary>One completed shop purchase, kept for support and refunds.</summary>
[Table("ShopPurchases")]
public sealed class ShopPurchaseRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed, Column("player_id")] public uint PlayerId { get; set; }
    [Column("shop_item")] public string ShopItem { get; set; } = string.Empty;
    [Column("currency")] public CurrencyType Currency { get; set; }
    [Column("price")] public long Price { get; set; }
    // Card ids granted by this purchase, comma separated (items already owned are not repeated).
    [Column("items")] public string Items { get; set; } = string.Empty;
    [Column("purchased_at")] public DateTimeOffset PurchasedAt { get; set; }
}

/// <summary>
/// A shop item card (CDB) sells private cards: its price is Gold (PriceVirtual) or Platinum (PriceReal), less an
/// active promotion, and buying grants every listed private card the player lacks through InventoryGrants.
/// </summary>
public static class ShopPurchases
{
    public static long? Price(CardShopItem item, bool real, DateTime utcNow)
    {
        var basePrice = real ? item.PriceReal : item.PriceVirtual;
        if (basePrice is not > 0) return null;
        var discount = 0f;
        if (item.Promotion is { } promo && (promo.From == null || promo.From <= utcNow) && (promo.To == null || utcNow <= promo.To))
            discount = Math.Clamp(real ? promo.RealDiscount : promo.VirtualDiscount, 0f, 1f);
        return (long)Math.Ceiling(basePrice.Value * (1f - discount));
    }

    /// <summary>The private cards this item grants; public cards are owned by everyone and never sold.</summary>
    public static List<Card> Grants(CardShopItem item) =>
        (item.Items ?? []).Select(key => key.GetCard<Card>()).OfType<Card>().Where(PlayerInventory.RequiresGrant).ToList();

    public static string Column(bool real) => real ? "platinum" : "gold";
}
