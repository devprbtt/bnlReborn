using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class ParadiseWeatherRegistration
{
    public const string OriginalId = "map_sr2_paradise";
    public const string MapId = "map_sr2_paradise_weather_test";
    public static bool IsEnabled(Key? map) => map == new Key(MapId);
    public static long? StartTime(Key? map, DateTimeOffset? assaultStart) =>
        IsEnabled(map) ? assaultStart?.ToUnixTimeMilliseconds() : null;

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
                Name = new LocalizedString { Text = "Paradise - Dynamic Weather", Data = [] },
                Description = new LocalizedString { Text = "Day to night in 20 minutes; daylight returns at 25 minutes.", Data = [] },
                Image = original?.Image ?? "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg",
                LargeImage = original?.LargeImage ?? "https://i.gyazo.com/5b50a11de845b9faad464a8593eace49.jpg"
            });
        foreach (var list in cards.OfType<CardMapList>())
        {
            list.Custom ??= [];
            if (!list.Custom.Contains(key)) list.Custom.Add(key);
            if (original != null && !list.Custom.Contains(original.Key)) list.Custom.Add(original.Key);
            list.Friendly ??= [];
            if (original != null && !list.Friendly.Contains(original.Key)) list.Friendly.Add(original.Key);
            if (!list.Friendly.Contains(key)) list.Friendly.Add(key);
        }
    }
}
