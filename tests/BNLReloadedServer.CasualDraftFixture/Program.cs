using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; }
object? Field(object target, string name) => target.GetType().GetField(name, Any)!.GetValue(target);
void Set(object target, string name, object value) => target.GetType().GetField(name, Any)!.SetValue(target, value);
void Expire(GameLobby lobby, string method, System.Timers.Timer? old = null)
{
    var current = ((LobbyTimerType, System.Timers.Timer))Field(lobby, "_currentTimer")!;
    typeof(GameLobby).GetMethod(method, Any)!.Invoke(lobby, [old ?? current.Item2, new System.Timers.ElapsedEventArgs(DateTime.Now)]);
}
void StopTimer(GameLobby lobby)
{
    if (Field(lobby, "_currentTimer") is ValueTuple<LobbyTimerType, System.Timers.Timer> timer) timer.Item2.Dispose();
}
var catalogue = (ServerCatalogue)Databases.Catalogue;
var mode = new CardGameMode { Id = "game_mode_friendly", Ranking = GameRankingType.Friendly, LobbyMode = new LobbyModeFreePick() };
var ranked = new CardGameMode { Id = "game_mode_ranked", Ranking = GameRankingType.Ranked, LobbyMode = new LobbyModeDraftPick { SelectionTime = 42 } };
var custom = new CardGameMode { Id = "game_mode_custom", Ranking = GameRankingType.None, LobbyMode = new LobbyModeFreePick() };
var global = new CardGlobalLogic { Id = "global_logic" };
var heroes = Enumerable.Range(0, 21).Select(i => new CardUnit { Id = "casual_fixture_" + i,
    Data = new UnitDataPlayer { Class = CasualDraft.Classes[i / 7] }, Scope = ScopeType.Public }).ToArray();
catalogue.Replicate(new Card[] { mode, ranked, custom, global }.Concat(heroes).ToList());
global.AvailableHeroes = heroes.Select(h => h.Key).ToList();
Check(mode.LobbyMode is LobbyModeDraftPick { SelectionTime: 20, WaitTime: 10 } && mode.HeroLimit == null, "casual registration");
Check(ranked.LobbyMode is LobbyModeDraftPick { SelectionTime: 42 } && custom.LobbyMode is LobbyModeFreePick, "other modes unchanged");

var ballot = new CasualDraft();
ballot.Begin(0, heroes.Take(3).Select(h => h.Key), 1000);
Check(!ballot.Vote(1, 1, heroes[0].Key, 1001), "wrong phase rejected");
Check(!ballot.Vote(1, 0, heroes[7].Key, 1001), "wrong class rejected");
Check(ballot.Vote(1, 0, heroes[0].Key, 1001), "vote accepted");
Check(!ballot.Vote(1, 0, heroes[1].Key, 1002), "repeat vote rejected");
Check(!ballot.Vote(2, 0, heroes[1].Key, 16000), "deadline enforced");
ballot.Vote(2, 0, heroes[1].Key, 1002);
var outcomes = Enumerable.Range(0, 100).Select(i => ballot.Winner(new Random(i))).ToHashSet();
Check(outcomes.SetEquals(heroes.Take(2).Select(h => h.Key)), "ties choose only tied leaders and can choose either");
ballot.Begin(1, heroes.Skip(7).Take(3).Select(h => h.Key), 20000);
Check(ballot.Votes.Count == 0 && Enumerable.Range(0, 100).Select(i => ballot.Winner(new Random(i))).Distinct().Count() == 3,
    "no-vote timeout randomly selects any candidate");

var entrants = Enumerable.Range(1, 10).Select(i => new RankedSnakeDraft.Player((uint)i, i <= 5 ? TeamType.Team1 : TeamType.Team2, i * 1000)).ToArray();
var orders = new HashSet<string>(); var firsts = new HashSet<uint>();
for (int seed = 0; seed < 100; seed++)
{
    var order = CasualDraft.Order(entrants, new Random(seed)); orders.Add(string.Join(",", order)); firsts.Add(order[0]);
    var sides = order.Select(id => id <= 5 ? 'A' : 'B').ToArray();
    if (sides[0] == 'B') sides = sides.Select(c => c == 'A' ? 'B' : 'A').ToArray();
    Check(new string(sides) == "ABBAABBAAB" && order.Distinct().Count() == 10, "random order retains snake and all players");
    Check(order.SequenceEqual(CasualDraft.Order(entrants.Select(p => p with { Mmr = -p.Mmr }), new Random(seed))), "order independent of MMR");
}
Check(orders.Count > 90 && firsts.Count == 10, "every seat can pick first");

