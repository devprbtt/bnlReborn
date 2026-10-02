using BNLReloadedServer.Database;
using BNLReloadedServer.Service;

namespace BNLReloadedServer.Servers;

internal class RegionSession : ServerSession
{
    private readonly RegionServiceDispatcher _serviceDispatcher;
    private readonly SessionReader _reader;

    public RegionSession(AsyncTaskTcpServer server) : base(server, "Region")
    {
        _serviceDispatcher = new RegionServiceDispatcher(Sender, Id, () => PeerAddress);
        _reader = new SessionReader(_serviceDispatcher,
            "Region server received packet with incorrect length");
    }

    protected override SessionReader Reader => _reader;

    protected override IServicePing LivenessPing => _serviceDispatcher.Ping;

    protected override void OnTeardown()
    {
        if (Sender.AssociatedPlayerId != null)
        {
            Databases.RegionServerDatabase.RemoveUser(Sender.AssociatedPlayerId.Value, Id);
            Databases.PlayerDatabase.RemovePlayer(Sender.AssociatedPlayerId.Value);
            // Clanmates see the member go offline now rather than at the next clan change.
            BNLReloadedServer.Clans.ClanHub.PushClanOf(Sender.AssociatedPlayerId.Value);
        }

        Databases.RegionServerDatabase.RemoveServices(Id);
    }
}
