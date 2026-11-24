using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

if (args.Length != 1) throw new Exception("Pass the path to a public JSON card catalogue; run from server repository root.");
using var catalogue = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var cards = catalogue.RootElement.EnumerateArray()
    .Where(c => c.TryGetProperty("category", out _))
    .Select(c => JsonSerializer.Deserialize<Card>(c.GetRawText(), JsonHelper.DefaultSerializerSettings)!).ToList();
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
int checks = 0, totalLocked = 0, totalMetal = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
MapData Read(string path) => JsonSerializer.Deserialize<MapCustomData>(File.ReadAllBytes(path).UnZip(),
    JsonHelper.DefaultSerializerSettings)!.Map!;
var updater = new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => false);
foreach (var name in new[] { "aztec_temple", "citadel", "insandity", "parapet" })
{
    string file = "map_sr2_" + name + ".bnlbin";
    var data = Read(Path.Combine("Maps", file));
    var source = Read(Path.Combine("extracted/one-cube-protection-inputs", file));
    // Use the actual map decoder, stability initialization, and client snapshot encoder.
    var before = new MapBinary(source.Schema, source.BlocksData!, source.Size, source.Properties!.PlanePosition, updater);
    var after = new MapBinary(data.Schema, data.BlocksData!, data.Size, data.Properties!.PlanePosition, updater);
    var clientSnapshot = new MapBinary(after.ToBinary(), data.Properties.PlanePosition, updater);
    int locked = 0, metal = 0;
    for (int x = 0; x < after.SizeX; x++)
    for (int y = 0; y < after.SizeY; y++)
    for (int z = 0; z < after.SizeZ; z++)
    {
        var old = before[x, y, z];
        var cell = after[x, y, z];
        var wire = clientSnapshot[x, y, z];
        Check(cell.Equals(wire), file + " client snapshot changed cell");
        if (old.Equals(cell)) continue;
        if (cell.Id == 59)
        {
            Check(old.IsAir && cell.IsLocked && !cell.IsSolid && cell.IsPassable && !cell.IsReplaceable,
                file + " no-build fill must reject building without blocking movement");
            locked++;
        }
        else
        {
            Check(name == "insandity" && cell.Id == 10 && old.Id is 0 or 2 && cell.IsSolid &&
                  !cell.Card.Destructible && cell.Card.CanFloat, file + " foundation must be standard indestructible metal");
            metal++;
        }
    }
    foreach (var cube in data.Units.Where(u => u.UnitKey == new Key("unit_base_shield_rush")))
    {
        for (int x = (int)cube.Position.X; x < cube.Position.X + 4; x++)
        for (int z = (int)cube.Position.Z; z < cube.Position.Z + 4; z++)
            Check(after[x, (int)cube.Position.Y - 1, z].Id == 10, file + " missing cube support");
    }
    Check(locked == (name switch { "aztec_temple" => 200, "citadel" => 532, "insandity" => 232, _ => 6 }),
        file + " wrong room fill count after stability initialization");
    Check(metal == (name == "insandity" ? 16 : 0), file + " unexpected metal edits");
    totalLocked += locked; totalMetal += metal;
    Console.WriteLine($"PASS {file}: {locked} no-build cells, {metal} metal cells; full runtime/client snapshot matches");
}
Console.WriteLine($"PASS {checks} checks; {totalLocked} no-build cells and {totalMetal} foundation cells.");
