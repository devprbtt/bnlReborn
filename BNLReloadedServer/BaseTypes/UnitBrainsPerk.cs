using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    // Brains I: Shared Recovery. Called only with actual restored health, once per source share.
    public void ApplyBrainsRecovery(float restored, EffectSource? source, Unit? healer)
    {
        if (!float.IsFinite(restored) || restored <= 0 || PlayerId is null || IsDead ||
            healer is not { PlayerId: not null, IsDead: false, IsActive: true } || healer.Id == Id ||
            Team == TeamType.Neutral || Team != healer.Team || FreeForAllPlayer || healer.FreeForAllPlayer ||
            healer.UnitCard?.Data is not UnitDataPlayer data ||
            data.Class.GetCard<CardHeroClass>()?.Type != HeroClassType.Brains)
            return;

        // Hero tools, abilities and their owned healing entities qualify. Health stations do not.
        if (source is not UnitSource { Unit: var origin } ||
            (origin.PlayerId != healer.PlayerId && origin.OwnerPlayerId != healer.PlayerId) ||
            origin.UnitCard is not { } originCard ||
            originCard.Id?.Contains("heal_station", StringComparison.Ordinal) == true ||
            origin.UnitCard.Labels?.Contains(UnitLabel.HealthSupply) == true ||
            origin.UnitCard.MinimapType == UnitMinimapType.HealthSupply)
            return;

        // AddHealth applies the normal cap/healing modifiers without invoking another heal effect.
        var gained = healer.AddHealth(restored * Database.ClassPerkCatalogue.Balance(Database.ClassPerkCatalogue.BrainsId).HealingReturnPercent / 100f);
        if (gained > 0) healer.SendHealAttribution(healer, gained, healer.Key);
    }
}
