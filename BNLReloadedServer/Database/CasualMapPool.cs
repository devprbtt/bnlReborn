using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class CasualMapPool
{
    // Derive both rotations from the configured Casual pool so subsequent map
    // additions/removals and vote weights still apply to both sizes.
    public static IEnumerable<CardMap> Filter(IEnumerable<CardMap> maps, GameRankingType ranking,
        int playersPerTeam, Func<CardMap, int> cubeCount) =>
        ranking == GameRankingType.Friendly && playersPerTeam == 5
            ? maps.Where(map => cubeCount(map) != 1)
            : maps;

    public static int CubeCount(CardMap map) =>
        map.Data?.Units is { Count: > 0 }
            ? CountCubes(map.Data, key => key.GetCard<CardUnit>())
            : Databases.MapDatabase.GetCubeCount(map.Key);

    public static int CountCubes(MapData? data, Func<Key, CardUnit?> unitCard)
    {
        int team1 = 0, team2 = 0;
        foreach (var unit in data?.Units ?? [])
        {
            var card = unitCard(unit.UnitKey);
            if (card == null || !(card.IsBase || card.IsLine1 || card.IsLine2 || card.IsLine3)) continue;
            if (unit.Team == TeamType.Team1) team1++;
            if (unit.Team == TeamType.Team2) team2++;
        }
        return Math.Max(team1, team2);
    }
}
