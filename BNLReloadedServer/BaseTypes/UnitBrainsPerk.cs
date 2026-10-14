using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    // Brains class perks that react to healing an ally. Called only with actual restored health, once per source share.
    public void ApplyBrainsRecovery(float restored, EffectSource? source, Unit? healer)
    {
        if (!float.IsFinite(restored) || restored <= 0 || PlayerId is null || IsDead ||
            healer is not { PlayerId: not null, IsDead: false, IsActive: true } || healer.Id == Id ||
            Team == TeamType.Neutral || Team != healer.Team || FreeForAllPlayer || healer.FreeForAllPlayer)
            return;

        // Hero tools, abilities and their owned healing entities qualify. Health stations do not.
        if (source is not UnitSource { Unit: var origin } ||
            (origin.PlayerId != healer.PlayerId && origin.OwnerPlayerId != healer.PlayerId) ||
            origin.UnitCard is not { } originCard ||
            originCard.Id?.Contains("heal_station", StringComparison.Ordinal) == true ||
            origin.UnitCard.Labels?.Contains(UnitLabel.HealthSupply) == true ||
            origin.UnitCard.MinimapType == UnitMinimapType.HealthSupply)
            return;

        switch (healer.ActiveClassPerk)
        {
            // Brains I: Shared Recovery. AddHealth applies the normal cap/healing modifiers without invoking another heal effect.
            case ClassPerkCatalogue.BrainsId:
                var gained = healer.AddHealth(restored * ClassPerkCatalogue.Balance(ClassPerkCatalogue.BrainsId).HealingReturnPercent / 100f);
                if (gained > 0) healer.SendHealAttribution(healer, gained, healer.Key);
                break;

            // Brains II: the healed ally receives the CDB effect (ammo regeneration); a timed card refreshes on each heal.
            case ClassPerkCatalogue.Brains2Id:
                if (ClassPerkCatalogue.Balance(ClassPerkCatalogue.Brains2Id).HealedAllyEffect is not { } effectId ||
                    Databases.Catalogue.GetCard<CardEffect>(effectId) == null)
                    return;
                AddEffect(new ConstEffectInfo(Catalogue.Key(effectId)), healer.Team, healer.GetSelfSource(healer.CreateImpactData()));
                break;
        }
    }
}
