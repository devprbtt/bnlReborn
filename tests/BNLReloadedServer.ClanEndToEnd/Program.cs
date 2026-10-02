// End-to-end clan test against a local server: real Reborn ticket login (test key), real TCP, server store,
// pushes, and the client's own ClanProtocol decoder. Usage: keys <dir> | run <dir>
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using BNL.Clans;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var dir = args[1];
if (args[0] == "keys")
{
    using var rsa = RSA.Create(2048);
    File.WriteAllText(Path.Combine(dir, "test-auth-private.pem"), rsa.ExportRSAPrivateKeyPem());
    File.WriteAllText(Path.Combine(dir, "test-auth-public.pem"), rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("keys written");
    return;
}

var key = RSA.Create();
key.ImportFromPem(File.ReadAllText(Path.Combine(dir, "test-auth-private.pem")));
string Ticket(ulong steamId, string name) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
{
    Issuer = "https://auth.blocknload.cc", Audience = "bnl-reborn-game",
    Expires = DateTime.UtcNow.AddMinutes(5),
    Subject = new ClaimsIdentity([new Claim("sub", steamId.ToString()), new Claim("jti", Guid.NewGuid().ToString("N")), new Claim("name", name)]),
    SigningCredentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)
});

var run = Guid.NewGuid().ToString("N")[..5];
int checks = 0;
void Check(bool pass, string what) { if (!pass) throw new Exception("FAIL " + what); checks++; Console.WriteLine("PASS " + what); }

ulong idBase = 76561190000000000UL + (ulong)Random.Shared.Next(1, 90000000) * 10;
var alice = await Bot.Login(idBase + 1, "Alice" + run, Ticket);
var bob = await Bot.Login(idBase + 2, "Bob" + run, Ticket);
var carol = await Bot.Login(idBase + 3, "Carol" + run, Ticket);
Check(alice.State != null && bob.State != null && carol.State != null, "three players log in and receive clan state after Hello");
Check(alice.State.Clan == null, "a new player starts without a clan");

var tag = "E2E" + run[..3].ToUpperInvariant();
Check(await alice.Ask(ClanRequest.Create, w => { w.Write("Yeti Test " + run); w.Write(tag); }) == ClanResult.Ok, "Alice creates a clan");
Check(await alice.Until(s => s.Clan?.Tag == tag && s.Clan.MyRank == ClanRank.Leader), "Alice's state shows her clan, as leader");
Check(await bob.Ask(ClanRequest.Create, w => { w.Write("Yeti Test " + run.ToUpperInvariant()); w.Write("OTHER" + run[..3]); }) == ClanResult.NameTaken,
    "the clan name is taken regardless of case");

Check(await alice.Ask(ClanRequest.Invite, w => w.Write("bob" + run)) == ClanResult.Ok, "Alice invites Bob by name (case-insensitive)");
Check(await bob.Until(s => s.Invites.Any(i => i.Tag == tag && i.InviterName == "Alice" + run)), "Bob is pushed the invite, naming the inviter");
Check(await alice.Until(s => s.Clan.Outgoing.Any(o => o.Name == "Bob" + run)), "Alice sees Bob as a pending invite");
Check(await alice.Ask(ClanRequest.Invite, w => w.Write("Nobody" + run)) == ClanResult.NoSuchPlayer, "inviting an unknown name fails clearly");

var clanId = bob.State.Invites.First(i => i.Tag == tag).ClanId;
Check(await bob.Ask(ClanRequest.Accept, w => w.Write(clanId)) == ClanResult.Ok, "Bob accepts");
Check(await alice.Until(s => s.Clan.Members.Any(m => m.Name == "Bob" + run && m.Online && m.Rank == ClanRank.Member)),
    "Alice is pushed the roster with Bob online");
Check(await bob.Until(s => s.Clan?.Tag == tag && s.Clan.MyRank == ClanRank.Member && s.Invites.Count == 0), "Bob's state shows the clan and no invites");

