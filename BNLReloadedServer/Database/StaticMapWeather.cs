using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class StaticMapWeather
{
    public const string Preset="DaytimeCold";

    public static void Apply(Card card)
    {
        if(card is not CardMap map)return;
        map.DynamicWeather=false;
        if(map.Id==ParadiseWeatherRegistration.MapId)
        {
            map.Name=new LocalizedString{Text="Paradise",Data=[]};
            map.Description=new LocalizedString{Text="Clear, cold daylight throughout the match.",Data=[]};
        }
    }

    public static MapData? Apply(MapData? map)
    {
        if(map is null)return null;
        map.Properties??=new MapDataProps();
        // Preserve the winter terrain theme while selecting the cold-sunny base preset.
        map.Properties.Render=map.Properties.Render?.EndsWith("_winter",StringComparison.OrdinalIgnoreCase)==true ? Preset+"_winter" : Preset;
        return map;
    }
}
