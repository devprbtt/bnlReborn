// Public queue rules after Casual and Ranked were separated:
// - the automatic Casual pool pops 5v5 Casual (free pick) at ten players, under the 4v4 Casual card;
// - that match keeps a five-a-side roster and backfills its own vacancies;
// - its confirmation names a five-a-side card so the prompt shows ten circles;
// - Ranked queues on its own, never backfills, and never kicks an idle player;
// - a Ranked draft turn never preselects a hero the team's limit forbids, and refused picks are rejected.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using Moserware.Skills;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

var checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}

void Field(object target, string name, object? value)
{
    var type = target.GetType();
    FieldInfo? field = null;
    while (type != null && (field = type.GetField(name, Any)) == null) type = type.BaseType;
    (field ?? throw new MissingFieldException(name)).SetValue(target, value);
}

// ---- Catalogue: three public modes, a hero roster in three classes and one private hero nobody owns.
var catalogue = (ServerCatalogue)Databases.Catalogue;
var friendly = new CardGameMode
{
    Id = "game_mode_friendly", Ranking = GameRankingType.Friendly, PlayersPerTeam = 4,
    LobbyMode = new LobbyModeFreePick(), Backfilling = new BackfillingLogic(),
    AntiAfk = new AfkLogic { KickFromMatch = true, AfkPunishSeconds = 180 }
};
var ranked = new CardGameMode
{
    Id = "game_mode_ranked", Ranking = GameRankingType.Ranked, PlayersPerTeam = 5,
    LobbyMode = new LobbyModeDraftPick(), Backfilling = null,
    HeroLimit = new LobbyHeroLimit { LimitOption = LobbyHeroLimitOption.PerClass, Limit = 2 },
    AntiAfk = new AfkLogic { KickFromMatch = false, AfkPunishSeconds = 180 }
};
var custom = new CardGameMode { Id = "game_mode_custom", Ranking = GameRankingType.None, PlayersPerTeam = 5 };
var graveyard = new CardGameMode { Id = "game_mode_graveyard", Ranking = GameRankingType.None, PlayersPerTeam = 3 };
var attack = new Key("fixture_class_attack");
var defense = new Key("fixture_class_defense");
var support = new Key("fixture_class_support");
CardUnit Hero(string id, Key heroClass, ScopeType scope = ScopeType.Public) =>
    new() { Id = id, Scope = scope, Data = new UnitDataPlayer { Class = heroClass } };
var a1 = Hero("fixture_hero_a1", attack);
var a2 = Hero("fixture_hero_a2", attack);
var a3 = Hero("fixture_hero_a3", attack);
var d1 = Hero("fixture_hero_d1", defense);
var d2 = Hero("fixture_hero_d2", defense);
var s1 = Hero("fixture_hero_s1", support);
var sPrivate = Hero("fixture_hero_s_private", support, ScopeType.Private);
var globalLogic = new CardGlobalLogic { Id = "global_logic" };
catalogue.Replicate([friendly, ranked, custom, graveyard, a1, a2, a3, d1, d2, s1, sPrivate, globalLogic]);
// Keys exist only after Replicate.
globalLogic.AvailableHeroes = [a1.Key, a2.Key, a3.Key, d1.Key, d2.Key, sPrivate.Key, s1.Key];
globalLogic.Matchmaker = new MatchmakerLogic { GameModesForQueues = [friendly.Key, ranked.Key, graveyard.Key] };

// ---- Automatic Casual pool: ten players pop 5v5 Casual on the Casual card.
Check(AutomaticPublicQueuePolicy.IsAutomaticQueue(friendly) && !AutomaticPublicQueuePolicy.IsAutomaticQueue(ranked),
    "only Casual uses the automatic pool; Ranked queues on its own");
Check(AutomaticPublicQueuePolicy.PlayersPerTeam(AutomaticPublicQueueAction.StartLargeCasual) == 5 &&
      AutomaticPublicQueuePolicy.PlayersPerTeam(AutomaticPublicQueueAction.StartCasual) == 4,
    "large Casual pops five a side, grace-period Casual four a side");

var normalize = typeof(Matchmaker).GetMethod("NormalizeQueueKey", Any)!;
Key Normalize(Key key) => (Key)normalize.Invoke(null, [key])!;
Check(Normalize(friendly.Key) == friendly.Key && Normalize(ranked.Key) == ranked.Key && Normalize(graveyard.Key) == graveyard.Key,
    "Casual, Ranked and other modes each keep their own queue");

