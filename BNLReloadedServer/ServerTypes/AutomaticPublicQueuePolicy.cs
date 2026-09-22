using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public enum AutomaticPublicQueueAction
{
    Wait,
    StartGracePeriod,
    ResetGracePeriod,
    StartCasual,
    StartRanked
}

public static class AutomaticPublicQueuePolicy
{
    public const int CasualPlayerCount = 8;
    public const int RankedPlayerCount = 10;
    public const double DefaultGracePeriodSeconds = 30;

    public static TimeSpan GracePeriod { get; } = TimeSpan.FromSeconds(ReadGracePeriodSeconds(
        Environment.GetEnvironmentVariable("BNL_PUBLIC_QUEUE_GRACE_SECONDS")));

    public static AutomaticPublicQueueAction Decide(int playerCount, DateTimeOffset? graceDeadline,
        DateTimeOffset now)
    {
        if (playerCount >= RankedPlayerCount) return AutomaticPublicQueueAction.StartRanked;
        if (playerCount < CasualPlayerCount)
            return graceDeadline.HasValue
                ? AutomaticPublicQueueAction.ResetGracePeriod
                : AutomaticPublicQueueAction.Wait;
        if (!graceDeadline.HasValue) return AutomaticPublicQueueAction.StartGracePeriod;
        return now >= graceDeadline.Value
            ? AutomaticPublicQueueAction.StartCasual
            : AutomaticPublicQueueAction.Wait;
    }

    public static bool IsPublicMode(CardGameMode? gameMode) =>
        gameMode?.Ranking is GameRankingType.Friendly or GameRankingType.Ranked;

    public static double ReadGracePeriodSeconds(string? configuredValue) =>
        int.TryParse(configuredValue, out var seconds) && seconds is >= 1 and <= 300
            ? seconds
            : DefaultGracePeriodSeconds;
}
