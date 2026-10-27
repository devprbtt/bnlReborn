using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
class FixturePlayers : DispatchProxy
{
    private Dictionary<uint, Key> _lastPlayed = null!;

    public static IPlayerDatabase Create(Dictionary<uint, Key> lastPlayed)
    {
        var proxy = Create<IPlayerDatabase, FixturePlayers>();
        ((FixturePlayers)(object)proxy)._lastPlayed = lastPlayed;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IPlayerDatabase.GetPlayerDataNoWait) => null,
        nameof(IPlayerDatabase.GetPlayerProfile) => new ProfileData { Nickname="Fixture Player" },
        nameof(IPlayerDatabase.GetDeviceLevels) => new Dictionary<Key,int>(),
        nameof(IPlayerDatabase.GetLastPlayedHero) => _lastPlayed[(uint)args![0]!],
        nameof(IPlayerDatabase.GetLoadoutForHero) => new LobbyLoadout
        {
            HeroKey = (Key)args![1]!, Devices = new Dictionary<int, Key>(), Perks = [], SkinKey = Key.None
        },
        _ => throw new NotSupportedException(method?.Name)
    };
}

class RecordingLobby : DispatchProxy
{
    private List<LobbyUpdate> _sent = null!;

    public static IServiceLobby Create(List<LobbyUpdate> sent)
    {
        var proxy = Create<IServiceLobby, RecordingLobby>();
        ((RecordingLobby)(object)proxy)._sent = sent;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(IServiceLobby.SendLobbyUpdate)) _sent.Add((LobbyUpdate)args![0]!);
        return method?.ReturnType == typeof(bool) ? false : null;
    }
}
