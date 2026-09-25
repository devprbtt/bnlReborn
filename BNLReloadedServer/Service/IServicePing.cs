namespace BNLReloadedServer.Service;

public interface IServicePing : IService
{
    public int RoundTripMilliseconds { get; }
    public void SendServerPing();
    public int SendLivenessProbe();
    public void SendClientPong();
}
