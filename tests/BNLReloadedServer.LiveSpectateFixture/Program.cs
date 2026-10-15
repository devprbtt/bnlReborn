using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;

var directory = Path.Combine(Path.GetTempPath(), "bnl-live-spectate-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("BNL_REPLAY_DIR", directory);
var db = (RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(RegionServerDatabase));
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
object Init(string name) {
    var field = typeof(RegionServerDatabase).GetField(name, flags)!;
    var value = Activator.CreateInstance(field.FieldType)!;
    field.SetValue(db, value); return value;
}
var users = Init("_connectedUsers");
Init("_customGamePlayerLists"); Init("_customGamesLock");
var connectionType = typeof(RegionServerDatabase).GetNestedType("ConnectionInfo", BindingFlags.NonPublic)!;
var connection = Activator.CreateInstance(connectionType, Guid.NewGuid(), new ChatPlayer { PlayerId = 1, Nickname = "Fixture" }, false)!;
users.GetType().GetMethod("TryAdd")!.Invoke(users, [1u, connection]);
var ids = (ConcurrentDictionary<ulong, string>)Init("_spectatableMatchIds");
var instances = (ConcurrentDictionary<string, IGameInstance>)Init("_gameInstances");
var games = (ConcurrentDictionary<string, MatchmakerInitiator>)Init("_matchmakerGames");
((ServerCatalogue)Databases.Catalogue).Replicate([new CardGlobalLogic { Id = "global_logic", CustomGame = new CustomGameLogic { MaxSpectatorsPerMatch = 1 } }]);
var initiator = new MatchmakerInitiator(new CardGameMode { PlayersPerTeam = 4 }, [], []);
games["live"] = initiator;
var instance = DispatchProxy.Create<IGameInstance, LiveInstance>();
var recorder = (LiveInstance)(object)instance;
instances["live"] = instance;
// Exactly the first id allocated by GetCustomGames, not a small custom-game id.
const ulong liveId = 0x8000_0000_0000_0001UL;
ids[liveId] = "live";
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
Check(ReplayStore.IsReplayId(liveId), "live browser id overlaps the replay high-bit discriminator");
Check(db.CheckSpectateCustomGame(1, liveId, "") == CustomGameSpectateResult.Accepted, "live match takes precedence over replay classification");
Check(db.SpectateCustomGame(1, liveId), "live spectate admission succeeds");
Check(recorder.Sent.SequenceEqual(new uint[] { 1 }) && initiator.IsPlayerSpectator(1), "spectator enters the live zone");
Check((string?)connectionType.GetProperty("GameInstanceId")!.GetValue(connection) == "live", "live instance recorded for reconnect");
Check(db.CheckSpectateCustomGame(1, liveId, "") == CustomGameSpectateResult.TooManySpectators, "live capacity check is preserved");
recorder.Ended = true;
Check(db.CheckSpectateCustomGame(1, liveId, "") == CustomGameSpectateResult.NoSuchGame && !db.SpectateCustomGame(1, liveId), "ended match cannot be entered");
recorder.Ended = false; recorder.Started = false;
Check(db.CheckSpectateCustomGame(1, liveId, "") == CustomGameSpectateResult.NoSuchGame, "unstarted match cannot be entered");
recorder.Started = true;
Check(db.CheckSpectateCustomGame(99, liveId, "") == CustomGameSpectateResult.NoSuchGame, "unknown player cannot spectate");
var replayId = ReplayStore.ReplayIdFor("missing-fixture:1");
Check(db.CheckSpectateCustomGame(1, replayId, "") == CustomGameSpectateResult.NoSuchGame && !db.SpectateCustomGame(1, replayId), "unmapped replay id still uses replay lookup");
Check(db.CheckSpectateCustomGame(1, 42, "") == CustomGameSpectateResult.NoSuchGame, "unknown custom game remains unavailable");
Console.WriteLine("LIVE_SPECTATE_FIXTURE_OK checks=" + checks);

public class LiveInstance : DispatchProxy {
    public bool Started = true, Ended;
    public readonly List<uint> Sent = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) {
        if (method!.Name == "get_IsStarted") return Started;
        if (method.Name == "IsOver") return Ended;
        if (method.Name == "SendUserToZone") Sent.Add((uint)args![0]!);
        return null;
    }
}
