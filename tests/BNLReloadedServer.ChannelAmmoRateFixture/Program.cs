// A channel tool's optional block_ammo_rate is the drain while channelling a block or a
// device; ammo.rate stays the drain on players. The field is read from the catalogue,
// kept on re-serialisation, and never written to clients (their record has no slot).
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

const string Caulk = """
{ "type": "channel", "ammo": { "rate": 1, "ammo_index": 0 }, "auto_switch": true, "range": 6,
  "interval_effects": [], "interval": 0.3, "constant_effects": [], "block_ammo_rate": 0.5 }
""";
const string Plain = """
{ "type": "channel", "ammo": { "rate": 3, "ammo_index": 0 }, "auto_switch": true, "range": 9,
  "interval_effects": [], "interval": 0.15, "constant_effects": [] }
""";

var caulk = Caulk.Deserialize<Tool>() as ToolChannel;
var plain = Plain.Deserialize<Tool>() as ToolChannel;
Check(caulk is not null && plain is not null, "catalogue JSON parses as ToolChannel");
Check(caulk!.BlockAmmoRate == 0.5f, "block_ammo_rate read from catalogue");
Check(plain!.BlockAmmoRate is null, "missing block_ammo_rate is null");

Check(caulk.AmmoRateFor(true) == 1f, "player target drains ammo.rate");
Check(caulk.AmmoRateFor(false) == 0.5f, "block/device target drains block_ammo_rate");
Check(plain.AmmoRateFor(true) == 3f && plain.AmmoRateFor(false) == 3f, "without the field both drain ammo.rate");
Check(new ToolChannel().AmmoRateFor(false) is null, "tool without ammo has no rate");

byte[] Wire(ToolChannel tool)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    Tool.WriteVariant(writer, tool);
    writer.Flush();
    return stream.ToArray();
}
var withoutField = (Caulk.Deserialize<Tool>() as ToolChannel)!;
withoutField.BlockAmmoRate = null;
Check(Wire(caulk).SequenceEqual(Wire(withoutField)), "client wire bytes identical with or without block_ammo_rate");
using (var reader = new BinaryReader(new MemoryStream(Wire(caulk))))
{
    var echoed = Tool.ReadVariant(reader) as ToolChannel;
    Check(echoed is not null && echoed.BlockAmmoRate is null && reader.BaseStream.Position == reader.BaseStream.Length,
        "client record round-trips without the server-only field");
}

var saved = JsonSerializer.Serialize<Tool>(caulk, JsonHelper.DefaultSerializerSettings);
Check(saved.Contains("\"block_ammo_rate\": 0.5"), "re-serialised catalogue keeps block_ammo_rate");
Check(!JsonSerializer.Serialize<Tool>(plain, JsonHelper.DefaultSerializerSettings).Contains("block_ammo_rate"),
    "unset block_ammo_rate is not written");

Console.WriteLine($"BNL_CHANNEL_AMMO_RATE_FIXTURE_OK checks={checks}");
