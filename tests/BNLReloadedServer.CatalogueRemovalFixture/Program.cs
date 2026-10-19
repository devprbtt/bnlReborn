// A live CDB delete removed Heavier Metal while players held it: their inventory came from login, so
// every later player update threw "Invalid card id" on the client. Removing a card must first rebuild
// each online inventory and strip the card from cached and saved loadouts, then tell the clients.
// Usage: dotnet run -- <live catalogue dump json>
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Service;
using SQLite;

var root = Path.Combine(Path.GetTempPath(), "bnl-catalogue-removal-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "PlayerData"));
Directory.CreateDirectory(Path.Combine(root, "Configs"));
Directory.SetCurrentDirectory(root); // Databases resolves every path from the working directory on first use
File.WriteAllText(Path.Combine(root, "Configs", "configs.json"),
    """{"master_host":"127.0.0.1","master_public_host":"127.0.0.1","region_name":"fixture","region_icon":"fixture"}""");

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
Check(Databases.PlayerDatabaseFile.StartsWith(root), "fixture database is isolated in a temp folder");

const string Removed = "perk_hero_cogwheel_heavier_metal";
var removed = new Key(Removed); var pusher = new Key("perk_hero_cogwheel_pusher"); var cogwheel = new Key("unit_hero_cogwheel");
var docs = JsonNode.Parse(File.ReadAllText(args[0]))!.AsArray().Select(n => n!.AsObject()).Where(d => d.ContainsKey("category")).ToList();
foreach (var doc in docs) doc.Remove("_rev");
Card Load(JsonObject doc) => JsonSerializer.Deserialize<Card>(doc.ToJsonString(), JsonHelper.DefaultSerializerSettings)!;
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(docs.Select(Load).ToList());
Check(removed.GetCard<CardPerk>() != null && cogwheel.GetCard<CardUnit>() != null, "live catalogue holds Heavier Metal and Cogwheel");

// Players: one online holding the perk, one offline holding it.
var db = new MasterServerDatabase();
var Online = (await db.AddPlayer(76561197990315750, "online", "fixture")).PlayerId;
var Offline = (await db.AddPlayer(76561198366278223, "offline", "fixture")).PlayerId;
var cogDefault = Databases.PlayerDatabase.GetLoadoutForHero(Online, cogwheel, true);
foreach (var id in new[] { Online, Offline })
    Check(await db.SetLoadoutForPlayer(id, cogwheel, new LobbyLoadout
          { HeroKey = cogwheel, SkinKey = cogDefault.SkinKey, Devices = cogDefault.Devices, Perks = [removed, pusher] }),
        $"player {id} saves a Cogwheel loadout with Heavier Metal");

var events = new List<string>();
var pushed = new List<(uint player, List<InventoryItem> inventory)>();
var region = DispatchProxy.Create<IRegionServerDatabase, Recorder>();
((Recorder)(object)region).OnCall = (m, a) =>
{
    if (m.Name != "NotifyInventory") return;
    pushed.Add(((uint)a![0]!, (List<InventoryItem>)a[1]!));
    events.Add("inventory " + a[0]);
};
Databases.SetRegionDatabase(region);
var players = (ConcurrentDictionary<uint, PlayerData>)typeof(PlayerDatabase).GetField("_players", Any)!.GetValue(Databases.PlayerDatabase)!;
players[Online] = (await db.GetPlayer(Online))!;
var getInventory = typeof(PlayerDatabase).GetMethod("GetInventory", Any)!;
List<Key> Inventory(uint id) => ((List<InventoryItem>)getInventory.Invoke(Databases.PlayerDatabase, [id])!).Select(i => i.Item).ToList();
Check(Inventory(Online).Contains(removed), "the login inventory holds Heavier Metal");

// What tools/remove_hero_perks_burning_lamps.py does: unlist the perk from global_logic, then delete it.
var global = docs.Single(d => (string?)d["_id"] == "global_logic").DeepClone().AsObject();
var heroPerks = global["perks"]!["heroes"]!["unit_hero_cogwheel"]!.AsArray();
heroPerks.Remove(heroPerks.Single(p => (string?)p == Removed));
catalogue.UpdateCard(Load(global));
Check(pushed.Count == 0, "unlisting alone sends nothing, so the client still holds the perk");

Check(!CatalogueRemoval.Apply(catalogue, "perk_does_not_exist", id => events.Add("broadcast " + id)) && events.Count == 0,
    "removing an unknown id neither refreshes nor broadcasts");

Check(CatalogueRemoval.Apply(catalogue, Removed, id => events.Add("broadcast " + id)), "the perk is removed");
Check(removed.GetCard<CardPerk>() == null, "the server catalogue no longer has the perk");
Check(events.SequenceEqual([$"inventory {Online}", "broadcast " + Removed]),
    $"the online inventory is pushed before the removal is broadcast ({string.Join(", ", events)})");
Check(pushed.All(p => p.player != Offline), "offline players are not sent anything");
var sent = pushed.Single().inventory.Select(i => i.Item).ToList();
Check(!sent.Contains(removed), "the pushed inventory no longer holds Heavier Metal");
Check(sent.Contains(pusher) && sent.Contains(cogwheel), "the pushed inventory keeps the other perks and heroes");
Check(sent.All(k => k.GetCard<Card>() != null), "every pushed inventory item exists in the catalogue");

Check(players[Online].HeroLoadouts[cogwheel].Perks!.SequenceEqual([pusher]), "the cached loadout loses only the removed perk");
PlayerData Saved(uint id)
{
    using var raw = new SQLiteConnection(Databases.PlayerDatabaseFile);
    return PlayerData.FromPlayerRecord(raw.Table<PlayerRecord>().Single(r => r.PlayerId == id)); // no sanitizing on this path
}
for (var i = 0; i < 100 && Saved(Online).HeroLoadouts[cogwheel].Perks!.Contains(removed); i++) await Task.Delay(50);
Check(Saved(Online).HeroLoadouts[cogwheel].Perks!.SequenceEqual([pusher]), "the online player's saved loadout is stripped too");
Check(players[Online].HeroLoadouts[cogwheel].Perks!.SequenceEqual([pusher]), "the reloaded cache stays stripped");
Check(Saved(Offline).HeroLoadouts[cogwheel].Perks!.Contains(removed), "an offline record is left for the login sanitizer");
Check((await db.GetPlayer(Offline))!.HeroLoadouts[cogwheel].Perks!.SequenceEqual([pusher]), "and that sanitizer strips it at login");

events.Clear(); players.Clear();
Check(CatalogueRemoval.Apply(catalogue, "effect_perk_heavier_metal_pos", id => events.Add("broadcast " + id)) &&
      events.SequenceEqual(["broadcast effect_perk_heavier_metal_pos"]), "with nobody online the removal is still broadcast");

Directory.SetCurrentDirectory(Path.GetTempPath());
SQLiteAsyncConnection.ResetPool();
try { Directory.Delete(root, true); } catch (IOException) { } // pooled handles may outlive the run on Windows
Console.WriteLine($"Catalogue removal fixture passed: {checks} checks.");

public class Recorder : DispatchProxy
{
    public Action<MethodInfo, object?[]?>? OnCall;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        OnCall?.Invoke(method!, args);
        var type = method!.ReturnType;
        return type == typeof(void) ? null : type == typeof(Task) ? Task.CompletedTask : type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
