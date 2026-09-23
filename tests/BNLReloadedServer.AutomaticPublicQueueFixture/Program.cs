using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
var future = now.AddSeconds(10);
var past = now.AddSeconds(-1);

Check(AutomaticPublicQueuePolicy.Decide(7, null, now) == AutomaticPublicQueueAction.Wait,
    "seven players continue waiting without a grace timer");
Check(AutomaticPublicQueuePolicy.Decide(8, null, now) == AutomaticPublicQueueAction.StartGracePeriod,
    "eight players start the grace period");
Check(AutomaticPublicQueuePolicy.Decide(9, future, now) == AutomaticPublicQueueAction.Wait,
    "nine players wait while the grace period remains");
Check(AutomaticPublicQueuePolicy.Decide(10, future, now) == AutomaticPublicQueueAction.StartRanked,
    "ten players start Ranked before grace expiry");
Check(AutomaticPublicQueuePolicy.Decide(8, past, now) == AutomaticPublicQueueAction.StartCasual,
    "eight players start Casual after grace expiry");
Check(AutomaticPublicQueuePolicy.Decide(9, past, now) == AutomaticPublicQueueAction.StartCasual,
    "nine players start an eight-player Casual match after grace expiry");
Check(AutomaticPublicQueuePolicy.Decide(7, future, now) == AutomaticPublicQueueAction.ResetGracePeriod,
    "dropping below eight resets the grace period");
Check(AutomaticPublicQueuePolicy.ReadGracePeriodSeconds(null) == 30 &&
      AutomaticPublicQueuePolicy.ReadGracePeriodSeconds("45") == 45 &&
      AutomaticPublicQueuePolicy.ReadGracePeriodSeconds("0") == 30 &&
      AutomaticPublicQueuePolicy.ReadGracePeriodSeconds("invalid") == 30,
    "grace configuration accepts 1-300 seconds and otherwise uses 30 seconds");
Check(AutomaticPublicQueuePolicy.IsPublicMode(new CardGameMode { Ranking = GameRankingType.Friendly }) &&
      AutomaticPublicQueuePolicy.IsPublicMode(new CardGameMode { Ranking = GameRankingType.Ranked }) &&
      !AutomaticPublicQueuePolicy.IsPublicMode(new CardGameMode { Ranking = GameRankingType.None }),
    "Casual and Ranked are the shared public queue modes");
var queuePlayerJson = JsonSerializer.Serialize(
    new QueuedPlayerSnapshot(1, "Player", 123, false, "RANKED"), JsonHelper.DefaultSerializerSettings);
Check(JsonDocument.Parse(queuePlayerJson).RootElement.GetProperty("mode").GetString() == "RANKED",
    "queue snapshot exposes each player's selected entry mode");
Check(MatchmakerBalanceSafety.IsCompleteCandidate([1, 1, 2, 2, 2], 8),
    "complete squad candidate is accepted for balancing");
Check(!MatchmakerBalanceSafety.IsCompleteCandidate([1, 2, 2, 2, 2], 10),
    "incomplete nine-player candidate is rejected before partition repair");
Check(MatchmakerBalanceSafety.MoveReducesImbalance(6, 4, 1) &&
      !MatchmakerBalanceSafety.MoveReducesImbalance(5, 4, 1),
    "partition repair only makes moves that reduce the team-size difference");

Console.WriteLine($"Automatic public queue fixture passed: {checks} checks.");
