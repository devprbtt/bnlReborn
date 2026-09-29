// Minimum client version: the gate's rules, and the real Reborn master login refusing an outdated client
// with an update message after a valid ticket, while an unreported or current version passes the gate.
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using BNLReloadedServer.Authentication;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

// The server reads Configs/ and the auth key relative to its working directory and environment.
var root = Path.Combine(Path.GetTempPath(), "bnl-client-version-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "Configs"));
Directory.CreateDirectory(Path.Combine(root, "PlayerData"));
Directory.SetCurrentDirectory(root);
using var rsa = RSA.Create(2048);
var keyPath = Path.Combine(root, "auth-public.pem");
File.WriteAllText(keyPath, rsa.ExportSubjectPublicKeyInfoPem());
Environment.SetEnvironmentVariable("BNL_REBORN_AUTH_PUBLIC_KEY_PATH", keyPath);
var minimumFile = Path.Combine(root, "Configs", "minimum_client_version.txt");
Databases.SetRegionDatabase(DispatchProxy.Create<IRegionServerDatabase, NullProxy>());

try
{
    Check(ClientVersionGate.SplitProtocol("bnl-reborn-v1;client=0.2.0-beta.43", out var reported) == "bnl-reborn-v1" && reported == "0.2.0-beta.43",
        "the protocol id and the reported version are split");
    Check(ClientVersionGate.SplitProtocol("bnl-reborn-v1", out var none) == "bnl-reborn-v1" && none is null,
        "clients that report nothing keep the plain protocol id");

    var gate = new ClientVersionGate(minimumFile);
    Check(gate.Refusal(null) is null && gate.Refusal("0.2.0-beta.1") is null, "no minimum file enforces nothing");
    SetMinimum("0.2.0-beta.43");
    Check(gate.Refusal("0.2.0-beta.42") is { } old && old.Contains("out of date") && old.Contains("0.2.0-beta.42") && old.Contains("restart the BNL Reborn launcher"),
        "an older release is refused with an update message");
    Check(gate.Refusal("0.2.0-beta.43") is null && gate.Refusal("0.2.0-beta.44") is null, "the minimum and newer releases log in");
    Check(gate.Refusal("0.2.0-beta.100") is null, "release numbers compare as numbers, not text");
    Check(gate.Refusal(null) is not null && gate.Refusal("garbage") is not null, "unreported or unreadable versions are refused once a minimum is set");
    SetMinimum("0.2.0-beta.4O");
    Check(gate.Refusal("0.2.0-beta.42") is not null && gate.Refusal("0.2.0-beta.43") is null, "a typo in the file keeps the last good minimum");
    SetMinimum("");
    Check(gate.Refusal("0.2.0-beta.1") is null, "an emptied file turns enforcement off without a restart");

    // The real login handler, with the shared validator configured from the environment above.
    SetMinimum("0.2.0-beta.43");
    var (outdatedReply, outdatedPassed) = Login("bnl-reborn-v1;client=0.2.0-beta.42", Ticket(rsa));
    Check(!outdatedPassed && outdatedReply.Contains("out of date") && outdatedReply.Contains("0.2.0-beta.42"), "the master login refuses an outdated client with the update message");
    var (unreportedReply, _) = Login("bnl-reborn-v1", Ticket(rsa));
    Check(unreportedReply.Contains("an older version"), "the master login refuses clients that report nothing once a minimum is set");
    var (badTicketReply, _) = Login("bnl-reborn-v1;client=0.2.0-beta.42", "not-a-ticket");
    Check(!badTicketReply.Contains("out of date"), "an invalid ticket is refused for the ticket, before any version message");
    SetMinimum("");
    var (_, noMinimumPassed) = Login("bnl-reborn-v1;client=0.2.0-beta.42", Ticket(rsa));
    Check(noMinimumPassed, "with no minimum the same client gets past the gate to player creation");
    SetMinimum("0.2.0-beta.43");
    var (_, currentPassed) = Login("bnl-reborn-v1;client=0.2.0-beta.43", Ticket(rsa));
    Check(currentPassed, "a client at the minimum gets past the gate to player creation");
}
finally
{
    Directory.SetCurrentDirectory(Path.GetTempPath());
    try { Directory.Delete(root, true); } catch (IOException) { }
}
Console.WriteLine($"Client version fixture passed: {checks} checks.");

void SetMinimum(string value)
{
    File.WriteAllText(minimumFile, value);
    // Each write must look like a change even within the file system's timestamp resolution.
    File.SetLastWriteTimeUtc(minimumFile, DateTime.UtcNow.AddSeconds(checks + 1));
}

// Sends one MessageLoginMasterXxx through ServiceLogin. Returns the reply text, and whether the handler got
// past the ticket and the version gate: it then creates the player record, which needs the game catalogue
// this fixture does not load, so it throws there without having sent any reply.
(string Reply, bool PassedGate) Login(string protocolId, string ticket)
{
    var sender = new CapturingSender();
    var service = new ServiceLogin(sender, Guid.NewGuid(), () => IPAddress.Loopback);
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)7); // MessageLoginMasterXxx
        writer.Write((ushort)1);
        writer.Write(protocolId);
        writer.Write(ticket);
    }
    stream.Position = 0;
    try
    {
        service.ReceiveMaster(new BinaryReader(stream));
        return (sender.Text, false);
    }
    catch (Exception exception)
    {
        var reachedPlayerCreation = exception.ToString().Contains("AddPlayer", StringComparison.Ordinal);
        return (sender.Text, reachedPlayerCreation && sender.Buffers.Count == 0);
    }
}

static string Ticket(RSA key)
{
    var now = DateTime.UtcNow;
    return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = RebornGameTicketValidator.DefaultIssuer,
        Audience = RebornGameTicketValidator.DefaultAudience,
        Subject = new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, "76561198000000000"),
            new Claim("name", "Version Tester"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        ]),
        IssuedAt = now,
        NotBefore = now.AddSeconds(-1),
        Expires = now.AddMinutes(1),
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)
    });
}

sealed class CapturingSender : ISender
{
    public List<byte[]> Buffers { get; } = [];
    public string Text { get; private set; } = "";
    public uint? AssociatedPlayerId { get; set; }
    public int SenderCount => 1;
    public void Send(BinaryWriter writer) => Send(((MemoryStream)writer.BaseStream).ToArray());
    public void Send(byte[] buffer) { Buffers.Add(buffer); Text += System.Text.Encoding.UTF8.GetString(buffer); }
    public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
    public void Subscribe(Guid sessionId) { }
    public void Unsubscribe(Guid sessionId) { }
    public void UnsubscribeAll() { }
}

public class NullProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method is null || method.ReturnType == typeof(void) || !method.ReturnType.IsValueType ? null : Activator.CreateInstance(method.ReturnType);
}