Check(await bob.Ask(ClanRequest.Invite, w => w.Write("Carol" + run)) == ClanResult.NotAllowed, "members cannot invite");
Check(await alice.Ask(ClanRequest.SetRank, w => { w.Write(bob.PlayerId); w.Write((byte)ClanRank.Officer); }) == ClanResult.Ok, "Alice promotes Bob");
Check(await bob.Until(s => s.Clan.MyRank == ClanRank.Officer), "Bob is pushed his new rank");
Check(await bob.Ask(ClanRequest.Invite, w => w.Write("Carol" + run)) == ClanResult.Ok, "officers can invite");
Check(await carol.Ask(ClanRequest.Accept, w => w.Write(clanId)) == ClanResult.Ok
      && await alice.Until(s => s.Clan.Members.Count == 3), "Carol joins and the leader sees three members");

Check(await bob.Ask(ClanRequest.Kick, w => w.Write(alice.PlayerId)) == ClanResult.NotAllowed, "officers cannot kick the leader");
Check(await bob.Ask(ClanRequest.Kick, w => w.Write(carol.PlayerId)) == ClanResult.Ok, "Bob kicks Carol");
Check(await carol.Until(s => s.Clan == null) && await alice.Until(s => s.Clan.Members.Count == 2), "Carol is pushed her removal; the roster shrinks");

Check(await alice.Ask(ClanRequest.Leave, null) == ClanResult.LeaderMustTransfer, "the leader must transfer before leaving");
Check(await alice.Ask(ClanRequest.Transfer, w => w.Write(bob.PlayerId)) == ClanResult.Ok, "Alice hands leadership to Bob");
Check(await bob.Until(s => s.Clan.MyRank == ClanRank.Leader && s.Clan.LeaderId == bob.PlayerId)
      && await alice.Until(s => s.Clan.MyRank == ClanRank.Officer), "both are pushed the swapped ranks");

carol.Dispose();
Check(await bob.Until(s => s.Clan.Members.All(m => m.Name != "Carol" + run)), "(Carol is not a member; disconnect has no clan effect)");
alice.Dispose();
Check(await bob.Until(s => s.Clan.Members.Any(m => m.Name == "Alice" + run && !m.Online)),
    "Bob is pushed Alice going offline as soon as she disconnects");
var alice2 = await Bot.Login(idBase + 1, "Alice" + run, Ticket);
Check(await bob.Until(s => s.Clan.Members.Any(m => m.Name == "Alice" + run && m.Online)),
    "Bob is pushed Alice coming back online when she logs in again");
Check(await alice2.Until(s => s.Clan?.Tag == tag && s.Clan.MyRank == ClanRank.Officer), "Alice's membership survives her reconnect");
Check(await bob.Ask(ClanRequest.Rename, w => { w.Write("Yeti Renamed " + run); w.Write(tag + "R"); }) == ClanResult.Ok
      && await alice2.Until(s => s.Clan.Tag == tag + "R"), "a rename reaches the other online member");
alice2.Dispose();

var requests = Enumerable.Range(0, 12).Select(_ => bob.Ask(ClanRequest.Leave, null)).ToArray();
var results = await Task.WhenAll(requests);
Check(results.Count(r => r == ClanResult.NotAllowed) >= 2, "a burst of 12 requests is rate-limited after 10");
await Task.Delay(11000);
Check(await bob.Ask(ClanRequest.Disband, null) == ClanResult.Ok && await bob.Until(s => s.Clan == null), "the leader disbands the clan");
bob.Dispose();
Console.WriteLine($"Clan end-to-end test passed: {checks} checks.");

sealed class Bot : IDisposable
{
    private readonly TcpClient tcp = new();
    private NetworkStream stream;
    private readonly BlockingCollection<(byte Service, byte Message, BinaryReader Body)> inbox = new();
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<ClanResult>> pending = new();
    private ushort nextRequest = 1;
    public ClanState State;
    public uint PlayerId;
    private readonly SemaphoreSlim stateChanged = new(0);

