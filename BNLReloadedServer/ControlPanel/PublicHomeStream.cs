using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.ControlPanel;

/// <summary>Public minimal roster only; never forwards administrative event payloads.</summary>
public static class PublicHomeStream
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static async Task Serve(HttpListenerContext context, Func<PublicHomeSnapshot> snapshot, CancellationToken stop)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 426;
            context.Response.Close();
            return;
        }
        using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop);
        using var subscription = ControlPanelEvents.Subscribe();
        var disconnected = ObserveDisconnect(socket, lifetime.Token);
        bool retry = false;
        async Task SendSnapshot()
        {
            string json;
            try { json = JsonSerializer.Serialize(snapshot(), Json); retry = false; }
            catch (Exception) { json = "{\"unavailable\":true}"; retry = true; }
            await Send(socket, json, lifetime.Token);
        }
        try
        {
            await SendSnapshot();
            var heartbeat = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!lifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                wait.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, (heartbeat - DateTimeOffset.UtcNow).TotalMilliseconds)));
                var changed = subscription.WaitAsync(wait.Token);
                if (await Task.WhenAny(changed, disconnected) == disconnected)
                {
                    wait.Cancel();
                    try { await changed; } catch (OperationCanceledException) { }
                    break;
                }
                try
                {
                    if ((await changed & ControlPanelEvents.PresenceEvents) != 0)
                    {
                        await Task.Delay(50, lifetime.Token);
                        await SendSnapshot();
                        heartbeat = DateTimeOffset.UtcNow.AddSeconds(15);
                    }
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    if (retry) await SendSnapshot();
                    else await Send(socket, "{\"heartbeat\":true}", lifetime.Token);
                    heartbeat = DateTimeOffset.UtcNow.AddSeconds(15);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is HttpListenerException) { }
        finally
        {
            lifetime.Cancel(); socket.Abort();
            await disconnected;
        }
    }
    private static async Task Send(WebSocket socket, string json, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, timeout.Token);
    }
    private static async Task ObserveDisconnect(WebSocket socket, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[256];
            while ((await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)).MessageType != WebSocketMessageType.Close) { }
        }
        catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException) { }
    }
}
