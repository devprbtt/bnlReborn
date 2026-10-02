using BNLReloadedServer.Clans;
using SQLite;

var checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}

var offensive = new WordList("badword");
var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
var file = Path.Combine(Path.GetTempPath(), "bnl-clan-fixture-" + Guid.NewGuid().ToString("N") + ".db");
var db = new SQLiteAsyncConnection(file);
var store = new ClanStore(db, offensive, () => now);
await store.Load();

// ---- Rules ----
Check(ClanRules.ValidateName("Yeti Squad", offensive) == ClanResult.Ok, "a normal clan name is accepted");
Check(ClanRules.ValidateName("ab", offensive) == ClanResult.InvalidName, "names shorter than 3 are rejected");
Check(ClanRules.ValidateName(new string('a', 25), offensive) == ClanResult.InvalidName, "names longer than 24 are rejected");
Check(ClanRules.ValidateName("Bad<b>Name", offensive) == ClanResult.InvalidName, "markup characters are rejected in names");
Check(ClanRules.ValidateTag("YETI-10_ok", offensive) == ClanResult.Ok, "a 10-character tag is accepted");
Check(ClanRules.ValidateTag("ELEVENCHARS", offensive) == ClanResult.InvalidTag, "an 11-character tag is rejected");
Check(ClanRules.ValidateTag("A", offensive) == ClanResult.InvalidTag, "a 1-character tag is rejected");
Check(ClanRules.ValidateTag("YE TI", offensive) == ClanResult.InvalidTag, "spaces are rejected in tags");
Check(ClanRules.ValidateName("my badword clan", offensive) == ClanResult.Offensive, "offensive names are rejected");
Check(ClanRules.CanKick(ClanRank.Officer, ClanRank.Member) && !ClanRules.CanKick(ClanRank.Officer, ClanRank.Officer)
      && !ClanRules.CanKick(ClanRank.Member, ClanRank.Member) && ClanRules.CanKick(ClanRank.Leader, ClanRank.Officer),
    "officers kick members only; leaders kick anyone else");
Check(!ClanRules.CanSetRank(ClanRank.Officer, ClanRank.Member, ClanRank.Officer)
      && !ClanRules.CanSetRank(ClanRank.Leader, ClanRank.Member, ClanRank.Leader),
    "only the leader changes ranks, and never to leader");
Check(ClanRules.Successor(new[] { (1u, ClanRank.Leader, now), (2u, ClanRank.Member, now.AddDays(-9)),
    (3u, ClanRank.Officer, now.AddDays(-1)), (4u, ClanRank.Officer, now.AddDays(-5)) }, 1) == 4,
    "the longest-serving officer succeeds the leader");
Check(ClanRules.Successor(new[] { (1u, ClanRank.Leader, now), (7u, ClanRank.Member, now.AddDays(-2)),
    (5u, ClanRank.Member, now.AddDays(-2)) }, 1) == 5, "without officers, the longest-serving member succeeds (ties by id)");

// ---- Create and uniqueness ----
var (r, yeti) = await store.Create(1, "Yeti Squad", "YETI");
Check(r == ClanResult.Ok && store.ClanOf(1)?.Members.Single().Rank == ClanRank.Leader, "the creator leads the new clan");
Check((await store.Create(2, "yeti   SQUAD", "OTHER")).Result == ClanResult.NameTaken, "names are unique ignoring case and spacing");
Check((await store.Create(2, "Other Clan", "yeti")).Result == ClanResult.TagTaken, "tags are unique ignoring case");
Check((await store.Create(1, "Second Clan", "TWO")).Result == ClanResult.AlreadyInClan, "a player can found only one clan");

// ---- Invites ----
Check(await store.Invite(2, 3) == ClanResult.NotInClan, "players outside a clan cannot invite");
Check(await store.Invite(1, 2) == ClanResult.Ok && store.InvitesFor(2).Length == 1, "the leader invites a player");
Check(await store.Invite(1, 2) == ClanResult.AlreadyInvited, "a second invite to the same player is refused");
var (_, other) = await store.Create(9, "Other Clan", "OTHR");
Check(await store.Invite(9, 2) == ClanResult.Ok && store.InvitesFor(2).Length == 2, "a player can hold invites from several clans");
Check(await store.Accept(2, yeti) == ClanResult.Ok && store.InvitesFor(2).Length == 0, "joining a clan clears the player's other invites");
Check(await store.Accept(2, other) == ClanResult.NoSuchInvite, "the voided invite can no longer be accepted");
Check(await store.Invite(2, 3) == ClanResult.NotAllowed, "members cannot invite");
Check(await store.Invite(1, 9) == ClanResult.AlreadyInClan, "players already in a clan cannot be invited");
Check(await store.Decline(4, yeti) == ClanResult.NoSuchInvite, "declining a non-existent invite fails");

