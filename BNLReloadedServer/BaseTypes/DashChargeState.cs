namespace BNLReloadedServer.BaseTypes;

// One accepted charge belongs to one gear/tool and permits one cast.
public sealed class DashChargeState
{
    private Key? gear;
    private byte toolIndex;
    private DateTimeOffset? started;
    private DateTimeOffset? castDeadline;
    private bool maximum;

    public void Clear() { gear = null; started = null; castDeadline = null; maximum = false; }

    private static bool CanPay(ToolLogic tool, ToolDash dash, bool max) =>
        tool.GetAmmoData()?.IsEnoughAmmoToUse((dash.Ammo?.Rate ?? 0) *
            (max ? dash.MaxAmmoRateMultiplier : dash.MinAmmoRateMultiplier)) ?? true;

    public bool Start(Unit unit, byte index, DateTimeOffset now)
    {
        Clear();
        var tool = unit.CurrentGear?.GetTool(index);
        if (unit.IsDead || unit.IsBuff(BuffType.Root) || tool?.Tool is not ToolDash dash || !CanPay(tool, dash, false)) return false;
        gear = unit.CurrentGear!.Key; toolIndex = index; started = now;
        return true;
    }

    public bool Finish(Unit unit, byte index, DateTimeOffset now, out bool max)
    {
        max = false;
        var tool = unit.CurrentGear?.GetTool(index);
        if (started == null || gear != unit.CurrentGear?.Key || toolIndex != index ||
            unit.IsDead || unit.IsBuff(BuffType.Root) || tool?.Tool is not ToolDash dash || !CanPay(tool, dash, false))
        { Clear(); return false; }
        // If ammo drained during charging, allow only the short dash that can
        // actually be paid for; tell the client that result before it moves.
        max = (now - started.Value).TotalSeconds >= dash.MaxChargeTime - .1f && CanPay(tool, dash, true);
        maximum = max; started = null; castDeadline = now.AddSeconds(5);
        return true;
    }

    public bool Consume(Unit unit, byte index, DateTimeOffset now, out bool max)
    {
        max = maximum;
        var tool = unit.CurrentGear?.GetTool(index);
        bool accepted = castDeadline.HasValue && now <= castDeadline && gear == unit.CurrentGear?.Key &&
            toolIndex == index && !unit.IsDead && !unit.IsBuff(BuffType.Root) &&
            tool?.Tool is ToolDash dash && CanPay(tool, dash, max);
        Clear();
        return accepted;
    }
}
