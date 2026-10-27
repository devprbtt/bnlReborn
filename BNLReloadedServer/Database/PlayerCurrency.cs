using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class PlayerCurrency
{
    // Return snapshots, never a shared mutable wallet. New accounts start at zero.
    public static Dictionary<CurrencyType, float> CreateStartingBalance() => Balance(0, 0);

    public static Dictionary<CurrencyType, float> Balance(long gold, long platinum) => new()
    {
        [CurrencyType.Virtual] = gold,
        [CurrencyType.Real] = platinum
    };
}
