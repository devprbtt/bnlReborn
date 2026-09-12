using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;
using Moserware.Skills;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

PlayerQueueData Player(uint id) => new(id, Guid.NewGuid(), new Rating(25, 25d / 3d),
    DateTimeOffset.UtcNow, null);

var initialTeam1 = Player(1);
var initialTeam2 = Player(2);
var initiator = new MatchmakerInitiator(new CardGameMode { Id = "fixture_backfill_reentry" },
    [initialTeam1], [initialTeam2], 2);

Check(initiator.HasParticipated(initialTeam1.PlayerId), "initial player is recorded in match history");
Check(initiator.GetTeamForPlayer(initialTeam1.PlayerId) == TeamType.Team1,
    "connected player retains the existing slot used by reconnect");

initiator.RemovePlayer(initialTeam1.PlayerId);
Check(initiator.HasParticipated(initialTeam1.PlayerId), "quitter remains in match history after slot removal");
Check(!initiator.AddPlayer(initialTeam1, TeamType.Team1), "quitter cannot backfill the same match");

var firstBackfill = Player(3);
Check(initiator.AddPlayer(firstBackfill, TeamType.Team1), "new player can fill the open slot");
initiator.RemovePlayer(firstBackfill.PlayerId);
Check(initiator.HasParticipated(firstBackfill.PlayerId), "departed backfill remains in match history");
Check(!initiator.AddPlayer(firstBackfill, TeamType.Team1), "departed backfill cannot reenter the same match");

var secondBackfill = Player(4);
Check(initiator.AddPlayer(secondBackfill, TeamType.Team1), "different player remains eligible for backfill");

var snapshot = initiator.GetParticipantHistory();
snapshot.Clear();
Check(initiator.HasParticipated(initialTeam2.PlayerId), "match history snapshots cannot mutate server state");

Console.WriteLine("Backfill reentry suite passed.");
