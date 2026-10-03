using System.Reflection;
using BNLReloadedServer.BaseTypes;

var method = typeof(BNLReloadedServer.Database.RegionServerDatabase).GetMethod(
    "FriendUpdateRecipients", BindingFlags.Static | BindingFlags.NonPublic)!;

uint[] Recipients(uint playerId, params uint[] friendIds) =>
    ((IEnumerable<uint>)method.Invoke(null, new object[]
    {
        playerId,
        friendIds.Select(id => new FriendInfo { PlayerId = id })
    })!).ToArray();

var checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

Check(Recipients(1, 2, 3).SequenceEqual(new uint[] { 2, 3, 1 }),
    "friend changes notify every current friend and the list owner");
Check(Recipients(1).SequenceEqual(new uint[] { 1 }),
    "removing the last friend still updates the list owner");
Check(Recipients(1, 1, 2, 2).SequenceEqual(new uint[] { 1, 2 }),
    "duplicate or self entries produce one update per player");

var listing = typeof(BNLReloadedServer.Database.PlayerDatabase).GetMethod(
    "PlayersListing", BindingFlags.Static | BindingFlags.NonPublic)!;

uint[] ListedBy(uint playerId, ulong steamId,
    params (uint id, uint[] friends, ulong[]? steam)[] online) =>
    ((List<uint>)listing.Invoke(null, new object[]
    {
        playerId, steamId,
        online.Select(p => (p.id, (IReadOnlyCollection<uint>)p.friends, (IReadOnlyCollection<ulong>?)p.steam))
    })!).ToArray();

Check(ListedBy(1, 100, (1, [2], null), (2, [1], null), (3, [4], null)).SequenceEqual(new uint[] { 2 }),
    "a departure reaches online players whose game friend list has the player");
Check(ListedBy(1, 100, (1, [], null), (2, [], [100UL]), (3, [], [200UL])).SequenceEqual(new uint[] { 2 }),
    "a departure reaches online players who list the player as a Steam friend");
Check(ListedBy(1, 100, (1, [1], [100UL]), (2, [1], [100UL])).SequenceEqual(new uint[] { 2 }),
    "the departing player and duplicate links produce one update per friend");
Check(ListedBy(1, 0, (2, [], [0UL])).Length == 0,
    "an unknown Steam id never matches unrelated players");

Console.WriteLine($"Friend removal fixture passed: {checks} checks.");
