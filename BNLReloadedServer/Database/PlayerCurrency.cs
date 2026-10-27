using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class PlayerCurrency
{
    // Currency persistence and match earnings are not enabled yet. Do not expose
    // the old test funds or share a mutable wallet across player snapshots.
    public static Dictionary<CurrencyType, float> CreateStartingBalance() => new()
    {
        [CurrencyType.Virtual] = 0f,
        [CurrencyType.Real] = 0f
    };
}
