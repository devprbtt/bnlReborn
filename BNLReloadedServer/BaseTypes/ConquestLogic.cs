namespace BNLReloadedServer.BaseTypes;

// Server-only CDB extension stored on the dedicated Conquest map card. It is
// intentionally not part of CardMap's binary protocol, so older clients can
// consume the same catalogue while operators can tune matches live in CDB.
public sealed class ConquestLogic
{
    public float InitialBricks { get; set; } = 2000;
    public float BrickCap { get; set; } = 3000;
    public float CaptureSeconds { get; set; } = 10;
    public float AttackSeconds { get; set; } = 90;
    public float LiteBbSeconds { get; set; } = 7 * 60;
    public float ClassicBbSeconds { get; set; } = 5 * 60;
    public float ExtremeBbSeconds { get; set; } = 3 * 60;
    public float TripleCaptureRate { get; set; } = 2;
    public float ZoneHalfWidth { get; set; } = 6;
    public float ZoneDepthBelow { get; set; } = 8;
    public float ZoneHeightAbove { get; set; } = 4;

    public ConquestLogic Validated() => new()
    {
        BrickCap = PositiveOr(BrickCap, 3000),
        InitialBricks = Math.Clamp(float.IsFinite(InitialBricks) ? InitialBricks : 2000, 0,
            PositiveOr(BrickCap, 3000)),
        CaptureSeconds = PositiveOr(CaptureSeconds, 10),
        AttackSeconds = PositiveOr(AttackSeconds, 90),
        LiteBbSeconds = PositiveOr(LiteBbSeconds, 7 * 60),
        ClassicBbSeconds = PositiveOr(ClassicBbSeconds, 5 * 60),
        ExtremeBbSeconds = PositiveOr(ExtremeBbSeconds, 3 * 60),
        TripleCaptureRate = PositiveOr(TripleCaptureRate, 2),
        ZoneHalfWidth = PositiveOr(ZoneHalfWidth, 6),
        ZoneDepthBelow = PositiveOr(ZoneDepthBelow, 8),
        ZoneHeightAbove = PositiveOr(ZoneHeightAbove, 4)
    };

    private static float PositiveOr(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;
}