foreach (int size in new[] { 3, 4, 5 })
{
    var roster = Enumerable.Range(1, size * 2).Select(i => new RankedSnakeDraft.Player((uint)i,
        i <= size ? TeamType.Team1 : TeamType.Team2, i)).ToArray();
    var last = roster.ToDictionary(p => p.Id, _ => heroes[0].Key); last[99] = heroes[0].Key;
    var sent = new List<LobbyUpdate>();
    var instance = DispatchProxy.Create<IGameInstance, GameProxy>();
    var lobby = new GameLobby(RecordingLobby.Create(sent), instance, "casual-fixture", Key.None, mode.Key, []);
    Set(lobby, "_playerDatabase", FixturePlayers.Create(last));
    lobby.InitializeDraftRoster(roster);
    var data = (LobbyData)Field(lobby, "<LobbyData>k__BackingField")!;
    foreach (var p in roster) lobby.AddPlayer(p.Id, p.Team, null);
    var mapA = new Key("fixture_map_a"); var mapB = new Key("fixture_map_b");
    data.Maps.Add(new LobbyMapData {Info=new MapInfoCard{MapKey=mapA},PlayerVotes=[]});
    data.Maps.Add(new LobbyMapData {Info=new MapInfoCard{MapKey=mapB},PlayerVotes=[]});
    foreach (var phase in new[]{LobbyTimerType.Wait,LobbyTimerType.Selection,LobbyTimerType.Start})
    {
        data.Timer.TimerType=phase;
        lobby.VoteForMap(1,mapA); lobby.VoteForMap(2,mapA);
        lobby.VoteForMap(1,mapB);
        Check(data.Maps[0].PlayerVotes!.SequenceEqual(new uint[]{2}) && data.Maps[1].PlayerVotes!.SequenceEqual(new uint[]{1}),"vote transfers exactly once during "+phase);
        var snapshot=sent.Last(m=>m.Maps!=null).Maps!;
        Check(snapshot[0].PlayerVotes!.SequenceEqual(new uint[]{2}) && snapshot[1].PlayerVotes!.SequenceEqual(new uint[]{1}),"changed vote broadcast shows updated totals");
        int sentCount=sent.Count;
        lobby.VoteForMap(1,mapB); Check(sent.Count==sentCount && data.Maps[1].PlayerVotes!.Count==1,"same-map click is idempotent");
        lobby.VoteForMap(1,new Key("invalid_map")); lobby.VoteForMap(999,mapB);
        Check(data.Maps[1].PlayerVotes!.SequenceEqual(new uint[]{1}),"invalid map and spectator requests preserve valid votes");
        lobby.VoteForMap(1,mapA);
        Check(data.Maps[0].PlayerVotes!.Count==2 && data.Maps[1].PlayerVotes!.Count==0 && snapshot[1].PlayerVotes!.Count==1,"switch back works and prior broadcast snapshot remains immutable");
    }
    data.Timer.TimerType=LobbyTimerType.Requeue;lobby.VoteForMap(1,mapB);
    Check(data.Maps[0].PlayerVotes!.Contains(1) && data.Maps[1].PlayerVotes!.Count==0,"closed map ballot rejects changes");
    data.Timer.TimerType=LobbyTimerType.Wait;
    Expire(lobby, "OnWaitTimerElapsed");
    var draft = (CasualDraft)Field(lobby, "_casualDraft")!;
    Check(draft.Phase == 0 && data.Timer.EndTime - data.Timer.StartTime == 15000, "first 15-second phase");
    for (int phase = 0; phase < 3; phase++)
    {
        Check(draft.Candidates.All(h => ((UnitDataPlayer)h.GetCard<CardUnit>()!.Data!).Class == CasualDraft.Classes[phase]), "class-scoped ballot");
        var oldTimer = ((LobbyTimerType, System.Timers.Timer))Field(lobby, "_currentTimer")!;
        var candidate = draft.Candidates[0];
        lobby.VoteHeroBan(777, phase, candidate);
        Check(draft.Votes.Count == 0, "outsider vote rejected");
        if (phase == 1)
        {
            // Disconnect preserves the seat and existing vote; timeout handles missing voters.
            lobby.VoteHeroBan(1, phase, candidate);
            lobby.PlayerLeft(1, null);
            Check(draft.Votes.ContainsKey(1) && data.Players.ContainsKey(1), "disconnect preserves ballot and seat");
            Expire(lobby, "OnWaitTimerElapsed");
            lobby.AddPlayer(1, TeamType.Team1, null);
        }
        else foreach (var p in roster) lobby.VoteHeroBan(p.Id, phase, candidate);
        Check(draft.Phase == phase + 1 && lobby.BannedHeroes.Contains(candidate), "votes or timer advance exactly one phase");
        int next = draft.Phase;
        Expire(lobby, "OnWaitTimerElapsed", oldTimer.Item2);
        Check(draft.Phase == next, "stale ban timer cannot skip phase");
    }
    Check(lobby.BannedHeroes.Count == 3 && data.Timer.TimerType == LobbyTimerType.Selection, "three global bans then picking");
    var expected = (uint[])Field(lobby, "_draftOrder")!;
    var teamTurns = new Dictionary<TeamType, int>();
    for (int turn = 0; turn < size * 2; turn++)
    {
        var player = data.Players.Values.Single(p => p.CanLoadout && !p.Ready);
        Check(player.PlayerId == expected[turn], "randomized snake order used by lobby");
        int teamTurn = teamTurns.GetValueOrDefault(player.Team);
        teamTurns[player.Team] = teamTurn + 1;
        int classIndex = size < 4 || teamTurn < size - 2 ? 0 : teamTurn == size - 2 ? 1 : 2;
        var repeatedHero = heroes.First(h => ((UnitDataPlayer)h.Data!).Class == CasualDraft.Classes[classIndex] && !lobby.BannedHeroes.Contains(h.Key)).Key;
        Check(!player.RestrictedHeroes!.Contains(repeatedHero), "repeat pick remains selectable on either team");
        lobby.SwapHero(player.PlayerId, repeatedHero);
        Check(player.Hero == repeatedHero, "same-team and opposing-team duplicate accepted by actual lobby");
        var selected = player.Hero;
        lobby.SwapHero(player.PlayerId, lobby.BannedHeroes.First());
        Check(player.Hero == selected, "ban enforced at pick");
        var waiting = data.Players.Values.FirstOrDefault(p => p.Hero == Key.None);
        if (waiting != null) { lobby.PlayerReady(waiting.PlayerId); Check(!waiting.Ready, "out of turn cannot lock"); }
        if (turn % 2 == 0) lobby.PlayerReady(player.PlayerId); else Expire(lobby, "OnSelectionTimerElapsed");
        Check(player.Ready && !player.CanLoadout, "manual and timeout lock");
    }
    Check(data.Timer.TimerType == LobbyTimerType.Start && data.Players.Values.All(p => p.Ready && p.Hero != Key.None), "complete roster permits duplicate heroes");
    foreach (var team in data.Players.Values.GroupBy(p => p.Team))
    {
        var classes = team.GroupBy(p => ((UnitDataPlayer)p.Hero.GetCard<CardUnit>()!.Data!).Class).ToArray();
        Check(team.Count(p => p.Hero == team.First().Hero) >= 1, "locked picks retained");
        if (size == 5) Check(classes.Any(g => g.Count() == 3), "three of the same class allowed when other classes covered");
        if (size >= 4) Check(classes.Length == 3, "all three classes on each large team");
    }
    StopTimer(lobby); data.Timer.TimerType = LobbyTimerType.Requeue;
    lobby.PlayerLeft(1, null); lobby.AddPlayer(99, TeamType.Team1, null);
    var replacement = data.Players[99];
    Check(replacement.Hero != Key.None && !lobby.BannedHeroes.Contains(replacement.Hero) && replacement.CanLoadout, "backfill receives legal default");
    var old = replacement.Hero; lobby.SwapHero(99, lobby.BannedHeroes.First());
    Check(replacement.Hero == old, "backfill cannot bypass bans");
    replacement.Ready = true;
    var canPick = typeof(GameLobby).GetMethod("CanPickHero", Any)!;
    var alternate = heroes.First(h => h.Key != replacement.Hero).Key;
    Check(!(bool)canPick.Invoke(lobby, [replacement, alternate])!, "backfill lock rejects queued hero changes before zone transfer");
    replacement.Ready = false;
    lobby.PlayerReady(99);
    var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); lobby.EnqueueAction(() => drained.SetResult()); await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(replacement.Ready && !replacement.CanLoadout, "backfill lock enters match");
    var payload = lobby.GetLobbyUpdate(99);
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
    payload.Write(writer); stream.Position = 0;
    var decoded = LobbyUpdate.ReadRecord(new BinaryReader(stream));
    Check(decoded.CasualDraft == payload.CasualDraft && stream.Position == stream.Length, "wire state roundtrip including reconnect");
    using var state = JsonDocument.Parse(decoded.CasualDraft!);
    Check(state.RootElement.GetProperty("banned").GetArrayLength() == 3, "reconnect snapshot retains bans");
    lobby.Stop();
}

