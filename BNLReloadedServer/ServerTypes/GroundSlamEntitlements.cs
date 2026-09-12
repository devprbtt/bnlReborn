namespace BNLReloadedServer.ServerTypes;

public sealed class GroundSlamEntitlements
{
    private readonly Dictionary<uint, Entry> _pending = [];

    private readonly record struct Entry(uint UnitId, byte ToolIndex);

    public void Begin(uint playerId, uint unitId, byte toolIndex)
    {
        _pending[playerId] = new Entry(unitId, toolIndex);
    }

    public void Cancel(uint playerId)
    {
        _pending.Remove(playerId);
    }

    public bool TryConsume(uint playerId, uint unitId, byte toolIndex)
    {
        if (!_pending.Remove(playerId, out var entry))
        {
            return false;
        }

        return entry.UnitId == unitId && entry.ToolIndex == toolIndex;
    }
}