// ---- Ranks, kicks ----
Check(await store.SetRank(1, 2, ClanRank.Officer) == ClanResult.Ok, "the leader promotes a member to officer");
Check(await store.Invite(2, 3) == ClanResult.Ok && await store.Accept(3, yeti) == ClanResult.Ok, "officers can invite");
Check(await store.Kick(2, 1) == ClanResult.NotAllowed, "officers cannot kick the leader");
Check(await store.Kick(2, 3) == ClanResult.Ok && store.ClanOf(3) == null, "officers kick members");
Check(await store.Kick(1, 9) == ClanResult.TargetNotInClan, "kicking someone from another clan fails");

// ---- Invite expiry ----
Check(await store.Invite(1, 5) == ClanResult.Ok, "invite for expiry test");
now = now.AddDays(7);
Check(await store.Accept(5, yeti) == ClanResult.NoSuchInvite, "invites expire after 7 days");
Check(await store.Invite(1, 5) == ClanResult.Ok, "an expired invite can be re-sent");

// ---- Size cap and the race for the last slot ----
for (uint p = 100; p < 117; p++) { await store.Invite(1, p); await store.Accept(p, yeti); }
Check(store.ClanOf(1)!.Members.Length == 19, "clan filled to 19 members");
for (uint p = 200; p < 225; p++) await store.Invite(1, p);
var racers = Enumerable.Range(200, 25).Select(p => store.Accept((uint)p, yeti)).ToArray();
var outcomes = await Task.WhenAll(racers);
Check(outcomes.Count(o => o == ClanResult.Ok) == 1 && outcomes.Count(o => o == ClanResult.ClanFull) == 24,
    "25 simultaneous accepts fill exactly the last slot");
Check(store.ClanOf(1)!.Members.Length == ClanRules.MaxMembers, "the clan holds exactly 20 members");
Check(await store.Invite(1, 300) == ClanResult.ClanFull, "a full clan cannot invite");

// ---- Leaving, transfer, rename, disband ----
Check(await store.Leave(1) == ClanResult.LeaderMustTransfer, "a leader with members must transfer before leaving");
Check(await store.TransferLeadership(1, 2) == ClanResult.Ok && store.ClanOf(2)!.LeaderId == 2
      && store.ClanOf(1)!.Members.First(m => m.PlayerId == 1).Rank == ClanRank.Officer,
    "leadership transfers and the old leader becomes an officer");
Check(await store.Leave(1) == ClanResult.Ok && store.ClanOf(1) == null, "the former leader can now leave");
Check(await store.Rename(2, "Yeti Legion", "LEGION") == ClanResult.Ok && store.TagOf(2) == "LEGION", "the leader renames the clan");
Check(await store.Rename(2, "Yeti Again", "AGAIN") == ClanResult.RenameCooldown, "renames are limited to once per 7 days");
Check((await store.Create(50, "New Clan", "YETI")).Result == ClanResult.Ok, "the old tag is free after a rename");

// ---- Inactive leader ----
Func<uint, DateTimeOffset?> lastOnline = id => id == 2 ? now.AddDays(-31) : now;
Check(await store.TransferInactiveLeaders(lastOnline, _ => false) == 1 && store.ClanOf(2)!.LeaderId != 2,
    "a leader offline for 31 days hands over leadership");
Check(await store.TransferInactiveLeaders(id => now.AddDays(-31), id => true) == 0, "online leaders are never replaced");

// ---- Persistence across a restart ----
var before = store.ClanOf(100)!;
var reopened = new ClanStore(new SQLiteAsyncConnection(file), offensive, () => now);
await reopened.Load();
var after = reopened.ClanOf(100)!;
Check(after.Tag == before.Tag && after.LeaderId == before.LeaderId && after.Members.Length == before.Members.Length,
    "clans, ranks and leadership survive a restart");
Check(reopened.InvitesFor(300).Length == 0 && reopened.TagOf(50) == "YETI", "invites and other clans reload exactly");

// ---- Disband ----
var leader = reopened.ClanOf(100)!.LeaderId;
Check(await reopened.Disband(100u == leader ? 101u : 100u) == ClanResult.NotAllowed, "only the leader disbands");
Check(await reopened.Disband(leader) == ClanResult.Ok && reopened.ClanOf(100) == null && reopened.ClanByTag("LEGION") == null,
    "disbanding removes the clan and frees every member");
