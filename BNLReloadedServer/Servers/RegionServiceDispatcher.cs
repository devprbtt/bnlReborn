using System.Net;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Servers;

public class RegionServiceDispatcher : IServiceDispatcher
{
    private readonly ServiceLogin _serviceLogin;
    private readonly ServiceScene _serviceScene;
    private readonly ServiceTime _serviceTime;
    private readonly ServiceCatalogue _serviceCatalogue;
    private readonly ServicePlayer _servicePlayer;
    private readonly ServiceChat _serviceChat;
    private readonly ServiceDebug _serviceDebug;
    private readonly ServiceMapEditor _serviceMapEditor;
    private readonly ServiceMatchmaker _serviceMatchmaker;
    private readonly ServiceLeaderboard _serviceLeaderboard;
    private readonly ServicePing _servicePing;
    private readonly ServiceClan _serviceClan;

    public IServicePing Ping => _servicePing;

    public RegionServiceDispatcher(ISender sender, Guid sessionId, Func<IPAddress?> peerAddress)
    {
        _serviceLogin = new ServiceLogin(sender, sessionId, peerAddress);
        _serviceScene = new ServiceScene(sender);
        _serviceTime = new ServiceTime(sender);
        _serviceCatalogue = new ServiceCatalogue(sender);
        _servicePlayer = new ServicePlayer(sender, _serviceScene, _serviceTime);
        _serviceChat = new ServiceChat(sender);
        _serviceDebug = new ServiceDebug(sender);
        _serviceMapEditor = new ServiceMapEditor(sender);
        _serviceMatchmaker = new ServiceMatchmaker(sender);
        _serviceLeaderboard = new ServiceLeaderboard(sender);
        _servicePing = new ServicePing(sender);
        _serviceClan = new ServiceClan(sender);

        try
        {
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceLogin, ServiceId.ServiceLogin);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceScene, ServiceId.ServiceScene);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceTime, ServiceId.ServiceTime);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceCatalogue, ServiceId.ServiceCatalogue);
            Databases.RegionServerDatabase.RegisterService(sessionId, _servicePlayer, ServiceId.ServicePlayer);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceChat, ServiceId.ServiceChat);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceDebug, ServiceId.ServiceDebug);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceMapEditor, ServiceId.ServiceMapEditor);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceMatchmaker, ServiceId.ServiceMatchmaker);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceLeaderboard, ServiceId.ServiceLeaderboard);
            Databases.RegionServerDatabase.RegisterService(sessionId, _servicePing, ServiceId.ServicePing);
            Databases.RegionServerDatabase.RegisterService(sessionId, _serviceClan, ServiceId.ServiceClan);
        }
        catch (Exception e)
        {
            Log.Error(LogCat.Net, $"Failed to register region services for session {sessionId}", e);
        }
    }

    private static bool OnUnsupported(ServiceId? serviceEnum, byte raw)
    {
        Log.Warn(LogCat.Net, $"Region session received unsupported serviceId: {Log.EnumName(serviceEnum, raw)}");
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
            ServiceId.ServiceScene => _serviceScene.Receive(reader),
            ServiceId.ServiceTime => _serviceTime.Receive(reader),
            ServiceId.ServiceCatalogue => _serviceCatalogue.Receive(reader),
            ServiceId.ServicePlayer => _servicePlayer.Receive(reader),
            ServiceId.ServiceChat => _serviceChat.Receive(reader),
            ServiceId.ServiceDebug => _serviceDebug.Receive(reader),
            ServiceId.ServiceMapEditor => _serviceMapEditor.Receive(reader),
            ServiceId.ServiceMatchmaker => _serviceMatchmaker.Receive(reader),
            ServiceId.ServiceLeaderboard => _serviceLeaderboard.Receive(reader),
            ServiceId.ServicePing => _servicePing.Receive(reader),
            ServiceId.ServiceClan => _serviceClan.Receive(reader),
            _ => OnUnsupported(serviceEnum, serviceId)
        };
    }
}
