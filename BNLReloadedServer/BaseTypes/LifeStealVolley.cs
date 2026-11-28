namespace BNLReloadedServer.BaseTypes;

// One server-registered multi-pellet cast shares a single cooldown decision.
internal sealed class LifeStealVolley(uint casterId, int pelletCount)
{
    private readonly HashSet<ulong> _creditedPellets = [];
    public uint CasterId { get; } = casterId;
    public bool? Admitted { get; set; }

    public bool TryCreditPellet(ulong shotId) =>
        _creditedPellets.Count < pelletCount && _creditedPellets.Add(shotId);
}
