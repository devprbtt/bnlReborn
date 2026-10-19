using System.Collections.Concurrent;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Database;

public class MapDatabase : IMapDatabase
{
    private const string MapExtension = ".bnlbin";

    private static string MapPath { get; } = Path.Combine(Databases.BaseFolderPath, "Maps");

    private readonly ConcurrentDictionary<Key, string> _maps = Scan();
    private readonly ConcurrentDictionary<Key, (long Modified, long Length, int Count)> _cubeCounts = new();

    public int GetCubeCount(Key key)
    {
        if (!_maps.TryGetValue(key, out var path)) return 0;
        var file = new FileInfo(path);
        if (!file.Exists) return 0;
        long modified = file.LastWriteTimeUtc.Ticks, length = file.Length;
        if (_cubeCounts.TryGetValue(key, out var cached) && cached.Modified == modified && cached.Length == length)
            return cached.Count;
        var data = LoadMapData(key);
        if (data == null) return 0;
        int count = CasualMapPool.CountCubes(data, k => k.GetCard<CardUnit>());
        _cubeCounts[key] = (modified, length, count);
        return count;
    }

    private static ConcurrentDictionary<Key, string> Scan()
    {
        var maps = new ConcurrentDictionary<Key, string>();
        if (!Directory.Exists(MapPath)) return maps;

        foreach (var file in Directory.GetFiles(MapPath, $"*{MapExtension}", SearchOption.TopDirectoryOnly))
        {
            maps[new Key(Path.GetFileNameWithoutExtension(file))] = file;
        }

        return maps;
    }

    public List<string> GetMapIds() => _maps.Values.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();

    public bool HasMap(Key key) => _maps.ContainsKey(key);

    private MapCustomData? LoadCustomData(Key key)
    {
        if (!_maps.TryGetValue(key, out var mapFile) || !File.Exists(mapFile)) return null;

        try
        {
            var json = File.ReadAllBytes(mapFile).UnZip();
            return JsonSerializer.Deserialize<MapCustomData>(json, JsonHelper.DefaultSerializerSettings);
        }
        catch (Exception e)
        {
            Log.Error(LogCat.Map, $"Failed to read '{mapFile}'", e);
            return null;
        }
    }

    public MapData? LoadMapData(Key key) => LoadCustomData(key)?.Map;

    private static bool IsValidMapKey(string key) =>
        key.Length is > 0 and <= 128 &&
        key[0] != '.' &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    public void SaveMap(string key, MapData mapData)
    {
        if (!IsValidMapKey(key)) throw new ArgumentException($"Invalid map key '{key}'", nameof(key));

        Directory.CreateDirectory(MapPath);
        var mapKey = new Key(key);
        var mapFile = Path.Combine(MapPath, key + MapExtension);

        var customData = LoadCustomData(mapKey) ?? new MapCustomData
        {
            Name = key,
            Description = string.Empty,
            MapId = key
        };
        customData.Map = mapData;
        customData.MapId = key;

        var json = JsonSerializer.SerializeToUtf8Bytes(customData, JsonHelper.DefaultSerializerSettings);
        using (var fs = File.Create(mapFile))
        {
            using var zipped = json.Zip(0);
            zipped.CopyTo(fs);
        }

        _maps[mapKey] = mapFile;
        _cubeCounts.TryRemove(mapKey, out _);

        Log.Info(LogCat.Map, $"Saved map '{key}' to {mapFile}");

        if (mapKey.GetCard<CardMap>() == null)
        {
            Log.Warn(LogCat.Map, $"Map '{key}' has no CardMap in the catalogue — add one in CouchDB or it will never be playable");
        }
    }
}
