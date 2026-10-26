using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

int checks=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++; }
var key=new Key(ParadiseWeatherRegistration.MapId);
var map=Databases.MapDatabase.LoadMapData(key);
Check(map!=null && map.Size.x==256 && map.Size.y==48 && map.Size.z==96,"cloned Paradise loaded by real map database");
Check(map!.Properties?.Render=="DaytimeCold" && map.Units.Count==12 && map.SpawnPoints.Count==2,"cold sunny environment; units and spawns preserved");
Check(map.BlocksData!.UnZip().Length==256L*48*96*6 || map.BlocksData!.UnZip().Length==256L*48*96*5 || map.BlocksData!.UnZip().Length==256L*48*96*4,"terrain payload decodes");
var original=new CardMap{Id=ParadiseWeatherRegistration.OriginalId,Key=new Key(ParadiseWeatherRegistration.OriginalId),Scope=ScopeType.Public,Name=new LocalizedString{Text="Paradise",Data=[]}};
var list=new CardMapList{Id="map_list",Custom=[original.Key],Friendly=[original.Key],Ranked=[original.Key]};
List<Card> cards=[original,list];
ParadiseWeatherRegistration.Register(cards);ParadiseWeatherRegistration.Register(cards);
Check(cards.OfType<CardMap>().Count()==2 && list.Custom.Count==2,"separate id and idempotent custom registration");
Check(list.Friendly.SequenceEqual([original.Key]) && list.Ranked.SequenceEqual([original.Key]) && original.Name.Text=="Paradise","matchmaking pools remain operator-owned");
var start=DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);
Check(ParadiseWeatherRegistration.StartTime(original.Key,start,false)==null,"no weather timestamp on ordinary map");
Check(ParadiseWeatherRegistration.StartTime(key,null,false)==null,"daytime before combat");
Check(ParadiseWeatherRegistration.StartTime(key,start,false)==null,"legacy map weather epoch disabled");
foreach(long? epoch in new long?[]{null,1800000000000})
{
    using var stream=new MemoryStream();
    new ZoneUpdate{WeatherStartTime=epoch,BlockOwnersJson="[]",ConquestStateJson="{}",ResourceCap=3000}.Write(new BinaryWriter(stream));
    stream.Position=0;var read=ZoneUpdate.ReadRecord(new BinaryReader(stream));
    Check(read.WeatherStartTime==epoch && read.ResourceCap==3000 && read.BlockOwnersJson=="[]" && read.ConquestStateJson=="{}" && stream.Position==stream.Length,"wire roundtrip "+epoch);
}
using(var stream=new MemoryStream())
{
    new ZoneUpdate().Write(new BinaryWriter(stream));
    Check(stream.ToArray().SequenceEqual(new byte[]{0,0}),"ordinary empty update retains existing two-byte layout");
}
// Same serialized snapshot for a fresh join, reconnect and a later phase; no local arrival timestamp.
foreach(var phase in new[]{ZonePhaseType.Assault,ZonePhaseType.Build2,ZonePhaseType.Assault2,ZonePhaseType.SuddenDeath})
{
    using var stream=new MemoryStream();
    new ZoneUpdate{Phase=new ZonePhase{PhaseType=phase,StartTime=1800001200000},WeatherStartTime=ParadiseWeatherRegistration.StartTime(key,start,false)}.Write(new BinaryWriter(stream));
    stream.Position=0;var read=ZoneUpdate.ReadRecord(new BinaryReader(stream));
    Check(read.WeatherStartTime==null && read.Phase?.PhaseType==phase,"weather remains disabled in phase "+phase);
}
Console.WriteLine($"PASS {checks} checks");

var emptyPool = new CardMapList { Id="map_list", Custom=[], Friendly=[], Ranked=[] };
List<Card> emptyCards=[emptyPool];
ParadiseWeatherRegistration.Register(emptyCards); ParadiseWeatherRegistration.Register(emptyCards);
Check(emptyCards.OfType<CardMap>().Count()==2 && emptyPool.Friendly.Count==0 && emptyPool.Custom.Count==2 && emptyPool.Ranked.Count==0,"register both maps once when original is absent");

foreach(bool enabled in new[]{false,true}) {
    var configured = new CardMap { Id="map_sr2_eco_city", DynamicWeather=enabled };
    using var bytes=new MemoryStream(); configured.Write(new BinaryWriter(bytes));
    var encoded=bytes.ToArray(); bytes.Position=0;
    var decoded=CardMap.ReadRecord(new BinaryReader(bytes));
    Check(decoded.DynamicWeather==enabled && decoded.Id==configured.Id && bytes.Position==bytes.Length,"per-map setting wire roundtrip "+enabled);
    var legacyHeader=(byte)(encoded[0]&0xfe);encoded[0]=legacyHeader;
    using var legacy=new MemoryStream(encoded);
    Check(!CardMap.ReadRecord(new BinaryReader(legacy)).DynamicWeather,"legacy header defaults disabled");
    Check(ParadiseWeatherRegistration.StartTime(new Key(configured.Id),start,enabled)==null,"arbitrary map epoch "+enabled);
    Check(ParadiseWeatherRegistration.StartTime(new Key(configured.Id),null,enabled)==null,"no clock before first assault");
    var json=System.Text.Json.JsonSerializer.Serialize(configured);
    Check(System.Text.Json.JsonSerializer.Deserialize<CardMap>(json)!.DynamicWeather==enabled,"JSON setting roundtrip");
}
var off=new CardMap{Id="map_sr2_eco_city"};var on=new CardMap{Id=off.Id,DynamicWeather=true};
using var oldBytes=new MemoryStream();using var newBytes=new MemoryStream();
off.Write(new BinaryWriter(oldBytes));on.Write(new BinaryWriter(newBytes));
Check(oldBytes.Length==newBytes.Length && oldBytes.ToArray().Skip(1).SequenceEqual(newBytes.ToArray().Skip(1)) && (oldBytes.ToArray()[0]^newBytes.ToArray()[0])==1,"only unused header bit changes; legacy payload length/layout preserved");
Check(!ParadiseWeatherRegistration.IsEnabled(null,true),"missing map cannot enable weather");
Console.WriteLine($"PASS {checks} checks including configurable map weather");

Check(JsonSerializer.Deserialize<CardMap>("{\"dynamic_weather\":true}")!.DynamicWeather,"CDB snake-case setting");
