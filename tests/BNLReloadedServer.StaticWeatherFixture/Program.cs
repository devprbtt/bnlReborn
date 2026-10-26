using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
foreach(var id in new[]{"map_sr2_eco_city",ParadiseWeatherRegistration.MapId,"map_tutorial","map_custom"})
foreach(var configured in new[]{false,true})
{
    var map=new CardMap{Id=id,Key=new Key(id),DynamicWeather=configured};
    StaticMapWeather.Apply(map);
    Check(!map.DynamicWeather,"catalogue dynamic flag cleared: "+id);
    Check(!ParadiseWeatherRegistration.IsEnabled(map.Key,configured),"stale flag cannot enable weather: "+id);
    Check(ParadiseWeatherRegistration.StartTime(map.Key,DateTimeOffset.UtcNow,configured)==null,"no weather epoch: "+id);
    using var bytes=new MemoryStream();map.Write(new BinaryWriter(bytes));bytes.Position=0;
    Check(!CardMap.ReadRecord(new BinaryReader(bytes)).DynamicWeather,"disabled flag reaches clients");
}
foreach(var old in new[]{"DaytimeWarm","Night","Sunset","Overcast","DaytimeCold","", "unknown"})
{
    var map=new MapData{Properties=new MapDataProps{Render=old,AudioAmbience="ambient_test",Plane="WaterPlane",PlanePosition=42}};
    Check(ReferenceEquals(map,StaticMapWeather.Apply(map))&&map.Properties.Render=="DaytimeCold","all render presets become Cold Sunny");
    Check(map.Properties.AudioAmbience=="ambient_test"&&map.Properties.PlanePosition==42&&map.Properties.Plane=="WaterPlane","unrelated map properties preserved");
}
var winter=new MapData{Properties=new MapDataProps{Render="DaytimeWarm_winter"}};StaticMapWeather.Apply(winter);
Check(winter.Properties.Render=="DaytimeCold_winter","winter terrain keeps its theme with cold daylight");
Check(StaticMapWeather.Apply((MapData?)null)==null,"missing map remains missing");
var empty=new MapData();StaticMapWeather.Apply(empty);Check(empty.Properties?.Render=="DaytimeCold","maps without properties get fixed preset");
var legacy=new CardMap{Id=ParadiseWeatherRegistration.MapId,DynamicWeather=true};StaticMapWeather.Apply(legacy);
Check(legacy.Name?.Text=="Paradise"&&!legacy.Description!.Text!.Contains("night"),"legacy map no longer advertises dynamic weather");
Console.WriteLine($"STATIC_WEATHER_FIXTURE_OK checks={checks}");
