#pragma warning disable CS9216 // Reflection initialization boxes the lock; production retains its Lock type.
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Servers;

const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
object Raw(Type t) => RuntimeHelpers.GetUninitializedObject(t);
void Set(object o, string f, object? v) => o.GetType().GetField(f, flags)!.SetValue(o, v);
object Field(object o, string f) => o.GetType().GetField(f, flags)!.GetValue(o)!;
void Prop(object o, string p, object? v) => o.GetType().GetProperty(p)!.SetValue(o, v);
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
var db = (RegionServerDatabase)Raw(typeof(RegionServerDatabase));
foreach (var name in new[] { "_customGamesLock", "_customGamePlayerLists", "_connectedUsers", "_services", "_gameInstances", "_matchmakerGames" })
{
    var f = typeof(RegionServerDatabase).GetField(name, flags)!;
    Set(db, name, Activator.CreateInstance(f.FieldType));
}
var senders = new Dictionary<ulong, Sink>();
void Room(ulong id, CustomGameStatus status)
{
    var room = (CustomGamePlayerGroup)Raw(typeof(CustomGamePlayerGroup));
    Set(room, "_lock", new System.Threading.Lock());
    Set(room, "<GameInfo>k__BackingField", new CustomGameInfo { Id = id, Players = 1, Status = status });
    var sender = DispatchProxy.Create<ISender, Sink>();
    senders[id] = (Sink)(object)sender;
    Set(room, "<ChatRoom>k__BackingField", new ChatRoom(new RoomIdCustomGame { CustomGameId = id }, sender));
    typeof(RegionServerDatabase).GetMethod("AddCustomGameEntry", flags)!.Invoke(db, [id, (room, sender)]);
}
object User(uint id, ulong room, bool online)
{
    var type = typeof(RegionServerDatabase).GetNestedType("ConnectionInfo", BindingFlags.NonPublic)!;
    var user = Raw(type);
    Prop(user, "Guid", Guid.NewGuid()); Prop(user, "CustomGameId", room); Prop(user, "Online", online);
    var users = Field(db, "_connectedUsers");
    users.GetType().GetMethod("TryAdd")!.Invoke(users, [id, user]);
    return user;
}
Room(1, CustomGameStatus.Preparing); Room(2, CustomGameStatus.Lobby); Room(3, CustomGameStatus.Match); Room(4, CustomGameStatus.Match);
var owner = User(42, 4, true); // One real owner, four stale 1/10 browser rows.
var games = db.GetCustomGames();
Check(games.Count == 1 && games[0].Id == 4, "stale rooms in every phase disappear");
Check((ulong?)owner.GetType().GetProperty("CustomGameId")!.GetValue(owner) == 4, "old room closure preserves newer membership");
Check(senders[1].Clears == 2 && senders[2].Clears == 2 && senders[3].Clears == 2, "room and chat subscriptions cleared");
var other = User(43, 4, true);
Prop(owner, "Online", false);
Check(db.GetCustomGames().Count == 1, "remaining online ally keeps room and reconnect seat");
Prop(other, "Online", false);
Check(db.GetCustomGames().Count == 0, "last disconnect closes active room");
Check(owner.GetType().GetProperty("CustomGameId")!.GetValue(owner) == null, "offline owner membership cleared");
Check(other.GetType().GetProperty("CustomGameId")!.GetValue(other) == null, "offline teammate membership cleared");
Check(!db.RemoveCustomGame(4), "repeated closure is harmless");
Room(5, CustomGameStatus.Match);
User(44, 5, true); // Spectators have the same connection room membership, no roster seat needed.
Check(db.GetCustomGames().Single().Id == 5, "online spectator counts as an occupant");
Room(6, CustomGameStatus.Match);
var last = User(45, 6, false);
Prop(last, "GameInstanceId", "instance-6");
var entries = Field(db, "_customGamePlayerLists");
var tuple = entries.GetType().GetProperty("Item", [typeof(ulong)])!.GetValue(entries, [6UL])!;
((CustomGamePlayerGroup)tuple.GetType().GetField("Item1")!.GetValue(tuple)!).GameInstanceId = "instance-6";
var instance = DispatchProxy.Create<IGameInstance, Sink>();
var instances = Field(db, "_gameInstances");
instances.GetType().GetMethod("TryAdd")!.Invoke(instances, ["instance-6", instance]);
db.GetCustomGames();
Check(((Sink)(object)instance).Departures == 1, "empty room also detaches player from match instance");
Console.WriteLine($"Empty custom rooms: {checks} checks passed.");
public class Sink : DispatchProxy
{
    public int Clears, Departures;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "UnsubscribeAll") Clears++;
        if (method.Name == "PlayerLeftInstance") Departures++;
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