// A last seat must cover the missing class, even if its last-played hero would normally be allowed.
var pool = heroes.Select(h => new CasualDraft.Hero(h.Key, ((UnitDataPlayer)h.Data!).Class)).ToArray();
var seats = new[] { new CasualDraft.Seat(1, TeamType.Team1, heroes[0].Key), new CasualDraft.Seat(2, TeamType.Team1, heroes[1].Key),
    new CasualDraft.Seat(3, TeamType.Team1, heroes[7].Key), new CasualDraft.Seat(4, TeamType.Team1, heroes[8].Key) };
Check(!CasualDraft.CanComplete(seats, pool, (_, _) => true, new HashSet<TeamType>{TeamType.Team1}), "missing Brain rejected");
Check(CasualDraft.CanComplete(seats.Select(s => s.Id == 4 ? s with { Hero = heroes[14].Key } : s).ToArray(), pool,
    (_, _) => true, new HashSet<TeamType>{TeamType.Team1}), "Brain completes legal team");
Check(CasualDraft.CanComplete(seats.Take(3).ToArray(), pool, (_, _) => true, new HashSet<TeamType>()), "small matches need no class minimum");
// Compare the public-pool capacity path with exact ownership search on identical usable heroes.
var unowned = new CasualDraft.Hero(new Key("fixture_unowned"), CasualDraft.Classes[0]);
var minimum = new HashSet<TeamType> { TeamType.Team1, TeamType.Team2 };
for (int seed = 0; seed < 80; seed++)
{
    var rng = new Random(seed);
    var proposed = entrants.Select(p => new CasualDraft.Seat(p.Id, p.Team,
        rng.Next(3) == 0 ? Key.None : heroes[rng.Next(heroes.Length)].Key)).ToArray();
    bool fast = CasualDraft.CanComplete(proposed, pool, (_, _) => true, minimum);
    bool exact = CasualDraft.CanComplete(proposed, pool.Append(unowned).ToArray(), (_, h) => h != unowned.Key, minimum);
    Check(fast == exact, "public pool and ownership solver agree " + seed);
}
// Brains can repeat across teams; picks never consume shared hero supply.
var scarce = pool.Where(h => h.Class != CasualDraft.Classes[2] || h.Key == heroes[14].Key || h.Key == heroes[15].Key).ToArray();
var reserved = entrants.Select(p => new CasualDraft.Seat(p.Id, p.Team,
    p.Id == 1 ? heroes[14].Key : p.Id == 2 ? heroes[15].Key : Key.None)).ToArray();
