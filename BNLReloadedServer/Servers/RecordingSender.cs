using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Servers;

// Wraps a match's zone broadcast sender. While a MatchRecorder is attached it
// receives a copy of every packet exactly as subscribed sessions get it
// (length-prefixed), and counts as one extra listener so update paths that
// skip work when nobody is subscribed still run during a recorded match.
public sealed class RecordingSender(ISender inner) : ISender
{
    private volatile MatchRecorder? _recorder;

    public MatchRecorder? Recorder => _recorder;

    public uint? AssociatedPlayerId
    {
        get => inner.AssociatedPlayerId;
        set => inner.AssociatedPlayerId = value;
    }

    public int SenderCount => inner.SenderCount + (_recorder != null ? 1 : 0);

    public void Attach(MatchRecorder recorder) => _recorder = recorder;

    public MatchRecorder? Detach()
    {
        var recorder = _recorder;
        _recorder = null;
        return recorder;
    }

    public void Send(BinaryWriter writer)
    {
        inner.Send(writer);
        _recorder?.RecordStream(Frame(writer));
    }

    public void Send(byte[] buffer)
    {
        inner.Send(buffer);
        _recorder?.RecordStream(buffer);
    }

    public void SendExcept(BinaryWriter writer, List<Guid> excluded)
    {
        inner.SendExcept(writer, excluded);
        // Exclusions name players; a spectator (and so a replay) is never excluded.
        _recorder?.RecordStream(Frame(writer));
    }

    public void Subscribe(Guid sessionId) => inner.Subscribe(sessionId);

    public void Unsubscribe(Guid sessionId) => inner.Unsubscribe(sessionId);

    public void UnsubscribeAll() => inner.UnsubscribeAll();

    // The wire framing SessionSender applies: 7-bit encoded length, then payload.
    public static byte[] Frame(BinaryWriter writer)
    {
        var payload = (MemoryStream)writer.BaseStream;
        using var framed = new MemoryStream((int)payload.Length + 5);
        using (var packetWriter = new BinaryWriter(framed, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            packetWriter.Write7BitEncodedInt((int)payload.Length);
            packetWriter.Write(payload.GetBuffer(), 0, (int)payload.Length);
        }
        return framed.ToArray();
    }
}

// Collects framed packets written through a ServiceZone, for keyframes.
public sealed class CaptureSender : ISender
{
    private readonly MemoryStream _captured = new();

    public uint? AssociatedPlayerId { get; set; }
    public int SenderCount => 1;

    public byte[] ToArray() => _captured.ToArray();

    public void Send(BinaryWriter writer)
    {
        var framed = RecordingSender.Frame(writer);
        _captured.Write(framed, 0, framed.Length);
    }

    public void Send(byte[] buffer) => _captured.Write(buffer, 0, buffer.Length);
    public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
    public void Subscribe(Guid sessionId) { }
    public void Unsubscribe(Guid sessionId) { }
    public void UnsubscribeAll() { }
}
