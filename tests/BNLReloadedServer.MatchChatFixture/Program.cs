// Match chat rooms are named by the client. A spectator in a public match (the "jbake" case) or a player
// on the other team could name a team's private room and post into it. GameInstance.GetChatRoom must only
// hand out rooms the sender belongs to: own team and all chat for players, all chat for custom-game
// spectators, nothing for public-match spectators.
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0, failures = 0;
void Check(bool pass, string name) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); }

RoomIdTeam Room(TeamType team) => new() { InstanceId = 11, LobbyId = 22, Team = team };
var rooms = new InstanceChatRooms(new ChatRoom(Room(TeamType.Team1), DispatchProxy.Create<ISender, Roles>()),
    new ChatRoom(Room(TeamType.Team2), DispatchProxy.Create<ISender, Roles>()),
    new ChatRoom(Room(TeamType.Neutral), DispatchProxy.Create<ISender, Roles>()));

GameInstance Instance(IGameInitiator initiator)
{
    var instance = (GameInstance)RuntimeHelpers.GetUninitializedObject(typeof(GameInstance));
    typeof(GameInstance).GetField("<GameInitiator>k__BackingField", Any)!.SetValue(instance, initiator);
    typeof(GameInstance).GetField("<ChatRooms>k__BackingField", Any)!.SetValue(instance, rooms);
    typeof(GameInstance).GetField("<GameInstanceId>k__BackingField", Any)!.SetValue(instance, "fixture");
    return instance;
}

// Public match: 1 on Team1, 2 on Team2, 9 spectating.
var matchmaker = DispatchProxy.Create<IGameInitiator, Roles>();
((Roles)(object)matchmaker).Teams = new() { [1] = TeamType.Team1, [2] = TeamType.Team2 };
((Roles)(object)matchmaker).Spectators = [9];
var match = Instance(matchmaker);
Check(match.GetChatRoom(1, Room(TeamType.Team1)) == rooms.Team1Room, "Team1 player posts to Team1 chat");
Check(match.GetChatRoom(1, Room(TeamType.Neutral)) == rooms.BothTeamsRoom, "Team1 player posts to all chat");
Check(match.GetChatRoom(1, Room(TeamType.Team2)) == null, "Team1 player cannot post into Team2 chat");
Check(match.GetChatRoom(2, Room(TeamType.Team2)) == rooms.Team2Room && match.GetChatRoom(2, Room(TeamType.Team1)) == null,
    "Team2 player posts only to own team chat");
Check(match.GetChatRoom(9, Room(TeamType.Team1)) == null && match.GetChatRoom(9, Room(TeamType.Team2)) == null,
    "public-match spectator cannot post into either team's chat");
Check(match.GetChatRoom(9, Room(TeamType.Neutral)) == null, "public-match spectator cannot post to all chat");
Check(match.GetChatRoom(1, new RoomIdTeam { InstanceId = 11, LobbyId = 23, Team = TeamType.Team1 }) == null,
    "a room id from another lobby is refused");

// Custom game: spectators keep all chat (casters, friends) but not team chat.
var custom = (CustomGamePlayerGroup)RuntimeHelpers.GetUninitializedObject(typeof(CustomGamePlayerGroup));
foreach (var field in typeof(CustomGamePlayerGroup).GetFields(Any).Where(f => f.Name is "_lock" or "_spectators" or "_players"))
    field.SetValue(custom, Activator.CreateInstance(field.FieldType));
((List<uint>)typeof(CustomGamePlayerGroup).GetField("_spectators", Any)!.GetValue(custom)!).Add(9);
var customMatch = Instance(custom);
Check(customMatch.GetChatRoom(9, Room(TeamType.Neutral)) == null, "custom-game spectator cannot post to all chat");
Check(customMatch.GetChatRoom(9, Room(TeamType.Team1)) == null && customMatch.GetChatRoom(9, Room(TeamType.Team2)) == null,
    "custom-game spectator cannot post into team chat");

var notifier = DispatchProxy.Create<IServiceChat, Roles>();
var observed = (Roles)(object)notifier;
var subscribe = typeof(GameInstance).GetMethod("AddToChat", Any)!;
foreach(var (who,team,wanted) in new[]{(9u,TeamType.Neutral,3),(9u,TeamType.Team1,3),(1u,TeamType.Team1,2),(2u,TeamType.Team2,2)})
{
    observed.AddedRooms.Clear();
    subscribe.Invoke(match,[who,team,Guid.NewGuid(),notifier]);
    Check(observed.AddedRooms.Count==wanted,"chat subscription count for "+who+" "+team);
    Check(observed.AddedRooms.Any(r=>r.Team==TeamType.Neutral),"all chat subscribed");
    if(who==9) Check(observed.AddedRooms.Any(r=>r.Team==TeamType.Team1)&&observed.AddedRooms.Any(r=>r.Team==TeamType.Team2),"both team chats subscribed");
    else Check(!observed.AddedRooms.Any(r=>r.Team!=(team)&&r.Team!=TeamType.Neutral),"players do not receive other team chat");
}
var connectedField=typeof(GameInstance).GetField("_connectedUsers",Any)!;
var connected=Activator.CreateInstance(connectedField.FieldType)!;connectedField.SetValue(match,connected);
var servicesField=typeof(GameInstance).GetField("_services",Any)!;
var services=(System.Collections.Concurrent.ConcurrentDictionary<Guid,Dictionary<ServiceId,IService>>)Activator.CreateInstance(servicesField.FieldType)!;servicesField.SetValue(match,services);
var connectionType=typeof(GameInstance).GetNestedType("MatchConnectionInfo",BindingFlags.NonPublic)!;
var receivers=new Dictionary<uint,Roles>();
foreach(var id in new uint[]{1,2,9})
{
    var guid=Guid.NewGuid();var connection=Activator.CreateInstance(connectionType,guid,Guid.NewGuid(),TeamType.Neutral,null)!;
    connected.GetType().GetMethod("TryAdd")!.Invoke(connected,[id,connection]);
    var service=DispatchProxy.Create<IServiceZone,Roles>();receivers[id]=(Roles)(object)service;
    services[guid]=new(){[ServiceId.ServiceZone]=service};
}
var result=new EndMatchData {PlayersData=[new EndMatchPlayerData {PlayerId=1,Stats=new EndMatchPlayerStats {Total=55,Stats=[]}}]};
typeof(GameInstance).GetMethod("SendSpectatorResults",Any)!.Invoke(match,[result]);
Check(receivers[9].Results.Single()==result,"spectator receives complete result rows");
Check(receivers[1].Results.Count==0&&receivers[2].Results.Count==0,"spectator result does not replace player reward packets");
Console.WriteLine($"MATCH_CHAT checks={checks} failures={failures}");
return failures == 0 ? 0 : 1;

public class Roles : DispatchProxy
{
    public Dictionary<uint, TeamType> Teams = [];
    public HashSet<uint> Spectators = [];
    public List<RoomIdTeam> AddedRooms = [];
    public List<EndMatchData> Results = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        if(method.Name=="SendEndMatchResult") Results.Add((EndMatchData)args![0]!);
        if(method.Name=="SendRoomAdd") AddedRooms.Add((RoomIdTeam)args![0]!);
        if (method.Name == "GetTeamForPlayer") return Teams.GetValueOrDefault((uint)args![0]!, TeamType.Neutral);
        if (method.Name == "IsPlayerSpectator") return Spectators.Contains((uint)args![0]!);
        var type = method.ReturnType;
        return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    }
}