Check(await reopened.Leave(50) == ClanResult.Ok && reopened.ClanByTag("YETI") == null, "a leader alone disbands by leaving");

await db.CloseAsync();

#if CLIENT_PROTOCOL
// ---- Wire format: the server encoder against the client's own decoder (BNL.Clans.ClanProtocol) ----
var sent = new ClanStateMessage(
    new ClanStateClan(7, "Yeti Squad", "YETI", 1, ClanRank.Officer, now,
        [new ClanStateMember(1, "Prbtt", ClanRank.Leader, "Casual game", now.AddDays(-3)),
         new ClanStateMember(2, "Ünïcødé 名前", ClanRank.Officer, null, now.AddDays(-1))],
        [new ClanStateOutgoing(9, "Invitee", now)]),
    [new ClanStateInvite(8, "Other Clan", "OTHR", "Someone", now)]);
var buffer = new MemoryStream();
BNLReloadedServer.Service.ServiceClan.WriteState(new BinaryWriter(buffer), sent);
buffer.Position = 0;
var got = BNL.Clans.ClanProtocol.ReadState(new BinaryReader(buffer));
Check(buffer.Position == buffer.Length, "the client decoder consumes exactly the server's state message");
var gc = got.Clan!;
Check(gc.Id == 7 && gc.Name == "Yeti Squad" && gc.Tag == "YETI" && gc.LeaderId == 1 && gc.MyRank == BNL.Clans.ClanRank.Officer
      && gc.RenamedAt == now.ToUnixTimeSeconds(), "clan header round-trips");
Check(gc.Members.Count == 2 && gc.Members[0].Online && gc.Members[0].Activity == "Casual game"
      && !gc.Members[1].Online && gc.Members[1].Name == "Ünïcødé 名前" && gc.Members[1].Rank == BNL.Clans.ClanRank.Officer,
    "members round-trip, including offline state and non-ASCII names");
Check(gc.Outgoing.Single().Name == "Invitee" && got.Invites.Single().Tag == "OTHR" && got.Invites.Single().InviterName == "Someone",
    "outgoing and incoming invites round-trip");
buffer = new MemoryStream();
BNLReloadedServer.Service.ServiceClan.WriteState(new BinaryWriter(buffer), new ClanStateMessage(null, []));
buffer.Position = 0;
var empty = BNL.Clans.ClanProtocol.ReadState(new BinaryReader(buffer));
Check(empty.Clan == null && empty.Invites.Count == 0 && buffer.Position == buffer.Length, "a clanless state round-trips");

static bool SameEnum(Type a, Type b) =>
    Enum.GetNames(a).SequenceEqual(Enum.GetNames(b)) &&
    Enum.GetValues(a).Cast<object>().Select(Convert.ToInt32).SequenceEqual(Enum.GetValues(b).Cast<object>().Select(Convert.ToInt32));
Check(SameEnum(typeof(ClanResult), typeof(BNL.Clans.ClanResult)), "result codes match on both sides");
Check(SameEnum(typeof(ClanRank), typeof(BNL.Clans.ClanRank)), "ranks match on both sides");
var serverRequests = typeof(BNLReloadedServer.Service.ServiceClan).GetNestedType("ClientMessage", System.Reflection.BindingFlags.NonPublic)!;
Check(SameEnum(serverRequests, typeof(BNL.Clans.ClanRequest)), "request ids match on both sides");
Check((byte)BNLReloadedServer.Service.ServiceId.ServiceClan == BNL.Clans.ClanProtocol.ServiceId
      && BNLReloadedServer.Service.ServiceClan.ProtocolVersion == BNL.Clans.ClanProtocol.Version,
    "service id and protocol version match");
Check(BNL.Clans.ClanProtocol.MaxMembers == ClanRules.MaxMembers && BNL.Clans.ClanProtocol.MaxTagLength == ClanRules.MaxTagLength
      && BNL.Clans.ClanProtocol.MinTagLength == ClanRules.MinTagLength && BNL.Clans.ClanProtocol.MaxNameLength == ClanRules.MaxNameLength
      && BNL.Clans.ClanProtocol.MinNameLength == ClanRules.MinNameLength, "client validation limits match the server rules");
#else
Console.WriteLine("SKIP wire-format cross-check: client ClanProtocol.cs not found (set BnlClientProtocol).");
#endif
Console.WriteLine($"Clan fixture passed: {checks} checks.");

sealed class WordList(params string[] words) : IOffensiveText
{
    public bool IsOffensive(string text) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
