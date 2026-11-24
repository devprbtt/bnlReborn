using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

internal static class VanderBallLightning
{
    internal static bool IsBurst(ToolLogic? tool) => tool is { ToolIndex: 1 } &&
        (tool.Gear.Key == new Key("gear_magnus_ball_lightning") || tool.Gear.Key == new Key("gear_magnus_ball_lightning_ball_to_the_wall"));

    internal static bool TryCost(ToolLogic tool, List<ShotData> shots, out int cost)
    {
        cost = shots.Count;
        int available = VanderBallBurstMath.Count(tool.GetAmmoData()?.Mag ?? 0, tool.Unit.AmmoRate(1));
        return cost > 0 && cost <= available && shots.All(s => s.ShotId.HasValue) && shots.Select(s => s.ShotId).Distinct().Count() == cost;
    }
}
