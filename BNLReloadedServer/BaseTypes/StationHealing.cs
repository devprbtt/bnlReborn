namespace BNLReloadedServer.BaseTypes;

public static class StationHealing
{
    public const string FastRestockEffect = "effect_perk_fast_restock_pos";

    public static void ApplyDescription(Card card)
    {
        var description = card switch
        {
            CardPerk { Id: "perk_def_fast_restock" } p => p.Description,
            CardShopItem { Id: "shop_item_perk_def_fast_restock" } shop => shop.Description,
            _ => null
        };
        const string oldScope = "healing and ammo regeneration from all sources";
        if (description?.Text?.Contains(oldScope, StringComparison.Ordinal) != true) return;
        description.Text = description.Text.Replace(oldScope,
            "healing from health and ammo stations and ammo regeneration from all sources", StringComparison.Ordinal);
        description.Data = []; // Fall back to the corrected text instead of stale translations.
    }

    public static bool IsStation(EffectSource? source) => source is UnitSource { Unit.UnitCard: { } card } &&
        (card.Labels?.Contains(UnitLabel.HealthSupply) == true ||
         card.Labels?.Contains(UnitLabel.AmmmoSupply) == true ||
         card.MinimapType is UnitMinimapType.HealthSupply or UnitMinimapType.AmmoSupply ||
         // Medkits emitted by health stations retain their station origin in the pickup card.
         card.Id is "unit_pickup_medikit_heal_station" or "unit_pickup_medikit_heal_station_enemy");
}
