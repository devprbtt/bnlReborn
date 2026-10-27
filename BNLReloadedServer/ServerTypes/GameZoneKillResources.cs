using BNLReloadedServer.BaseTypes;
namespace BNLReloadedServer.ServerTypes;
public partial class GameZone
{
    private void AwardKillResources(Unit target, Unit? killerPlayer, bool killerIsOpponent, bool mining)
    {
        var targetUnitCard = target.UnitCard;
        var targetTeam = target.Team;
        var isObjective = targetUnitCard?.IsObjective == true;
        if (targetUnitCard?.Health?.KillReward is { } reward && killerPlayer is not null && (!reward.Mining || mining) &&
            (!target.IsTestDummy || killerIsOpponent))
        {
            if (reward.TeamReward is not null && (target.PlayerId == null || killerIsOpponent))
            {
                foreach (var player in _playerUnits.Values.Where(p => p.Team == killerPlayer.Team && p != killerPlayer))
                {
                    player.AddResource(reward.TeamReward.Value,
                        (target.PlayerId != null || target.IsTestDummy) ? ResourceType.TeamKill :
                        isObjective ? ResourceType.Objective : ResourceType.General);
                }
            }
            if (reward.EnemyReward is not null && (target.PlayerId == null
                    ? killerPlayer.Team != targetTeam
                    : killerIsOpponent))
            {
                killerPlayer.AddResource(reward.EnemyReward.Value,
                    (target.PlayerId != null || target.IsTestDummy) ? ResourceType.Kill :
                    isObjective ? ResourceType.Objective :
                    mining ? ResourceType.Mining : ResourceType.General);

                if (targetUnitCard.Health.Health?.HealthType is HealthType.World)
                {
                    killerPlayer.DestroyedBlock(targetUnitCard.DeviceType, reward.EnemyReward.Value);
                }
            }
            else if (reward.PlayerReward is not null)
            {
                killerPlayer.AddResource(reward.PlayerReward.Value,
                    (target.PlayerId != null || target.IsTestDummy) ? ResourceType.Kill :
                    isObjective ? ResourceType.Objective :
                    mining ? ResourceType.Mining : ResourceType.General);

                if (targetUnitCard.Health.Health?.HealthType is HealthType.World)
                {
                    killerPlayer.DestroyedBlock(targetUnitCard.DeviceType, reward.PlayerReward.Value);
                }
            }
        }
    }
}
