namespace BNLReloadedServer.BaseTypes;

// CDB balance data; descriptions carry resolved values over the existing perk wire record.
public sealed class ClassPerkBalance
{
    public float OutOfCombatSeconds { get; set; } = 5;
    public float HealingReturnPercent { get; set; } = 50;
    public float RegenMaxHealthPercentPerSecond { get; set; } = 10;
    public float SpawnDistance { get; set; } = 1.15f;
}
