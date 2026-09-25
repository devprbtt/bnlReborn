using System.Net;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Servers;

public class MatchServiceDispatcher : IServiceDispatcher
{
    private readonly ServiceLogin _serviceLogin;
    private readonly ServiceZone _serviceZone;
    private readonly ServiceLobby _serviceLobby;
    private readonly ServicePing _servicePing;
    private readonly ServiceMediator _serviceMediator;

    public IServicePing Ping => _servicePing;

    public MatchServiceDispatcher(ISender sender, Guid sessionId, Func<IPAddress?> peerAddress)
    {
        _serviceLogin = new ServiceLogin(sender, sessionId, peerAddress);
        _serviceZone = new ServiceZone(sender, peerAddress);
        _serviceLobby = new ServiceLobby(sender);
        _servicePing = new ServicePing(sender);
        _serviceMediator = new ServiceMediator(sender);

        try
        {
            Databases.RegionServerDatabase.RegisterMatchService(sessionId, _serviceLogin, ServiceId.ServiceLogin);
            Databases.RegionServerDatabase.RegisterMatchService(sessionId, _serviceZone, ServiceId.ServiceZone);
            Databases.RegionServerDatabase.RegisterMatchService(sessionId, _serviceLobby, ServiceId.ServiceLobby);
            Databases.RegionServerDatabase.RegisterMatchService(sessionId, _servicePing, ServiceId.ServicePing);
            Databases.RegionServerDatabase.RegisterMatchService(sessionId, _serviceMediator, ServiceId.ServiceMediator);
        }
        catch (Exception e)
        {
            Log.Error(LogCat.Net, $"Failed to register match services for session {sessionId}", e);
        }
    }

    private static bool OnUnsupported(ServiceId? serviceEnum, byte raw)
    {
        Log.Warn(LogCat.Net, $"Match session received unsupported serviceId: {Log.EnumName(serviceEnum, raw)}");
        return false;
    }

    public bool Dispatch(BinaryReader reader)
    {
        var serviceId = reader.ReadByte();
        ServiceId? serviceEnum = null;
        if (Enum.IsDefined(typeof(ServiceId), serviceId))
        {
            serviceEnum = (ServiceId)serviceId;
        }

        Log.Debug(LogCat.Net, $"Service ID: {Log.EnumName(serviceEnum, serviceId)}");

        return serviceEnum switch
        {
            ServiceId.ServiceLogin => _serviceLogin.Receive(reader),
            ServiceId.ServiceZone => _serviceZone.Receive(reader),
            ServiceId.ServiceLobby => _serviceLobby.Receive(reader),
            ServiceId.ServicePing => _servicePing.Receive(reader),
            ServiceId.ServiceMediator => _serviceMediator.Receive(reader),
            _ => OnUnsupported(serviceEnum, serviceId)
        };
    }
}