Check(CasualDraft.CanComplete(reserved, scarce, (_, _) => true, minimum), "both teams may reuse the remaining Brain heroes");
var onePerClass = new[] {pool[0],pool[7],pool[14]};
Check(CasualDraft.CanComplete(entrants.Select(p=>new CasualDraft.Seat(p.Id,p.Team,Key.None)).ToArray(), onePerClass, (_,_)=>true, minimum), "only three available heroes can fill both five-player teams");
var needsTwo = new[] { new CasualDraft.Seat(1,TeamType.Team1,heroes[0].Key),new CasualDraft.Seat(2,TeamType.Team1,heroes[0].Key),new CasualDraft.Seat(3,TeamType.Team1,Key.None),new CasualDraft.Seat(4,TeamType.Team1,Key.None) };
Check(!CasualDraft.CanComplete(needsTwo,onePerClass,(id,h)=>id==3 || h==heroes[0].Key,new HashSet<TeamType>{TeamType.Team1}),"one remaining player cannot fill two missing classes");
Check(CasualDraft.CanComplete(needsTwo,onePerClass,(id,h)=>id==3 ? h==heroes[7].Key : id==4 ? h==heroes[14].Key : true,new HashSet<TeamType>{TeamType.Team1}),"distinct remaining owners can complete missing classes");
Check(!CasualDraft.CanComplete(needsTwo,onePerClass,(id,h)=>id!=4,new HashSet<TeamType>{TeamType.Team1}),"unowned choices cannot fill a seat");
Console.WriteLine($"Casual draft fixture passed: {checks} checks.");

class GameProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType == typeof(bool) ? false : null;
}
