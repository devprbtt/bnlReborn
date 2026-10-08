using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

MapCustomData Load(string path) => JsonSerializer.Deserialize<MapCustomData>(File.ReadAllBytes(path).UnZip(), JsonHelper.DefaultSerializerSettings)!;
var original = Load(args[0]);
var winter = Load(args[1]);
if (winter.MapId != "map_sr2_paradise_winter" || winter.Name != "Paradise - Winter") throw new Exception("Wrong identity");
if (winter.Map?.Properties?.Render != "DaytimeWarm_winter") throw new Exception("Missing winter trigger");
using var stream = new MemoryStream();
using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) winter.Write(writer);
stream.Position = 0;
var decoded = MapCustomData.ReadRecord(new BinaryReader(stream));
if (stream.Position != stream.Length || decoded.Map?.Properties?.Render != "DaytimeWarm_winter") throw new Exception("Wire roundtrip failed");
if (decoded.MapId != winter.MapId || decoded.Name != winter.Name) throw new Exception("Wire identity failed");
winter.Map.Properties.Render = original.Map!.Properties!.Render;
if (JsonSerializer.Serialize(winter.Map, JsonHelper.DefaultSerializerSettings) != JsonSerializer.Serialize(original.Map, JsonHelper.DefaultSerializerSettings)) throw new Exception("Gameplay differs");
Console.WriteLine("WINTER_MAP_OK identity, render trigger, native deserialization, full wire roundtrip, unchanged gameplay");
