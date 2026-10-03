using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;
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
        if (Sender.AssociatedPlayerId is { } playerId)
        {
            var listedBy = Databases.PlayerDatabase.GetOnlinePlayersListing(playerId);
            Databases.RegionServerDatabase.RemoveUser(playerId, Id);
            if (Databases.PlayerDatabase.RemovePlayer(playerId))
                Databases.RegionServerDatabase.NotifyFriendsOfDeparture(listedBy).ObserveFailure(LogCat.Player,
                    $"Failed to notify friends that player {playerId} went offline");
            // Clanmates see the member go offline now rather than at the next clan change.
            BNLReloadedServer.Clans.ClanHub.PushClanOf(playerId);
        }

        Databases.RegionServerDatabase.RemoveServices(Id);
    }
}
