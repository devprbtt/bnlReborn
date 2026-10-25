using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// Ranks are a position on the ladder, not a stored value: a player's tier is derived from where
// their rating falls among everyone who has ever played. The snapshot below holds those ratings in
// descending order, so a rank is a binary search away and nobody's stored tier can go stale when
// other players move past them.
//
// Going inactive takes a player's badge away, but it does not take them off the ladder: the
// percentages are cut from the whole population, so nobody else's tier moves when someone stops
// playing.
public static class LeagueRanker
{
    // Tier numbering is the client's: -1 draws no medal and reads "unranked", 0-2 are the
    // divisioned tiers (division 4 is "I", the top of the tier), 3 is Pro and has only the
    // league_icon_3_0 sprite, so its division must stay 0 or the badge renders blank.
    public const int TierUnranked = -1;
    public const int TierBronze = 0;
    public const int TierSilver = 1;
    public const int TierGold = 2;
    public const int TierPro = 3;

    private const int DivisionsPerTier = 5;
    private const int TopDivision = DivisionsPerTier - 1;

    private const double ProFraction = 0.10;
    private const double GoldFraction = 0.30;
    private const double SilverFraction = 0.60;

    // Who wears a badge: five matches of any mode, then a sixty day window from the fifth-most-
    // recent one.
    private const int EligibilityMatches = 5;
    private const int EligibilityDays = 60;

    // "Top 10%" of eight players is 0.8 players, and one lucky match would mint a Pro. Keep the
    // top tier shut until the ladder is big enough for the percentages to mean anything.
    private const int MinLadderSize = 10;
    private const int MinLadderSizeForPro = 20;

    private static volatile double[] _ratingsDescending = [];

    public static void SetLadder(IEnumerable<double> ladderRatings) =>
        _ratingsDescending = ladderRatings.OrderByDescending(r => r).ToArray();

    // Shown under the badge and on the leaderboard, so both read it from here.
    public static int PointsFor(double ratingMean) => (int)Math.Round(ratingMean * 100);

    // The leaderboard's last column is a plain string the client prints verbatim, so the badge
    // wording has to be rebuilt here rather than read from the catalogue: these are the client's
    // own tier_0..tier_3 entries, and GuiLeagueInfo numbers divisions backwards, 4 being the top.
    private static readonly string[] TierNames = ["BRONZE", "SILVER", "GOLD", "Pro"];
    private static readonly string[] DivisionNumerals = ["V", "IV", "III", "II", "I"];

    // Empty rather than "UNRANKED" for a player without a live rank: the column is captioned
    // "Region" by the bundles, so a word there reads as a value, and a blank reads as nothing.
    public static string Label(League? league)
    {
        if (league == null || league.Tier < 0 || league.Tier >= TierNames.Length) return string.Empty;

        var tier = TierNames[league.Tier];
        if (league.Tier == TierPro)
            return league.Status.HasValue ? $"{tier} ({league.Status.Value})" : tier;

        return league.Division >= 0 && league.Division < DivisionNumerals.Length
            ? $"{tier} {DivisionNumerals[league.Division]}"
            : tier;
    }

    // Who the percentages are cut from: a rating only moves off its default once a match has been
    // played, so accounts that have never played sit outside the ladder entirely.
    public static bool IsOnLadder(PlayerRecord record) =>
        (record.RatingMean != Databases.DefaultMean && record.RatingMean != 25.0) || record.RatingDeviation != Databases.DefaultSd;

    public static bool IsEligible(DateTimeOffset? rankEligibleUntil) =>
        rankEligibleUntil.HasValue && rankEligibleUntil.Value > DateTimeOffset.UtcNow;

    public static DateTimeOffset? EligibleUntil(List<MatchHistoryRecord> matchHistory)
    {
        if (matchHistory.Count < EligibilityMatches) return null;
        var qualifying = matchHistory
            .OrderByDescending(m => m.MatchEndTime)
            .Skip(EligibilityMatches - 1)
            .First();
        return DateTimeOffset.FromUnixTimeMilliseconds((long)qualifying.MatchEndTime).AddDays(EligibilityDays);
    }

