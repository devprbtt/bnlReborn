namespace BNLReloadedServer.ServerTypes;

public static class MatchmakerBalanceSafety
{
    public static bool IsCompleteCandidate(IEnumerable<int> groupSizes, int requiredPlayers) =>
        requiredPlayers > 0 && groupSizes.Sum() == requiredPlayers;

    public static bool MoveReducesImbalance(int sourcePlayers, int destinationPlayers, int groupSize)
    {
        if (groupSize <= 0 || sourcePlayers <= destinationPlayers) return false;

        var before = sourcePlayers - destinationPlayers;
        var after = Math.Abs((sourcePlayers - groupSize) - (destinationPlayers + groupSize));
        return after < before;
    }
}
