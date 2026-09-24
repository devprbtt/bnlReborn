using BNLReloadedServer.BaseTypes;
using System.Text.Json;

namespace BNLReloadedServer.Database;

/// <summary>Controls authenticated ownership of unpublished authoring skins.</summary>
public static class PrivateSkinAccess
{
    public const ulong TestSteamId = 76561197990315750;
    public static readonly Key ArcticWolf = new("skin_hunter_arctic_wolf_private");
    public static readonly Key Demon = new("skin_boxer_demon_private");
    private static readonly Key Hunter = new("unit_hero_hunter");
    private static readonly Key Boxer = new("unit_hero_boxer");
    private static readonly Lock GrantLock = new();
    private static IReadOnlyDictionary<uint, HashSet<ulong>> _fileGrants = new Dictionary<uint, HashSet<ulong>>();
    private static string? _loadedPath;
    private static long _loadedWriteTicks;
    private static long _loadedLength = -1;
    private static bool _loaded;

    public static bool IsPrivate(Key skin) => skin == ArcticWolf || skin == Demon;
    public static bool CanUse(ulong? steamId, Key skin)
    {
        if (!IsPrivate(skin)) return true;
        if (steamId is null or 0) return false;
        if (steamId == TestSteamId) return true;

        RefreshFileGrants();
        var grants = _fileGrants;
        return grants.TryGetValue(skin.Hash, out var owners) && owners.Contains(steamId.Value);
    }

    public static bool CanEquip(ulong? steamId, Key hero, Key skin) => CanUse(steamId, skin) &&
        (skin != ArcticWolf || hero == Hunter) && (skin != Demon || hero == Boxer);

    public static Key SafeSkin(ulong? steamId, Key hero, Key skin)
    {
        if (CanEquip(steamId, hero, skin)) return skin;
        return (hero.GetCard<CardUnit>()?.Data as UnitDataPlayer)?.Skins?
            .FirstOrDefault(candidate => !IsPrivate(candidate)) ?? Key.None;
    }

    private static string GrantFilePath =>
        Environment.GetEnvironmentVariable("BNL_PRIVATE_SKIN_GRANTS_PATH") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Databases.ConfigsFolderPath, "private_skin_grants.json");

    private static void RefreshFileGrants()
    {
        var path = GrantFilePath;
        lock (GrantLock)
        {
            var info = new FileInfo(path);
            var exists = info.Exists;
            var writeTicks = exists ? info.LastWriteTimeUtc.Ticks : 0;
            var length = exists ? info.Length : -1;
            if (_loaded && string.Equals(_loadedPath, path, StringComparison.Ordinal) &&
                _loadedWriteTicks == writeTicks && _loadedLength == length)
            {
                return;
            }

            _loaded = true;
            _loadedPath = path;
            _loadedWriteTicks = writeTicks;
            _loadedLength = length;
            if (!exists)
            {
                _fileGrants = new Dictionary<uint, HashSet<ulong>>();
                return;
            }

            try
            {
                var document = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? [];
                var loaded = new Dictionary<uint, HashSet<ulong>>();
                foreach (var (skinId, owners) in document)
                {
                    var skin = new Key(skinId);
                    if (!IsPrivate(skin)) continue;
                    loaded[skin.Hash] = owners
                        .Select(value => ulong.TryParse(value, out var steamId) ? steamId : 0)
                        .Where(steamId => steamId != 0)
                        .ToHashSet();
                }
                _fileGrants = loaded;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Preserve the last valid snapshot. Deployments replace the file atomically, so a bad
                // candidate cannot revoke existing access or grant partially parsed access.
                Console.Error.WriteLine($"Unable to reload private skin grants from '{path}': {exception.Message}");
            }
        }
    }
}
