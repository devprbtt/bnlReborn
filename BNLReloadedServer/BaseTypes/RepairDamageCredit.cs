namespace BNLReloadedServer.BaseTypes;

// Server-only outstanding enemy health damage. Every heal spends it, regardless of healer.
public sealed class RepairDamageCredit
{
    private float remaining;
    private TeamType team;
    private void MatchTeam(TeamType target)
    {
        if (team != target) { remaining = 0; team = target; }
    }
    public void Record(float damage, TeamType target, TeamType? source)
    {
        MatchTeam(target);
        if (float.IsFinite(damage) && damage > 0 && target != TeamType.Neutral &&
            source.HasValue && source != TeamType.Neutral && source != target)
            remaining += damage;
    }
    public float Consume(float healing, TeamType target)
    {
        MatchTeam(target);
        if (!float.IsFinite(healing) || healing <= 0) return 0;
        float eligible = Math.Min(remaining, healing);
        remaining -= eligible;
        return eligible;
    }
}
