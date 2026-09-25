using System.IO.Compression;
using System.Net;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Database;

/// <summary>
/// Offline IP-to-country lookup backed by DB-IP Country Lite CSV. Only the resulting ISO code is
/// exposed to game clients; addresses never leave the server process.
/// </summary>
public static class IpCountryLookup
{
    private const string DatabaseEnvironmentVariable = "BNL_IP_COUNTRY_DATABASE";
    private const string DatabaseFileName = "dbip-country-lite.csv.gz";

    private readonly record struct Range(UInt128 Start, UInt128 End, string CountryCode);
    private sealed record Database(Range[] Ipv4, Range[] Ipv6);

    private static readonly Lazy<Database?> LazyDatabase = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? Resolve(IPAddress? address)
    {
        if (address == null || IPAddress.IsLoopback(address)) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        var database = LazyDatabase.Value;
        if (database == null) return null;
        var ranges = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? database.Ipv4
            : database.Ipv6;
        var value = ToUInt128(address);

        var low = 0;
        var high = ranges.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var range = ranges[middle];
            if (value < range.Start) high = middle - 1;
            else if (value > range.End) low = middle + 1;
            else return range.CountryCode;
        }
        return null;
    }

    private static Database? Load()
    {
        var configured = Environment.GetEnvironmentVariable(DatabaseEnvironmentVariable);
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Databases.ConfigsFolderPath, DatabaseFileName)
            : configured;
        if (!File.Exists(path))
        {
            Log.Warn(LogCat.Server, $"IP country database not found at '{path}'; scoreboard flags will be unknown");
            return null;
        }

        try
        {
            var ipv4 = new List<Range>();
            var ipv6 = new List<Range>();
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line)
            {
                var fields = line.Trim().Trim('"').Split("\",\"");
                if (fields.Length != 3 || fields[2].Length != 2 ||
                    !IPAddress.TryParse(fields[0], out var start) ||
                    !IPAddress.TryParse(fields[1], out var end) ||
                    start.AddressFamily != end.AddressFamily)
                    continue;

                var range = new Range(ToUInt128(start), ToUInt128(end), fields[2].ToUpperInvariant());
                if (start.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ipv4.Add(range);
                else ipv6.Add(range);
            }

            Log.Info(LogCat.Server,
                $"Loaded offline IP country database ({ipv4.Count} IPv4 and {ipv6.Count} IPv6 ranges)");
            return new Database(ipv4.ToArray(), ipv6.ToArray());
        }
        catch (Exception exception)
        {
            Log.Error(LogCat.Server, $"Failed to load IP country database '{path}'", exception);
            return null;
        }
    }

    private static UInt128 ToUInt128(IPAddress address)
    {
        UInt128 value = 0;
        foreach (var part in address.GetAddressBytes()) value = (value << 8) | part;
        return value;
    }
}
