using System.Net;
using System.Net.Sockets;
using BNLReloadedServer.Servers;
using NetCoreServer;

var checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
var port = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();

var server = new FixtureServer(IPAddress.Loopback, port);
using var client = new System.Net.Sockets.TcpClient();
var releaseWorker = new ManualResetEventSlim();
ThreadPool.GetMaxThreads(out var oldWorkers, out var oldIo);
ThreadPool.GetMinThreads(out var minWorkers, out _);

try
{
    Check(server.Start(), "fixture server started");
    client.Connect(IPAddress.Loopback, port);
    Check(server.Connected.Wait(TimeSpan.FromSeconds(3)), "fixture session connected");

    // Reproduce the dependency that used to strand handshake replies: consume the only worker
    // after the sender has begun waiting for work. A Channel-based sender cannot resume here.
    Check(ThreadPool.SetMaxThreads(minWorkers, oldIo), "worker pool constrained");
    var workersOccupied = new CountdownEvent(minWorkers);
    for (var i = 0; i < minWorkers; i++) _ = Task.Run(() =>
    {
        workersOccupied.Signal();
        releaseWorker.Wait();
    });
    Check(workersOccupied.Wait(TimeSpan.FromSeconds(5)), "all workers occupied");

    var packet = new byte[] { 0x0D, 0x10, 0xCA, 0xFE };
    server.Session!.Sender!.SendPacket(packet);
    client.ReceiveTimeout = 1000;
    var received = new byte[packet.Length];
    var count = client.GetStream().Read(received, 0, received.Length);
    Check(count == packet.Length && received.SequenceEqual(packet),
        "reply reaches socket without a ThreadPool continuation");

    server.Session.Sender.Stop();
    server.Session.Sender.SendPacket(new byte[] { 0x01 });
    client.ReceiveTimeout = 150;
    try
    {
        Check(client.GetStream().ReadByte() < 0, "stopped sender rejects later packets");
    }
    catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
    {
        Check(true, "stopped sender rejects later packets");
    }
}
finally
{
    releaseWorker.Set();
    ThreadPool.SetMaxThreads(oldWorkers, oldIo);
    client.Close();
    server.Stop();
}

Console.WriteLine($"Transport fixture passed: {checks} checks.");

sealed class FixtureServer(IPAddress address, int port) : TcpServer(address, port)
{
    public ManualResetEventSlim Connected { get; } = new();
    public FixtureSession? Session { get; private set; }

    protected override TcpSession CreateSession()
    {
        Session = new FixtureSession(this, Connected);
        return Session;
    }
}

sealed class FixtureSession(TcpServer server, ManualResetEventSlim connected) : TcpSession(server)
{
    public AsyncSenderTask? Sender { get; private set; }

    protected override void OnConnected()
    {
        Sender = new AsyncSenderTask(this);
        connected.Set();
    }
}
