namespace BNLReloadedServer.BaseTypes;

/// <summary>
/// The kill accolades the Reborn client announces (client KillAccolades.cs). Values are archived per match;
/// never renumber them.
/// </summary>
public enum KillAccolade : byte
{
    FirstKill = 0,
    Headshot = 1,
    DoubleKill = 2,
    MultiKill = 3,
    UltraKill = 4,
    Fantastic = 5,
    Unbelievable = 6
}

/// <summary>
/// Server copy of the client's accolade rules, so profile totals count what players saw announced. A kill continues
/// the killer's streak if it lands within <see cref="StreakWindowSeconds"/> of their previous kill; the second to
/// sixth-and-later kills of a streak earn Double, Multi, Ultra, Fantastic and Unbelievable. A death ends the victim's
/// streak. The match's first counted kill earns First Kill and a critical kill earns Headshot.
/// </summary>
public sealed class KillAccoladeTracker
{
    // Must match the client's KillAccolades.StreakWindow.
    public const double StreakWindowSeconds = 8;

    private readonly Dictionary<uint, (double At, int Chain)> _streaks = [];
    private bool _firstKillAwarded;

    /// <summary>A player died. <paramref name="killerPlayerId"/> is null unless the death counts as a kill.</summary>
    /// <returns>The killer's accolades for this kill and their streak length (0 when it is not a counted kill).</returns>
    public (List<KillAccolade> Accolades, int Streak) Register(uint victimPlayerId, uint? killerPlayerId, bool crit,
        double nowSeconds)
    {
        _streaks.Remove(victimPlayerId);
        var accolades = new List<KillAccolade>();
        if (killerPlayerId is not { } killer || killer == victimPlayerId) return (accolades, 0);

        if (!_firstKillAwarded)
        {
            _firstKillAwarded = true;
            accolades.Add(KillAccolade.FirstKill);
        }
        if (crit) accolades.Add(KillAccolade.Headshot);

        var streak = _streaks.TryGetValue(killer, out var previous) && nowSeconds - previous.At <= StreakWindowSeconds
            ? previous.Chain + 1
            : 1;
        _streaks[killer] = (nowSeconds, streak);
        if (streak >= 2)
            accolades.Add(streak switch
            {
                2 => KillAccolade.DoubleKill,
                3 => KillAccolade.MultiKill,
                4 => KillAccolade.UltraKill,
                5 => KillAccolade.Fantastic,
                _ => KillAccolade.Unbelievable
            });
        return (accolades, streak);
    }
}
