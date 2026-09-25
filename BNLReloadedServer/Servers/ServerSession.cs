using System.Net;
using System.Net.Sockets;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using NetCoreServer;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Servers;

// Shared plumbing for every session type: sender task wiring, liveness probing, and a
// teardown path that runs exactly once no matter how many threads reach it.
public abstract class ServerSession : TcpSession
{
    private int _teardownStarted;
    private CancellationTokenSource? _livenessCts;
    private bool _connected;

    // Resolved once while the socket is up — RemoteEndPoint is gone by the time anything wants to
    // name the peer in a teardown line — and on first use rather than at connect, because the
    // receive callback can get there before OnConnected does.
    private volatile string? _peer;

    private string? Peer => _peer ??= ReadPeer();

    // The address on its own, for the records that key on the client rather than describe one
    // connection. Read off the endpoint instead of parsed back out of Peer, whose IPv6 form has
    // colons of its own, and folded back to v4 when the accept came in v4-mapped, so a dual-stack
    // listener does not file one client under two addresses.
    private volatile IPAddress? _peerAddress;
    private readonly ProxyProtocolV1Reader _proxyProtocol = new();

    protected IPAddress? PeerAddress => _peerAddress ??= ReadPeerAddress();

    protected SessionSender Sender { get; }

    protected string Label { get; }

    protected abstract SessionReader Reader { get; }

    // Null for session types that do not probe for liveness.
    protected abstract IServicePing? LivenessPing { get; }

    protected abstract void OnTeardown();

    protected ServerSession(AsyncTaskTcpServer server, string label) : base(server)
    {
        Label = label;
        var senderTask = new AsyncSenderTask(this);
        server.AddSenderTask(Id, senderTask);
        Sender = new SessionSender(server, Id, senderTask);
    }

    protected override void OnConnected()
    {
        _connected = true;
        _ = Peer;
        _ = PeerAddress;

        if (LivenessPing is { } ping)
        {
            _livenessCts = SessionLiveness.Start(ping, this, Label, Sender, Reader, () => Peer);
            // A socket failure can tear this session down while we are still in here. Teardown
            // would have seen a null field, so the probe we just started is ours to stop.
            if (Volatile.Read(ref _teardownStarted) != 0)
                StopLiveness();
        }

        LogPeerEvent($"{Label} session {Describe} connected");
    }

    // How many previous names to name before the guess stops being a guess worth reading.
    private const int MaxPeerNames = 3;

    // One lookup per session, shared by the connect and disconnect lines, kept as the finished
    // suffix so neither line has to know how the guess is worded.
    private Task<string>? _peerNames;

    // The names cost a database read, so the line is printed by the lookup rather than ahead of
    // it: knowing who an address probably is beats printing the line a millisecond sooner. Nothing
    // waits on this — a session is never held up by its own log line.
    private void LogPeerEvent(string message) => _ = LogPeerEventAsync(message);

    private async Task LogPeerEventAsync(string message)
    {
        string names;
        try
        {
            names = await (_peerNames ??= ResolvePeerNames());
        }
        catch (Exception e)
        {
            Log.Info(LogCat.Conn, message);
            Log.Error(LogCat.Conn, $"Failed to look up who {Describe} has been before", e);
            return;
        }

        Log.Info(LogCat.Conn, message + names);
    }

    private async Task<string> ResolvePeerNames()
    {
        if (PeerAddress is not { } address) return string.Empty;

        // One more than gets printed, purely to tell "that is all of them" from "and others" —
        // counting the rest would mean reading every account the address has ever carried.
        var names = await Databases.MasterServerDatabase.GetNicknamesForIp(address.ToString(), MaxPeerNames + 1);
        if (names.Count == 0) return string.Empty;

        var shown = names.Take(MaxPeerNames).ToList();
        if (names.Count > MaxPeerNames) shown.Add("...");

        return $" ({string.Join(", ", shown)})";
    }

    // Who the session is, for a log line: the address it came from, with the tail of its id in
    // brackets to tell two connections from the same host apart.
    private string Describe => Peer is { } peer ? $"{peer} ({Log.ShortId(Id)})" : Log.ShortId(Id);

    private string? ReadPeer()
    {
        try
        {
            return Socket.RemoteEndPoint?.ToString();
        }
        catch (Exception)
        {
            // A socket that died between accept and here has no address left to report.
            return null;
        }
    }

    private IPAddress? ReadPeerAddress()
    {
        try
        {
            if (Socket.RemoteEndPoint is not IPEndPoint endPoint) return null;
            return endPoint.Address.IsIPv4MappedToIPv6 ? endPoint.Address.MapToIPv4() : endPoint.Address;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Disconnect() gates on a plain IsConnected check, so the liveness timer, the receive
    // completion and a failing Send can all reach here at once. The first thread in does the
    // teardown; the rest fall straight through.
    protected override void OnDisconnected()
    {
        if (Interlocked.Exchange(ref _teardownStarted, 1) != 0) return;

        StopLiveness();
        OnTeardown();

        if (_connected)
            LogPeerEvent($"{Label} session {Describe} disconnected");

        _connected = false;
    }

    private void StopLiveness()
    {
        var cts = Interlocked.Exchange(ref _livenessCts, null);
        if (cts == null) return;

        cts.Cancel();
        cts.Dispose();
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        if (size <= 0) return;

        if (!_proxyProtocol.IsResolved)
        {
            var initial = _proxyProtocol.Accept(buffer.AsSpan(checked((int)offset), checked((int)size)));
            if (!initial.Ready) return;
            if (initial.Invalid)
            {
                Log.Warn(LogCat.Conn, $"{Label} session {Describe} sent an invalid PROXY protocol header");
                Disconnect();
                return;
            }
            if (initial.Address != null)
            {
                _peerAddress = initial.Address;
                _peer = initial.Peer;
            }
            if (initial.Payload.Length == 0) return;
            using var initialPeer = Log.WithPeer(Peer);
            Reader.ProcessPacket(initial.Payload, 0, initial.Payload.Length);
            return;
        }

        using var peer = Log.WithPeer(Peer);
        Reader.ProcessPacket(buffer, offset, size);
    }

    protected override void OnError(SocketError error)
    {
        Log.Error(LogCat.Conn, $"{Label} session {Describe} socket error: {error}");
    }
}
