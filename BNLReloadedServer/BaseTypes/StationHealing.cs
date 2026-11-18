namespace BNLReloadedServer.BaseTypes;

public static class StationHealing
{
    public const string FastRestockEffect = "effect_perk_fast_restock_pos";

    public static bool IsStation(EffectSource? source) => source is UnitSource { Unit.UnitCard: { } card } &&
        (card.Labels?.Contains(UnitLabel.HealthSupply) == true ||
         card.Labels?.Contains(UnitLabel.AmmmoSupply) == true ||
         card.MinimapType is UnitMinimapType.HealthSupply or UnitMinimapType.AmmoSupply ||
         // Medkits emitted by health stations retain their station origin in the pickup card.
         card.Id is "unit_pickup_medikit_heal_station" or "unit_pickup_medikit_heal_station_enemy");
}
