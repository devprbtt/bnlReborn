using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

int checks=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++; }
var key=new Key(ParadiseWeatherRegistration.MapId);
var map=Databases.MapDatabase.LoadMapData(key);
Check(map!=null && map.Size.x==256 && map.Size.y==48 && map.Size.z==96,"cloned Paradise loaded by real map database");
Check(map!.Properties?.Render=="DaytimeWarm" && map.Units.Count==12 && map.SpawnPoints.Count==2,"original environment, units and spawns preserved");
Check(map.BlocksData!.UnZip().Length==256L*48*96*6 || map.BlocksData!.UnZip().Length==256L*48*96*5 || map.BlocksData!.UnZip().Length==256L*48*96*4,"terrain payload decodes");
var original=new CardMap{Id=ParadiseWeatherRegistration.OriginalId,Key=new Key(ParadiseWeatherRegistration.OriginalId),Scope=ScopeType.Public,Name=new LocalizedString{Text="Paradise",Data=[]}};
var list=new CardMapList{Id="map_list",Custom=[original.Key],Friendly=[original.Key],Ranked=[original.Key]};
List<Card> cards=[original,list];
ParadiseWeatherRegistration.Register(cards);ParadiseWeatherRegistration.Register(cards);
Check(cards.OfType<CardMap>().Count()==2 && list.Custom.Count==2,"separate id and idempotent custom registration");
Check(list.Friendly.SequenceEqual([original.Key]) && list.Ranked.SequenceEqual([original.Key]) && original.Name.Text=="Paradise","ordinary map and matchmaking pools untouched");
var start=DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);
Check(ParadiseWeatherRegistration.StartTime(original.Key,start)==null,"no weather timestamp on ordinary map");
Check(ParadiseWeatherRegistration.StartTime(key,null)==null,"daytime before combat");
Check(ParadiseWeatherRegistration.StartTime(key,start)==1800000000000,"combat epoch preserved");
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
    new ZoneUpdate{Phase=new ZonePhase{PhaseType=phase,StartTime=1800001200000},WeatherStartTime=ParadiseWeatherRegistration.StartTime(key,start)}.Write(new BinaryWriter(stream));
    stream.Position=0;var read=ZoneUpdate.ReadRecord(new BinaryReader(stream));
    Check(read.WeatherStartTime==1800000000000 && read.Phase?.PhaseType==phase,"epoch independent of phase "+phase);
}
Console.WriteLine($"PASS {checks} checks");
