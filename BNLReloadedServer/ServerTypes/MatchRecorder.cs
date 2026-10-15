using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.ServerTypes;

// Records one public or ranked match as a spectator would receive it: a
// keyframe (the InitZone packet, then everything a joining spectator gets) and
// then every zone broadcast packet and look-stream relay, each with its offset
// from the keyframe. Packets are copied by reference into a channel and
// compressed to disk on a background task, so the zone thread only enqueues.
//
// File (Brotli-compressed): "BNLR" magic, int format version, int metadata
// length + UTF-8 JSON, then records of { byte kind, uint offset ms, int length,
// bytes }. Kinds: 1 InitZone, 2 load snapshot, 3 stream, 255 end (JSON footer).
public sealed class MatchRecorder
{
    public const uint Magic = 0x524C4E42; // "BNLR"
    public const int FormatVersion = 1;
    public const byte KindInitZone = 1;
    public const byte KindLoad = 2;
    public const byte KindStream = 3;
    public const byte KindEnd = 255;

    private readonly Channel<(byte Kind, uint Offset, byte[] Data)> _records =
        Channel.CreateUnbounded<(byte, uint, byte[])>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _partPath;
    private readonly string _finalPath;
    private readonly Task _writer;
    private long _bytes;
    private int _packets;
    private int _finished;

    public string MatchId { get; }
    public long StartedAtMs { get; }
    // Completes once the file has been finalized or discarded.
    public Task Completion => _writer;

    private MatchRecorder(string matchId, long startedAtMs, string partPath, string finalPath, object metadata)
    {
        MatchId = matchId;
        StartedAtMs = startedAtMs;
        _partPath = partPath;
        _finalPath = finalPath;
        _writer = Task.Run(() => WriteAsync(metadata));
    }

