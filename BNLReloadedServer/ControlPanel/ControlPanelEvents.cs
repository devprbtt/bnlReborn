using System.Collections.Concurrent;

namespace BNLReloadedServer.ControlPanel;

[Flags]
public enum ControlPanelEvent
{
    None = 0,
    Status = 1,
    Activity = 2,
    Queues = 4,
    Players = 8,
    Logs = 16
}

public static class ControlPanelEvents
{
    public const ControlPanelEvent PresenceEvents = ControlPanelEvent.Status | ControlPanelEvent.Activity | ControlPanelEvent.Queues | ControlPanelEvent.Players;
    private static long _presenceRevision;
    public static long PresenceRevision => Interlocked.Read(ref _presenceRevision);
    private static long _nextId;
    private static readonly ConcurrentDictionary<long, Subscription> Subscriptions = new();

    public static Subscription Subscribe()
    {
        var subscription = new Subscription(Interlocked.Increment(ref _nextId));
        Subscriptions[subscription.Id] = subscription;
        return subscription;
    }

    public static void Publish(ControlPanelEvent events)
    {
        if ((events & PresenceEvents) != 0) Interlocked.Increment(ref _presenceRevision);
        foreach (var subscription in Subscriptions.Values)
            subscription.Signal(events);
    }

    public sealed class Subscription(long id) : IDisposable
    {
        private readonly SemaphoreSlim _ready = new(0, 1);
        private int _pending;
        private int _disposed;

        internal long Id { get; } = id;

        internal void Signal(ControlPanelEvent events)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Or(ref _pending, (int)events);
            try { _ready.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }

        public async Task<ControlPanelEvent> WaitAsync(CancellationToken ct)
        {
            await _ready.WaitAsync(ct);
            return (ControlPanelEvent)Interlocked.Exchange(ref _pending, 0);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Subscriptions.TryRemove(Id, out _);
            _ready.Dispose();
        }
    }
}
