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

Console.WriteLine($"Friend removal fixture passed: {checks} checks.");