    public static async Task<Bot> Login(ulong steamId, string name, Func<ulong, string, string> ticket)
    {
        // Master: signed login, pick the region, receive its address and a session token.
        var master = new Bot();
        await master.Connect(28100);
        master.Send(1, w => { w.Write((byte)7); w.Write((ushort)1); w.Write("bnl-reborn-v1"); w.Write(ticket(steamId, name)); });
        string host = null; int port = 0; string token = null;
        uint playerId = 0;
        while (token == null)
        {
            var (service, message, body) = master.Next();
            if (service != 1) continue;
            if (message == 7)
            {
                body.ReadUInt16();
                if (body.ReadByte() != 0) throw new Exception("master login refused: " + body.ReadString());
                playerId = body.ReadUInt32();
            }
            else if (message == 9) master.Send(1, w => { w.Write((byte)10); w.Write("master"); w.Write(false); });
            else if (message == 11) { host = body.ReadString(); port = body.ReadInt32(); token = body.ReadString(); }
        }
        master.Dispose();

        // Region: log in with the session token, then opt in to clans.
        var bot = new Bot { PlayerId = playerId };
        await bot.Connect(port);
        bot.Send(1, w => { w.Write((byte)12); w.Write((ushort)2); w.Write(token); w.Write(0u); });
        bot.Send(ClanProtocol.ServiceId, w => ClanProtocol.WriteHello(w));
        _ = Task.Run(bot.Pump);
        if (!await bot.Until(s => true)) throw new Exception(name + " received no clan state after Hello");
        return bot;
    }

    private async Task Connect(int port) { await tcp.ConnectAsync("127.0.0.1", port); stream = tcp.GetStream(); _ = Task.Run(Read); }

    private void Read()
    {
        try
        {
            var reader = new BinaryReader(stream);
            while (true)
            {
                int length = reader.Read7BitEncodedInt();
                var body = new BinaryReader(new MemoryStream(reader.ReadBytes(length)));
                inbox.Add((body.ReadByte(), body.ReadByte(), body));
            }
        }
        catch (Exception) { inbox.CompleteAdding(); }
    }

    private (byte, byte, BinaryReader) Next() => inbox.Take(new CancellationTokenSource(10000).Token);

    private void Pump()
    {
        foreach (var (service, message, body) in inbox.GetConsumingEnumerable())
        {
            if (service != ClanProtocol.ServiceId) continue;
            if (message == ClanProtocol.MessageState) { State = ClanProtocol.ReadState(body); stateChanged.Release(); }
            else if (message == ClanProtocol.MessageResult)
            {
                var id = body.ReadUInt16();
                if (pending.TryRemove(id, out var done)) done.SetResult((ClanResult)body.ReadByte());
            }
        }
    }

    public void Send(byte service, Action<BinaryWriter> write)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        w.Write(service);
        write(w);
        var frame = new MemoryStream();
        var fw = new BinaryWriter(frame);
        fw.Write7BitEncodedInt((int)body.Length);
        fw.Write(body.ToArray());
        lock (tcp) stream.Write(frame.ToArray());
    }

    public Task<ClanResult> Ask(ClanRequest request, Action<BinaryWriter> payload)
    {
        ushort id;
        lock (pending) id = nextRequest++;
        var done = new TaskCompletionSource<ClanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = done;
        Send(ClanProtocol.ServiceId, w => { ClanProtocol.WriteRequest(w, request, id); payload?.Invoke(w); });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Waits until the latest pushed state satisfies the condition (up to 10 seconds).</summary>
    public async Task<bool> Until(Func<ClanState, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (State != null && condition(State)) return true;
            await stateChanged.WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        return State != null && condition(State);
    }

    public void Dispose() { try { tcp.Close(); } catch (Exception) { } }
}
