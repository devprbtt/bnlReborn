using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public static class RankedSnakeDraft
{
    public record Player(uint Id, TeamType Team, double Mmr);

    // One player at a time: A, BB, AA, BB, AA, B for five-a-side.
    // Lower average MMR gets first pick; equal averages alternate by a match-level coin toss.
    public static uint[] Order(IEnumerable<Player> players, bool teamOneWinsTie)
    {
        var teams = new[] { TeamType.Team1, TeamType.Team2 }.Select(team => players
            .Where(p => p.Team == team).OrderByDescending(p => p.Mmr).ThenBy(p => p.Id).ToArray()).ToArray();
        var averages = teams.Select(t => t.Length == 0 ? 0 : t.Average(p => p.Mmr)).ToArray();
        int side = averages[0] < averages[1] || averages[0] == averages[1] && teamOneWinsTie ? 0 : 1;
        var queues = teams.Select(t => new Queue<Player>(t)).ToArray();
        var result = new List<uint>();
        int count = 1;
        while (queues.Any(q => q.Count > 0))
        {
            for (int i = 0; i < count && queues[side].Count > 0; i++) result.Add(queues[side].Dequeue().Id);
            count = 2;
            side = 1 - side;
        }
        return result.ToArray();
    }
}