    // Monthly seasons (LeagueSeasons) replace the percentile tiers below: Bronze 0, Silver 1, Gold 2, Masters 3 and
    // Champions 4, without divisions, from the ranked-only league rating. Null hides the badge (not yet placed).
    public static League? Derive(PlayerRecord record) => Derive(record.PlayerId, record.LeagueInfo);

    public static League? Derive(uint playerId, byte[]? storedLeague = null)
    {
        if (!LeagueSeasons.Snapshot.TryGetValue(playerId, out var standing)) return null;
        var stored = storedLeague != null ? League.ReadByteRecord(storedLeague) : null;
        return new League
        {
            Tier = (int)standing.Tier,
            Division = 0,
            Points = LeagueRules.Points(standing.Mean),
            JoinedTime = stored?.JoinedTime ?? DateTime.UtcNow,
            LastPlayedTime = stored?.LastPlayedTime ?? DateTime.UtcNow,
            Status = standing.Rank
        };
    }

    public static string SeasonLabel(League? league) =>
        league == null || league.Tier < 0 || league.Tier >= LeagueRules.TierNames.Length ? string.Empty : LeagueRules.TierNames[league.Tier];

    // The percentile ladder that preceded seasons; kept for reference and its tests.
    public static League? DerivePercentile(PlayerRecord record)
    {
        var ladder = _ratingsDescending;
        if (!IsEligible(record.RankEligibleUntil) || ladder.Length == 0) return null;

        var stored = record.LeagueInfo != null ? League.ReadByteRecord(record.LeagueInfo) : null;
        var league = new League
        {
            Tier = TierUnranked,
            Division = 0,
            Points = PointsFor(record.RatingMean),
            JoinedTime = stored?.JoinedTime ?? DateTime.UtcNow,
            LastPlayedTime = stored?.LastPlayedTime ?? DateTime.UtcNow,
            Status = null
        };

        var rank = CountAbove(record.RatingMean, ladder) + 1;
        // Players on identical ratings sit across a span of positions, and handing all of them the
        // top of that span would put a whole ladder of tied players in Pro. They share its middle.
        var tieEnd = ladder.Length - CountBelow(record.RatingMean, ladder);
        var fraction = ((rank + tieEnd) / 2.0 - 1) / ladder.Length;

        if (ladder.Length < MinLadderSize)
        {
            league.Tier = TierBronze;
            league.Division = DivisionIn(fraction, 0.0, 1.0);
        }
        else if (fraction < ProFraction && ladder.Length >= MinLadderSizeForPro)
        {
            league.Tier = TierPro;
            league.Division = 0;
            league.Status = rank;
        }
        else if (fraction < GoldFraction)
        {
            // Below MinLadderSizeForPro the Pro slice folds into Gold instead of vanishing.
            var bandStart = ladder.Length >= MinLadderSizeForPro ? ProFraction : 0.0;
            league.Tier = TierGold;
            league.Division = DivisionIn(fraction, bandStart, GoldFraction);
        }
        else if (fraction < SilverFraction)
        {
            league.Tier = TierSilver;
            league.Division = DivisionIn(fraction, GoldFraction, SilverFraction);
        }
        else
        {
            league.Tier = TierBronze;
            league.Division = DivisionIn(fraction, SilverFraction, 1.0);
        }

        return league;
    }

    private static int CountAbove(double rating, double[] ratingsDescending)
    {
        int low = 0, high = ratingsDescending.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (ratingsDescending[mid] > rating) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private static int CountBelow(double rating, double[] ratingsDescending)
    {
        int low = 0, high = ratingsDescending.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (ratingsDescending[mid] >= rating) low = mid + 1;
            else high = mid;
        }
        return ratingsDescending.Length - low;
    }

    private static int DivisionIn(double fraction, double bandStart, double bandEnd)
    {
        var span = bandEnd - bandStart;
        if (span <= 0) return TopDivision;
        var slice = (int)((fraction - bandStart) / span * DivisionsPerTier);
        return Math.Clamp(TopDivision - slice, 0, TopDivision);
    }
}
