namespace BNLReloadedServer.BaseTypes;

public static class PiggyBankBalance
{
    // Preserve the existing payout rate (including its factor of five).
    public static float Calculate(double seconds, float resourcePerInterval, float generationInterval)
    {
        if (!double.IsFinite(seconds) || !float.IsFinite(resourcePerInterval) ||
            !float.IsFinite(generationInterval) || seconds <= 0 || resourcePerInterval <= 0 || generationInterval <= 0) return 0;
        return (float)Math.Min(float.MaxValue, seconds * resourcePerInterval / (generationInterval * 5d));
    }
}
