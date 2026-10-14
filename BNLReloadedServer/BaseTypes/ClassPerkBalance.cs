namespace BNLReloadedServer.BaseTypes;

// CDB balance data; descriptions carry resolved values over the existing perk wire record.
public sealed class ClassPerkBalance
{
    public float OutOfCombatSeconds { get; set; } = 5;
    public float HealingReturnPercent { get; set; } = 50;
    public float RegenMaxHealthPercentPerSecond { get; set; } = 10;
    public float SpawnDistance { get; set; } = 1.15f;
    // Skill III: share of each allied respawn timer's remaining time removed by a kill.
    public float RespawnReductionPercent { get; set; } = 70;
    // Brains II: effect card applied to an ally whenever this hero's healing restores their health.
    public string? HealedAllyEffect { get; set; }
}
