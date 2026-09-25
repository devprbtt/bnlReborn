using System.Net;
using System.Text;

namespace BNLReloadedServer.Servers;

internal sealed class ProxyProtocolV1Reader
{
    private const int MaximumHeaderBytes = 108;
    private static readonly byte[] Prefix = "PROXY "u8.ToArray();
    private readonly MemoryStream _probe = new(MaximumHeaderBytes);

    public bool IsResolved { get; private set; }

    public Result Accept(ReadOnlySpan<byte> bytes)
    {
        if (IsResolved) return new Result(true, false, null, null, bytes.ToArray());

        _probe.Write(bytes);
        var buffered = _probe.GetBuffer().AsSpan(0, checked((int)_probe.Length));
        var prefixLength = Math.Min(buffered.Length, Prefix.Length);
        if (!buffered[..prefixLength].SequenceEqual(Prefix.AsSpan(0, prefixLength)))
        {
            IsResolved = true;
            return new Result(true, false, null, null, buffered.ToArray());
        }
        if (buffered.Length < Prefix.Length) return Result.NeedMore;

        var terminator = FindTerminator(buffered);
        if (terminator < 0)
        {
            if (buffered.Length <= MaximumHeaderBytes) return Result.NeedMore;
            IsResolved = true;
            return new Result(true, true, null, null, []);
        }

        var header = Encoding.ASCII.GetString(buffered[..terminator]);
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IPAddress? address = null;
        string? peer = null;
        var invalid = false;
        if (parts.Length == 2 && parts[1] == "UNKNOWN")
        {
            // The proxy deliberately withheld the original endpoint.
        }
        else if (parts.Length == 6 && (parts[1] == "TCP4" || parts[1] == "TCP6") &&
                 IPAddress.TryParse(parts[2], out address) && ushort.TryParse(parts[4], out var port) &&
                 ((parts[1] == "TCP4" && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ||
                  (parts[1] == "TCP6" && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)))
        {
            peer = new IPEndPoint(address, port).ToString();
        }
        else
        {
            invalid = true;
        }

        IsResolved = true;
        var payloadStart = terminator + 2;
        return new Result(true, invalid, address, peer, buffered[payloadStart..].ToArray());
    }

    private static int FindTerminator(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == '\r' && bytes[i + 1] == '\n') return i;
        return -1;
    }

    internal readonly record struct Result(bool Ready, bool Invalid, IPAddress? Address, string? Peer,
        byte[] Payload)
    {
        public static Result NeedMore => new(false, false, null, null, []);
    }
}
