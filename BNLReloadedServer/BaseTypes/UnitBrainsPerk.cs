using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    // Brains class perks react to this hero's healing reaching an ally: a living, active Brains player healing
    // another player of the same team (never in FFA) with a hero tool, ability or owned healing entity.
    // Health stations do not qualify.
    private bool IsBrainsAllyHeal(EffectSource? source, Unit? healer)
    {
        if (PlayerId is null || IsDead ||
            healer is not { PlayerId: not null, IsDead: false, IsActive: true } || healer.Id == Id ||
            Team == TeamType.Neutral || Team != healer.Team || FreeForAllPlayer || healer.FreeForAllPlayer)
            return false;

        return source is UnitSource { Unit: var origin } &&
               (origin.PlayerId == healer.PlayerId || origin.OwnerPlayerId == healer.PlayerId) &&
               origin.UnitCard is { } originCard &&
               originCard.Id?.Contains("heal_station", StringComparison.Ordinal) != true &&
               originCard.Labels?.Contains(UnitLabel.HealthSupply) != true &&
               originCard.MinimapType != UnitMinimapType.HealthSupply;
    }

    // Brains I: Shared Recovery. Called only with actual restored health, once per source share.
    public void ApplyBrainsRecovery(float restored, EffectSource? source, Unit? healer)
    {
        if (!float.IsFinite(restored) || restored <= 0 || healer?.ActiveClassPerk != ClassPerkCatalogue.BrainsId ||
            !IsBrainsAllyHeal(source, healer))
            return;

        // AddHealth applies the normal cap/healing modifiers without invoking another heal effect.
        var gained = healer.AddHealth(restored * ClassPerkCatalogue.Balance(ClassPerkCatalogue.BrainsId).HealingReturnPercent / 100f);
        if (gained > 0) healer.SendHealAttribution(healer, gained, healer.Key);
    }

    // Brains II: Supply Line. Called whenever this hero's healing lands on the ally, including at full health,
    // so allies who need ammo rather than health still benefit. The timed CDB effect refreshes on each heal.
    public void ApplySupplyLine(EffectSource? source, Unit? healer)
    {
        if (healer?.ActiveClassPerk != ClassPerkCatalogue.Brains2Id || !IsBrainsAllyHeal(source, healer) ||
            ClassPerkCatalogue.Balance(ClassPerkCatalogue.Brains2Id).HealedAllyEffect is not { } effectId ||
            Databases.Catalogue.GetCard<CardEffect>(effectId) == null)
            return;
        AddEffect(new ConstEffectInfo(Catalogue.Key(effectId)), healer.Team, healer.GetSelfSource(healer.CreateImpactData()));
    }
}