    public static MatchRecorder? TryStart(string matchId, long startedAtMs, object metadata)
    {
        try
        {
            var directory = ReplayStore.Directory;
            System.IO.Directory.CreateDirectory(directory);
            var name = ReplayStore.FileNameFor(matchId);
            return new MatchRecorder(matchId, startedAtMs,
                Path.Combine(directory, name + ".part"), Path.Combine(directory, name), metadata);
        }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay recording unavailable for {matchId}: {exception.Message}");
            return null;
        }
    }

    public uint OffsetMs => (uint)Math.Min(uint.MaxValue, _clock.ElapsedMilliseconds);

    public void RecordKeyframe(byte[] initZone, byte[] load)
    {
        Enqueue(KindInitZone, initZone);
        Enqueue(KindLoad, load);
    }

    public void RecordStream(byte[] framedPackets) => Enqueue(KindStream, framedPackets);

    private void Enqueue(byte kind, byte[] data)
    {
        if (Volatile.Read(ref _finished) != 0 || data.Length == 0) return;
        Interlocked.Add(ref _bytes, data.Length);
        Interlocked.Increment(ref _packets);
        _records.Writer.TryWrite((kind, OffsetMs, data));
    }

    // Completes the file and makes it visible to the store.
    public void Finish(long endedAtMs, string endReason)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        var footer = JsonSerializer.SerializeToUtf8Bytes(new
        {
            match_id = MatchId,
            ended_at = endedAtMs,
            duration_ms = OffsetMs,
            end_reason = endReason,
            packets = Volatile.Read(ref _packets),
            raw_bytes = Interlocked.Read(ref _bytes)
        });
        _records.Writer.TryWrite((KindEnd, OffsetMs, footer));
        _records.Writer.TryComplete();
    }

    // Drops an unfinished recording (match torn down without an archived result).
    public void Abort()
    {
        if (Interlocked.Exchange(ref _finished, 2) != 0) return;
        _records.Writer.TryComplete();
    }

    private async Task WriteAsync(object metadata)
    {
        var completed = false;
        try
        {
            await using (var file = new FileStream(_partPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
            await using (var compressed = new BrotliStream(file, CompressionLevel.Fastest))
            await using (var writer = new BinaryWriter(compressed, Encoding.UTF8, leaveOpen: true))
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(metadata);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(json.Length);
                writer.Write(json);
                await foreach (var (kind, offset, data) in _records.Reader.ReadAllAsync())
                {
                    writer.Write(kind);
                    writer.Write(offset);
                    writer.Write(data.Length);
                    writer.Write(data);
                    if (kind == KindEnd) completed = true;
                }
                writer.Flush();
            }

            if (completed && Volatile.Read(ref _finished) == 1)
            {
                File.Move(_partPath, _finalPath, overwrite: true);
                var size = new FileInfo(_finalPath).Length;
                Log.Info(LogCat.Match, $"Replay saved: {MatchId}, {OffsetMs / 1000}s, " +
                                       $"{Volatile.Read(ref _packets)} records, raw {Interlocked.Read(ref _bytes) / 1024} KiB, " +
                                       $"file {size / 1024} KiB");
                ReplayStore.EnforceLimits();
            }
            else
            {
                File.Delete(_partPath);
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay recording failed for {MatchId}: {exception.Message}");
            try { File.Delete(_partPath); } catch { /* best effort */ }
        }
    }
}

// Where replays live and how long they stay. Files older than the retention
// window go first; past the size cap, the oldest are removed until it fits.
public static class ReplayStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan StalePart = TimeSpan.FromHours(6);
    private static readonly object CleanupLock = new();

    public static string Directory { get; } =
        Environment.GetEnvironmentVariable("BNL_REPLAY_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(BNLReloadedServer.Database.Databases.BaseFolderPath, "Replays");

    public static long MaxBytes { get; } =
        (long)((double.TryParse(Environment.GetEnvironmentVariable("BNL_REPLAY_MAX_GB"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb) && gb > 0
            ? gb : 5.0) * 1024 * 1024 * 1024);

    public const string Extension = ".bnlreplay";

    // Match ids are "{instanceId}:{startedMs}"; ':' is not a portable file-name character.
    public static string FileNameFor(string matchId)
    {
        var safe = new StringBuilder(matchId.Length);
        foreach (var c in matchId)
            safe.Append(char.IsLetterOrDigit(c) || c is '-' ? c : '_');
        return safe + Extension;
    }

    public static string? PathFor(string matchId)
    {
        var path = Path.Combine(Directory, FileNameFor(matchId));
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= Retention ? path : null;
    }

    public static bool Exists(string matchId) => PathFor(matchId) != null;

    // Replays are entered through the spectate request, whose id is a ulong. The
    // id is a 63-bit FNV-1a hash of the replay file name with the top bit set,
    // separating it from small custom-game ids. Live matchmaking browser ids
    // also use the high bit: spectate dispatch must check the live registry
    // first. The client computes the same value from a match id.
    public static ulong ReplayIdFor(string matchId) => ReplayIdForFileName(FileNameFor(matchId));

    public static bool IsReplayId(ulong id) => (id & 0x8000_0000_0000_0000UL) != 0;

    private static ulong ReplayIdForFileName(string fileName)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(fileName))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return 0x8000_0000_0000_0000UL | (hash & 0x7FFF_FFFF_FFFF_FFFFUL);
    }

    public static bool TryResolve(ulong replayId, out string path, out string matchId)
    {
        path = string.Empty;
        matchId = string.Empty;
        if (!IsReplayId(replayId) || !System.IO.Directory.Exists(Directory)) return false;
        var now = DateTime.UtcNow;
        foreach (var file in new DirectoryInfo(Directory).EnumerateFiles("*" + Extension))
        {
            if (now - file.LastWriteTimeUtc > Retention || ReplayIdForFileName(file.Name) != replayId) continue;
            path = file.FullName;
            try
            {
                using var reader = new ReplayReader(path);
                matchId = reader.Metadata.RootElement.GetProperty("match_id").GetString() ?? file.Name;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        return false;
    }

    public static void Start(CancellationToken stop)
    {
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                EnforceLimits();
                try { await Task.Delay(TimeSpan.FromHours(1), stop); }
                catch (OperationCanceledException) { return; }
            }
        }, stop);
    }

    public static void EnforceLimits()
    {
        lock (CleanupLock)
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return;
                var now = DateTime.UtcNow;
                var replays = new List<FileInfo>();
                foreach (var file in new DirectoryInfo(Directory).EnumerateFiles())
                {
                    var age = now - file.LastWriteTimeUtc;
                    if (file.Name.EndsWith(".part", StringComparison.Ordinal))
                    {
                        if (age > StalePart) Delete(file, "stale partial");
                    }
                    else if (file.Name.EndsWith(Extension, StringComparison.Ordinal))
                    {
                        if (age > Retention) Delete(file, "expired");
                        else replays.Add(file);
                    }
                }

                var total = replays.Sum(file => file.Length);
                foreach (var file in replays.OrderBy(file => file.LastWriteTimeUtc))
                {
                    if (total <= MaxBytes) break;
                    total -= file.Length;
                    Delete(file, "over size cap");
                }
            }
            catch (Exception exception)
            {
                Log.Warn(LogCat.Match, $"Replay cleanup failed: {exception.Message}");
            }
        }
    }

    private static void Delete(FileInfo file, string reason)
    {
        try
        {
            file.Delete();
            Log.Info(LogCat.Match, $"Replay removed ({reason}): {file.Name}");
        }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay removal failed for {file.Name}: {exception.Message}");
        }
    }
}
