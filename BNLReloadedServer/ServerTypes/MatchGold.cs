using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

/// <summary>
/// Match Gold (docs/DECISIONS.md, 2026-10-25 policy): 10 Gold per minute played, up to 40 minutes, 50 for a win and
/// 10 for each stat a player leads on their own team. Only matchmade casual/ranked rounds pay, and AFK players get
/// nothing. The payout travels inside each player's end-match stats so every client can show the whole lobby's Gold.
/// </summary>
public static class MatchGold
{
    public const int GoldPerMinute = 10;
    public const int MaxPaidMinutes = 40;
    public const int WinBonus = 50;
    public const int LeaderBonus = 10;
    public const int MaxTotal = GoldPerMinute * MaxPaidMinutes + WinBonus + LeaderBonus * 7;

    public enum Category { Healing, Damage, DevicesDestroyed, Kills, Assists, ObjectiveDamage, DropsCollected }

    public static readonly IReadOnlyDictionary<Category, PlayerMatchStatType> LeaderStat = new Dictionary<Category, PlayerMatchStatType>
    {
        [Category.Healing] = PlayerMatchStatType.GoldMostHealing,
        [Category.Damage] = PlayerMatchStatType.GoldMostDamage,
        [Category.DevicesDestroyed] = PlayerMatchStatType.GoldMostDevicesDestroyed,
        [Category.Kills] = PlayerMatchStatType.GoldMostKills,
        [Category.Assists] = PlayerMatchStatType.GoldMostAssists,
        [Category.ObjectiveDamage] = PlayerMatchStatType.GoldMostObjectiveDamage,
        [Category.DropsCollected] = PlayerMatchStatType.GoldMostDropsCollected
    };

    public sealed record Candidate(uint PlayerId, TeamType Team, bool Eligible, bool Winner, double PlayedSeconds,
        IReadOnlyDictionary<Category, double> Values);

    public sealed record Award(int Participation, int Victory, IReadOnlyList<Category> Leads)
    {
        public int Total => Participation + Victory + LeaderBonus * Leads.Count;
    }

    /// <summary>The same counters the scoreboard and stat leaderboards use, so a leader is who players see on top.</summary>
    public static double Value(Category category, IReadOnlyDictionary<PlayerMatchStatType, int> stats,
        IReadOnlyDictionary<ScoreType, float> raw) => category switch
    {
        // Ally health actually restored against enemy damage: no self-healing or overhealing.
        Category.Healing => raw.GetValueOrDefault(ScoreType.EffectiveAllyHealing),
        // Enemy players only; friendly fire is tracked separately and never counts.
        Category.Damage => raw.GetValueOrDefault(ScoreType.DamagePlayerByHero) + raw.GetValueOrDefault(ScoreType.DamagePlayerByBlock),
        // Placed devices and hero blocks, not building materials.
        Category.DevicesDestroyed => raw.GetValueOrDefault(ScoreType.DevicesDestroyed) + raw.GetValueOrDefault(ScoreType.HeroBlocksDestroyed),
        Category.Kills => stats.GetValueOrDefault(PlayerMatchStatType.Kill),
        Category.Assists => stats.GetValueOrDefault(PlayerMatchStatType.Assist),
        Category.ObjectiveDamage => stats.GetValueOrDefault(PlayerMatchStatType.Objective),
        Category.DropsCollected => raw.GetValueOrDefault(ScoreType.SupplyCollected) + raw.GetValueOrDefault(ScoreType.BlockbusterCollected),
        _ => 0
    };

    public static int Participation(double playedSeconds) =>
        (int)Math.Floor(GoldPerMinute * Math.Clamp(playedSeconds, 0, MaxPaidMinutes * 60) / 60);

    /// <summary>Awards for eligible players. Leaders are found per team among eligible players; ties all qualify, zero never does.</summary>
    public static Dictionary<uint, Award> Compute(IEnumerable<Candidate> players)
    {
        var eligible = players.Where(p => p.Eligible).ToList();
        var leads = eligible.ToDictionary(p => p.PlayerId, _ => new List<Category>());
        foreach (var team in eligible.GroupBy(p => p.Team))
        foreach (var category in Enum.GetValues<Category>())
        {
            var best = team.Max(p => p.Values.GetValueOrDefault(category));
            if (!(best > 0)) continue;
            foreach (var p in team.Where(p => p.Values.GetValueOrDefault(category) == best)) leads[p.PlayerId].Add(category);
        }
        return eligible.ToDictionary(p => p.PlayerId,
            p => new Award(Participation(p.PlayedSeconds), p.Winner ? WinBonus : 0, leads[p.PlayerId]));
    }

    /// <summary>Writes the breakdown into a player's end-match stats; a missing award shows as zero Gold.</summary>
    public static void Write(Award? award, Dictionary<PlayerMatchStatType, int> stats)
    {
        stats[PlayerMatchStatType.GoldEarned] = award?.Total ?? 0;
        stats[PlayerMatchStatType.GoldParticipation] = award?.Participation ?? 0;
        stats[PlayerMatchStatType.GoldVictory] = award?.Victory ?? 0;
        foreach (var (category, type) in LeaderStat)
            stats[type] = award?.Leads.Contains(category) == true ? LeaderBonus : 0;
    }

    /// <summary>The Gold the master credits for this result, or 0 if absent or outside what the policy can pay.</summary>
    public static int Earned(EndMatchResults result)
    {
        if (result.MatchData.TimeTrialData != null) return 0;
        var stats = result.MatchData.PlayersData?.FirstOrDefault(p => p.PlayerId == result.PlayerId)?.Stats?.Stats;
        var gold = stats?.GetValueOrDefault(PlayerMatchStatType.GoldEarned) ?? 0;
        return gold is >= 0 and <= MaxTotal ? gold : 0;
    }

    /// <summary>
    /// Released clients show Gold as RewardCurrency times (1 + every reward bonus fraction), the original game's
    /// percentage bonuses. Send the base that makes that product equal the Gold actually credited.
    /// </summary>
    public static float DisplayBase(int total, IEnumerable<float> bonusFractions) =>
        total / (1f + bonusFractions.Sum());
}
