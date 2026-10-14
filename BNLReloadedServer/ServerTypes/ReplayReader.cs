using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BNLReloadedServer.ServerTypes;

public sealed record ReplayRecord(byte Kind, uint OffsetMs, byte[] Data);

// Reads a MatchRecorder file. Records are yielded lazily so playback streams a
// long match without holding it all in memory.
public sealed class ReplayReader : IDisposable
{
    private readonly FileStream _file;
    private readonly BrotliStream _compressed;
    private readonly BinaryReader _reader;

    public JsonDocument Metadata { get; }
    public int FormatVersion { get; }

    public ReplayReader(string path)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        _compressed = new BrotliStream(_file, CompressionMode.Decompress);
        _reader = new BinaryReader(_compressed, Encoding.UTF8);
        if (_reader.ReadUInt32() != MatchRecorder.Magic)
            throw new InvalidDataException("Not a replay file.");
        FormatVersion = _reader.ReadInt32();
        if (FormatVersion != MatchRecorder.FormatVersion)
            throw new InvalidDataException($"Unsupported replay format {FormatVersion}.");
        var length = _reader.ReadInt32();
        if (length is < 0 or > 1 << 20) throw new InvalidDataException("Bad replay metadata length.");
        Metadata = JsonDocument.Parse(_reader.ReadBytes(length));
    }

    // Null at the end of the file.
    public ReplayRecord? Next()
    {
        int kind;
        try { kind = _reader.ReadByte(); }
        catch (EndOfStreamException) { return null; }
        var offset = _reader.ReadUInt32();
        var length = _reader.ReadInt32();
        if (length is < 0 or > 64 << 20) throw new InvalidDataException("Bad replay record length.");
        var data = _reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException("Truncated replay record.");
        return new ReplayRecord((byte)kind, offset, data);
    }

    public void Dispose()
    {
        Metadata.Dispose();
        _reader.Dispose();
        _compressed.Dispose();
        _file.Dispose();
    }
}
