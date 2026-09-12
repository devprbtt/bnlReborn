namespace BNLReloadedServer.ServerTypes;

public sealed class ForceFallEntitlements
{
    private readonly Dictionary<uint, Entry> _latestActions = [];

    private readonly record struct Entry(uint UnitId, bool Armed);

    public void Begin(uint playerId, uint unitId)
    {
        _latestActions[playerId] = new Entry(unitId, true);
    }

    public void Cancel(uint playerId, uint unitId)
    {
        _latestActions[playerId] = new Entry(unitId, false);
    }

    public void Clear(uint playerId)
    {
        _latestActions.Remove(playerId);
    }

    public bool TryConsume(uint playerId, uint unitId)
    {
        // Older clients do not announce ForceFall. Preserve their ordinary CTRL
        // impacts, but a witnessed double jump always vetoes the next forced hit.
        if (!_latestActions.Remove(playerId, out var entry))
        {
            return true;
        }

        return entry.UnitId == unitId && entry.Armed;
    }
}