// ---- Confirmation prompt size: a 5v5 Casual pop names a five-a-side card, a 4v4 pop keeps Casual.
var displayMode = typeof(Matchmaker).GetMethod("AcceptanceDisplayMode", Any)!;
Key Display(CardGameMode mode, int perTeam) => (Key)displayMode.Invoke(null, [mode, perTeam])!;
Check(Display(friendly, 4) == friendly.Key, "4v4 Casual confirmation keeps the Casual card (8 circles)");
Check(Display(friendly, 5).GetCard<CardGameMode>()?.PlayersPerTeam == 5, "5v5 Casual confirmation names a five-a-side card (10 circles)");
Check(Display(friendly, 5).GetCard<CardGameMode>()?.Ranking != GameRankingType.Ranked, "5v5 Casual confirmation does not name the Ranked card");
Check(Display(ranked, 5) == ranked.Key, "Ranked confirmation keeps the Ranked card");

// ---- 5v5 Casual roster under the 4v4 Casual card.
PlayerQueueData Queued(uint id) => new(id, Guid.NewGuid(), new Rating(25, 25d / 3d), DateTimeOffset.UtcNow, null, friendly.Key);
var team1 = Enumerable.Range(1, 5).Select(i => Queued((uint)i)).ToList();
var team2 = Enumerable.Range(6, 5).Select(i => Queued((uint)i)).ToList();
var large = new MatchmakerInitiator(friendly, team1, team2, 5);
Check(large.PlayersPerTeam == 5 && large.MaxPlayers == 10, "5v5 Casual match is sized five a side");
Check(new MatchmakerInitiator(friendly, team1.Take(4).ToList(), team2.Take(4).ToList()).PlayersPerTeam == 4,
    "4v4 Casual match still takes the card's size");
large.SetBackfillReady(true);
Check(!large.NeedsBackfill(), "full 5v5 needs no backfill");
large.RemovePlayer(1);
Field(large, "_firstSlotFreed", DateTimeOffset.Now.AddSeconds(-11));
Check(large.NeedsBackfill(), "5v5 Casual with four on a team needs backfill (the 4v4 card would call it full)");
Check(large.AddPlayer(Queued(20), TeamType.Team1), "backfiller takes the fifth slot");
Check(!large.AddPlayer(Queued(21), TeamType.Team1), "a sixth player is refused");

// ---- Backfill balance sizes the vacancy from the running match.
var queueType = typeof(Matchmaker).GetNestedType("QueueData", Any)!;
var infoType = typeof(Matchmaker).GetNestedType("BackfillInfo", Any)!;
var backfillBalance = typeof(Matchmaker).GetMethod("DoBackfillBalance", Any)!;
(PlayerQueueData?, PlayerQueueData?) Backfill(int matchPerTeam)
{
    var queue = RuntimeHelpers.GetUninitializedObject(queueType);
    var candidate = Queued(30);
    Field(queue, "<GameModeKey>k__BackingField", friendly.Key);
    Field(queue, "<Players>k__BackingField", new List<PlayerQueueData> { candidate });
    var doBackfilling = new ConcurrentDictionary<uint, bool>();
    doBackfilling[candidate.PlayerId] = true;
    Field(queue, "<DoBackfilling>k__BackingField", doBackfilling);
    var ratings = new Func<IEnumerable<int>, Dictionary<uint, Rating>>(ids =>
        ids.ToDictionary(id => (uint)id, _ => new Rating(25, 25d / 3d)));
    var info = Activator.CreateInstance(infoType, ratings(Enumerable.Range(2, 4)), ratings(Enumerable.Range(6, 5)),
        "fixture-instance", new HashSet<uint>(), matchPerTeam)!;
    var result = backfillBalance.Invoke(null, [queue, info])!;
    var tuple = (System.Runtime.CompilerServices.ITuple)result;
    return ((PlayerQueueData?)tuple[0], (PlayerQueueData?)tuple[1]);
}
var (fill1, fill2) = Backfill(5);
Check(fill1?.PlayerId == 30 && fill2 == null, "4-vs-5 Casual match backfills the short team");
var (old1, old2) = Backfill(4);
Check(old1 == null && old2 == null, "sizing from the 4v4 card would have left that vacancy empty (ablation)");

// ---- Squad caps: Casual is solo (max_players_in_squad 1), Ranked allows a full five.
friendly.MaxPlayersInSquad = 1;
ranked.MaxPlayersInSquad = 5;
Check(CatalogueHelper.SquadFitsMode(1, friendly.Key) && !CatalogueHelper.SquadFitsMode(2, friendly.Key),
    "a duo formed before the Casual cap dropped to 1 cannot queue Casual");
