// A player entering a lobby must receive its first lobby message from the lobby queue, after
// AddPlayer has run, so the snapshot always contains that player. The old code took the snapshot
// on the caller's thread while AddPlayer was still queued; a client that received a complete lobby
// without itself threw in LobbyAlterEgo.GetHero and sat at "Loading lobby 100%".
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.Servers;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
object Raw(Type type) => RuntimeHelpers.GetUninitializedObject(type);
void Field(object obj, string name, object? value)
{
    var type = obj.GetType();
    FieldInfo? field = null;
    while (type != null && (field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)) == null) type = type.BaseType;
    field!.SetValue(obj, value);
}

var recorded = new List<LobbyUpdate>();
var lobbyService = RecordingLobbyService.Create(recorded);
var playerId = 7u;
var guid = Guid.NewGuid();

// Lobby with a captured action queue and the state AddPlayer would have produced.
var lobby = (CapturingLobby)Raw(typeof(CapturingLobby));
lobby.Queued = new List<Action>();
var data = new LobbyData();
data.Players[playerId] = new PlayerLobbyState { PlayerId = playerId, Hero = new Key("unit_hero_fixture"), Status = LobbyStatus.Online };
data.MatchModeKey = new Key("match_fixture");
data.GameModeKey = new Key("game_mode_fixture");
Field(lobby, "<LobbyData>k__BackingField", data);
Field(lobby, "_votesLock", (object)new System.Threading.Lock());

var instance = (GameInstance)Raw(typeof(GameInstance));
Field(instance, "<Lobby>k__BackingField", lobby);
Field(instance, "<GameInitiator>k__BackingField", Raw(typeof(WaitingArenaInitiator)));
var connectionType = typeof(GameInstance).GetNestedType("MatchConnectionInfo", BindingFlags.NonPublic)!;
var connection = Activator.CreateInstance(connectionType, guid, Guid.NewGuid(), TeamType.Team1, (ulong?)null)!;
var connected = (System.Collections.IDictionary)Activator.CreateInstance(typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(uint), connectionType))!;
connected[playerId] = connection;
Field(instance, "_connectedUsers", connected);
Field(instance, "_services", new ConcurrentDictionary<Guid, Dictionary<ServiceId, IService>>(
    new[] { new KeyValuePair<Guid, Dictionary<ServiceId, IService>>(guid, new() { [ServiceId.ServiceLobby] = lobbyService }) }));
var server = (AsyncTaskTcpServer)Raw(typeof(AsyncTaskTcpServer));
Field(server, "_senderTasks", new ConcurrentDictionary<Guid, AsyncSenderTask>());
var sender = new SessionSender(server);
Field(instance, "_lobbySender", sender);

// Spectators and plain re-entries skip AddPlayer; the ordering contract must hold for both flags.
foreach (var entering in new[] { true, false })
{
    recorded.Clear(); lobby.Queued.Clear();
    instance.UserEnteredLobby(playerId, entering);
    Check(recorded.Count == 0, $"entering={entering}: nothing is sent to the joining client on the caller's thread");
    Check(lobby.Queued.Count == 1, $"entering={entering}: exactly one lobby action is queued");
    // Run the queued action as the lobby thread would; AddPlayer itself needs the player database,
    // so this fixture pre-seeds the lobby state and exercises the re-entry flag for the executed path.
    if (entering) continue;
    lobby.Queued[0]();
    Check(recorded.Count == 1, "the queued action sends exactly one snapshot to the joining client");
    var snapshot = recorded[0];
    Check(snapshot.Players != null && snapshot.Players.Any(p => p.PlayerId == playerId),
        "the snapshot contains the joining player");
    Check(snapshot.MatchMode == data.MatchModeKey && snapshot.GameMode == data.GameModeKey,
        "the snapshot is the full lobby, not a partial update");
}

var order = typeof(GameInstance).GetMethod("UserEnteredLobby")!;
Check(order.GetParameters().Length == 2, "UserEnteredLobby signature unchanged for RegionServerDatabase");
Console.WriteLine($"Lobby join order fixture passed: {checks} checks.");

class CapturingLobby : GameLobby
{
    public List<Action> Queued = null!;
    private CapturingLobby() : base(null!, null!, string.Empty, Key.None, Key.None, []) { }
    public override bool EnqueueAction(Action func) { Queued.Add(func); return true; }
}

class RecordingLobbyService : DispatchProxy
{
    private List<LobbyUpdate> _sink = null!;
    public static IServiceLobby Create(List<LobbyUpdate> sink)
    {
        var proxy = Create<IServiceLobby, RecordingLobbyService>();
        ((RecordingLobbyService)(object)proxy)._sink = sink;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IServiceLobby.SendLobbyUpdate)) _sink.Add((LobbyUpdate)args![0]!);
        return targetMethod?.ReturnType == typeof(bool) ? false : null;
    }
}
