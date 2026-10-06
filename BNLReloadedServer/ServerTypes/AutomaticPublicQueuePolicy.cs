using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public enum AutomaticPublicQueueAction
{
    Wait,
    StartGracePeriod,
    ResetGracePeriod,
    StartCasual,
    StartLargeCasual
}

public static class AutomaticPublicQueuePolicy
{
    // The casual pool pops 4v4 after the grace period, or 5v5 casual as soon as ten are waiting.
    // Ranked has its own queue and never comes from this pool.
    public const int CasualPlayerCount = 8;
    public const int LargeCasualPlayerCount = 10;
    public const int CasualPlayersPerTeam = CasualPlayerCount / 2;
    public const int LargeCasualPlayersPerTeam = LargeCasualPlayerCount / 2;
    public const double DefaultGracePeriodSeconds = 30;

    public static TimeSpan GracePeriod { get; } = TimeSpan.FromSeconds(ReadGracePeriodSeconds(
        Environment.GetEnvironmentVariable("BNL_PUBLIC_QUEUE_GRACE_SECONDS")));

    public static AutomaticPublicQueueAction Decide(int playerCount, DateTimeOffset? graceDeadline,
        DateTimeOffset now)
    {
        if (playerCount >= LargeCasualPlayerCount) return AutomaticPublicQueueAction.StartLargeCasual;
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

    /// <summary>Only Casual requests share the automatic pool; Ranked queues on its own and pops at its card's size.</summary>
    public static bool IsAutomaticQueue(CardGameMode? gameMode) => gameMode?.Ranking is GameRankingType.Friendly;

    public static int PlayersPerTeam(AutomaticPublicQueueAction action) => action switch
    {
        AutomaticPublicQueueAction.StartCasual => CasualPlayersPerTeam,
        AutomaticPublicQueueAction.StartLargeCasual => LargeCasualPlayersPerTeam,
        _ => 0
    };

    public static double ReadGracePeriodSeconds(string? configuredValue) =>
        int.TryParse(configuredValue, out var seconds) && seconds is >= 1 and <= 300
            ? seconds
            : DefaultGracePeriodSeconds;
}
