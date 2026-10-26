using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private void ScoreLampPickup(Unit pickup, Unit? recipient)
    {
        if (!CanScore || recipient?.PlayerId == null || pickup.ScoreLampOwner is not { } owner ||
            owner == recipient.PlayerId || GetPlayerFromPlayerId(owner) is not { } builder ||
            builder.Team != recipient.Team || builder.Team == TeamType.Neutral) return;
        AwardAction(builder, "Lamp pickup", 1, pickup.GetMidpoint(), pickup.Id);
        pickup.ScoreLampOwner = null;
    }
    private void ScoreHeroDeviceDestroyed(Unit target, Unit? player, Unit? attacker, ImpactData impact)
    {
        if (!CanScore || !target.IsActive || !IsScoreDevice(target.UnitCard) || target.UnitCard?.IsObjective == true ||
            target.PlayerId != null || target.Team == TeamType.Neutral || player == null || !AreOpponents(target, player) ||
            ExcludedDestroyScore(target.UnitCard?.Id)) return;
        player.ReportHeroBoostAction("effect_hero_trondson_heigh_ho_buff", "Demolition boost assist", target.GetMidpoint(), target.Id);
        if (attacker != null && attacker != player)
            attacker.ReportHeroBoostAction("effect_hero_trondson_heigh_ho_torpedo_buff", "Demolition boost assist", target.GetMidpoint(), target.Id);
    }
}