Check(CatalogueHelper.SquadFitsMode(5, ranked.Key) && !CatalogueHelper.SquadFitsMode(6, ranked.Key),
    "Ranked squads of up to five can queue");

// ---- AFK: Casual kicks, Ranked only warns.
Check(GameZone.ShouldKickForAfk(friendly.AntiAfk, 181), "Casual kicks an idle player after the punish time");
Check(!GameZone.ShouldKickForAfk(friendly.AntiAfk, 179), "Casual does not kick before the punish time");
Check(!GameZone.ShouldKickForAfk(ranked.AntiAfk, 10_000), "Ranked never kicks an idle player");
Check(!GameZone.ShouldKickForAfk(null, 10_000), "modes without anti-AFK rules never kick");

// ---- Ranked draft.
var lastPlayed = new Dictionary<uint, Key>();
var sent = new List<LobbyUpdate>();
var lobby = (GameLobby)RuntimeHelpers.GetUninitializedObject(typeof(GameLobby));
var data = new LobbyData { IsDataExist = true, GameModeKey = ranked.Key };
data.Timer = new LobbyTimer { TimerType = LobbyTimerType.Selection };
Field(lobby, "<LobbyData>k__BackingField", data);
Field(lobby, "_playerDatabase", FixturePlayers.Create(lastPlayed));
Field(lobby, "_serviceLobby", RecordingLobby.Create(sent));
PlayerLobbyState Seat(uint id, TeamType team, CardUnit? hero, bool ready)
{
    var state = new PlayerLobbyState
    {
        PlayerId = id, Team = team, Hero = hero?.Key ?? Key.None, Ready = ready, CanLoadout = hero != null,
        Devices = new Dictionary<int, Key>(), Perks = [], RestrictedHeroes = []
    };
    data.Players[id] = state;
    return state;
}
Seat(101, TeamType.Team1, a1, true);
Seat(102, TeamType.Team1, a2, true);
Seat(103, TeamType.Team1, d1, true);
var turn = Seat(104, TeamType.Team1, null, false);
var waiting = Seat(105, TeamType.Team1, null, false);
var enemy = Seat(201, TeamType.Team2, null, false);
lastPlayed[104] = a3.Key; // attack is full on team 1
lastPlayed[105] = d2.Key;
lastPlayed[201] = a1.Key;

var startTurns = typeof(GameLobby).GetMethod("StartNextDraftTurns", Any)!;
startTurns.Invoke(lobby, [new List<PlayerLobbyState> { turn }]);
Check(turn.Hero != a3.Key, "a full class is not preselected for the next player");
Check(turn.Hero == s1.Key, "the default moves to an owned hero of the emptiest open class (support)");
Check(turn.CanLoadout && turn.RestrictedHeroes!.ToHashSet().SetEquals([a1.Key, a2.Key, a3.Key]),
    "the turn player is told every attack hero is restricted");
startTurns.Invoke(lobby, [new List<PlayerLobbyState> { enemy }]);
Check(enemy.Hero == a1.Key && enemy.RestrictedHeroes!.Count == 0,
    "limits are per team: the other team keeps its last-played hero");

sent.Clear();
lobby.SwapHero(104, a2.Key);
Check(turn.Hero == s1.Key && sent.Count == 1, "picking a restricted hero is refused and the roster resent");
lobby.SwapHero(104, sPrivate.Key);
Check(turn.Hero == s1.Key, "picking an unowned hero is refused");
lobby.SwapHero(104, d2.Key);
Check(turn.Hero == d2.Key, "picking an open hero (second defense) is accepted");
lobby.SwapHero(105, s1.Key);
Check(waiting.Hero == Key.None, "a player whose turn has not come cannot pick");
turn.Ready = true;
lobby.SwapHero(104, s1.Key);
Check(turn.Hero == d2.Key, "a locked-in pick cannot be changed");

ranked.HeroLimit = new LobbyHeroLimit { LimitOption = LobbyHeroLimitOption.PerHero, Limit = 1 };
var restrictedFor = typeof(GameLobby).GetMethod("RestrictedHeroesFor", Any)!;
var perHero = (List<Key>)restrictedFor.Invoke(lobby, [waiting])!;
Check(perHero.ToHashSet().SetEquals([a1.Key, a2.Key, d1.Key, d2.Key]), "per-hero limit restricts exactly the team's picks");

Console.WriteLine($"Public queue rules fixture passed: {checks} checks.");

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
