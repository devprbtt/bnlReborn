using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class ParadiseWeatherRegistration
{
    public const string OriginalId = "map_sr2_paradise";
    public const string MapId = "map_sr2_paradise_weather_test";
    public static bool IsEnabled(Key? map, bool dynamicWeather = false) => false;
    public static long? StartTime(Key? map, DateTimeOffset? assaultStart) =>
        StartTime(map, assaultStart, map.HasValue && Databases.Catalogue.GetCard<CardMap>(map.Value)?.DynamicWeather == true);
    public static long? StartTime(Key? map, DateTimeOffset? assaultStart, bool dynamicWeather) =>
        IsEnabled(map, dynamicWeather) ? assaultStart?.ToUnixTimeMilliseconds() : null;

    public static void Register(List<Card> cards)
    {
        var key = new Key(MapId);
        if (!Databases.MapDatabase.HasMap(key)) return;
        var original = cards.OfType<CardMap>().FirstOrDefault(c => c.Id == OriginalId);
        if (original == null && Databases.MapDatabase.HasMap(new Key(OriginalId)))
        {
            original = new CardMap {
                Id = OriginalId, Key = new Key(OriginalId), Scope = ScopeType.Public,
                Name = new LocalizedString { Text = "Paradise", Data = [] },
                Description = new LocalizedString { Text = "Swim, jump, dig, and build your way to victory. Two Cubes and a Base.", Data = [] },
                Image = "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg",
                LargeImage = "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg"
            };
            cards.Add(original);
        }
        if (!cards.Any(c => c.Id == MapId))
            cards.Add(new CardMap {
                Id = MapId, Key = key, Scope = original?.Scope ?? ScopeType.Public,
                Name = new LocalizedString { Text = "Paradise", Data = [] },
                Description = new LocalizedString { Text = "Clear, cold daylight throughout the match.", Data = [] },
                Image = original?.Image ?? "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg",
                LargeImage = original?.LargeImage ?? "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg"
            });
        foreach (var list in cards.OfType<CardMapList>())
        {
            list.Custom ??= [];
            if (!list.Custom.Contains(key)) list.Custom.Add(key);
            if (original != null && !list.Custom.Contains(original.Key)) list.Custom.Add(original.Key);
            // Matchmaking rotation is operator-owned; never re-add removed maps.
        }
    }
}
