// The catalogue says which items exist; the server's InventoryGrants table says who owns the private ones.
// Runs the real MasterServerDatabase against a throwaway SQLite file: the one-time import of the retired
// grant file, grant/revoke, restarts, the live inventory push, inventory building, loadout rules, and the
// startup check that fails a deploy made before the private cards were published to CouchDB.
using System.Collections.Concurrent;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using SQLite;

var root = Path.Combine(Path.GetTempPath(), "bnl-inventory-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "PlayerData"));
Directory.CreateDirectory(Path.Combine(root, "Configs"));
Directory.SetCurrentDirectory(root); // Databases resolves every path from the working directory on first use

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
Check(Databases.PlayerDatabaseFile.StartsWith(root), "fixture database is isolated in a temp folder");

const ulong RecoveryOwner = 76561197990315750, Granted = 76561198366278223, Other = 76561197990315751, NoAccount = 76561190000000001;
using (var seed = new SQLiteConnection(Databases.PlayerDatabaseFile))
{
    seed.CreateTable<PlayerRecord>();
    foreach (var (steam, name) in new[] { (RecoveryOwner, "prbtt"), (Granted, "wolfirin"), (Other, "other") })
        seed.Insert(new PlayerRecord { SteamId = steam, Username = name });
}
const uint A = 1, B = 2, C = 3; // player ids in insert order
File.WriteAllText(Path.Combine(root, "Configs", "private_skin_grants.json"),
    $$"""{"skin_hunter_arctic_wolf_private":["{{Granted}}","{{NoAccount}}","not-a-number"],"skin_boxer_demon_private":[],"skin_unlisted":["{{Other}}"]}""");

var hunter = new Key("unit_hero_hunter"); var boxer = new Key("unit_hero_boxer");
var normal = new Key("skin_hunter_s1"); var boxerNormal = new Key("skin_boxer_s1");
var wolf = new Key("skin_hunter_arctic_wolf_private"); var demon = new Key("skin_boxer_demon_private");
var publicBadge = new Key("badge_fixture_public"); var privateBadge = new Key("badge_fixture_private");
List<Card> Catalogue(bool withPrivate)
{
    var cards = new List<Card>
    {
        new CardUnit { Id = "unit_hero_hunter", Data = new UnitDataPlayer { Skins = withPrivate ? [normal, wolf] : [normal] } },
        new CardUnit { Id = "unit_hero_boxer", Data = new UnitDataPlayer { Skins = withPrivate ? [boxerNormal, demon] : [boxerNormal] } },
        new CardSkin { Id = "skin_hunter_s1", HeroKey = hunter },
        new CardSkin { Id = "skin_boxer_s1", HeroKey = boxer },
        new CardBadge { Id = "badge_fixture_public" },
        new CardGlobalLogic { Id = "global_logic", AvailableHeroes = [hunter, boxer], AvailableBadges = [publicBadge, privateBadge] },
    };
    if (withPrivate)
        cards.AddRange([
            new CardSkin { Id = "skin_hunter_arctic_wolf_private", HeroKey = hunter, Scope = ScopeType.Private },
            new CardSkin { Id = "skin_boxer_demon_private", HeroKey = boxer, Scope = ScopeType.Private },
            new CardBadge { Id = "badge_fixture_private", Scope = ScopeType.Private }]);
    return cards;
}
var catalogue = (ServerCatalogue)Databases.Catalogue;

MasterServerDatabase Start(out string stderr)
{
    var capture = new StringWriter(); var previous = Console.Error;
    Console.SetError(capture);
    try { return new MasterServerDatabase(); }
    finally { Console.SetError(previous); stderr = capture.ToString(); }
}
List<InventoryGrantRecord> Rows() { using var db = new SQLiteConnection(Databases.PlayerDatabaseFile); return db.Table<InventoryGrantRecord>().ToList(); }

// 1. Deployed before the cards reached CouchDB: the import still runs, and the start is flagged at error priority.
catalogue.Replicate(Catalogue(withPrivate: false));
Start(out var stderr);
Check(stderr.Contains("<3>") && stderr.Contains("skin_hunter_arctic_wolf_private") && stderr.Contains("skin_boxer_demon_private"),
    "server started before the cards were published reports it at journald error priority");
var rows = Rows();
Check(rows.Count == 3, $"legacy file and recovery owner imported as three grants (found {rows.Count})");
Check(rows.Where(r => r.PlayerId == A).Select(r => r.Item).Order().SequenceEqual(["skin_boxer_demon_private", "skin_hunter_arctic_wolf_private"]),
    "recovery owner holds both skins");
Check(rows.Count(r => r.PlayerId == B) == 1 && rows.Single(r => r.PlayerId == B).Item == "skin_hunter_arctic_wolf_private", "file grant imported for its account only");
Check(rows.All(r => r.PlayerId != C), "unlisted skin ids in the old file are ignored");
Check(rows.All(r => r.GrantedBy == "migration" && (DateTimeOffset.UtcNow - r.GrantedAt).TotalMinutes < 5), "import is attributed and timestamped");

// 2. Cards published: a clean start, and the import does not run twice.
catalogue.Replicate(Catalogue(withPrivate: true));
var db = Start(out stderr);
Check(!stderr.Contains("<3>"), "no error once every granted item is a private card");
Check(Rows().Count == 3, "import runs once");

Check(PlayerInventory.Owns(A, wolf) && PlayerInventory.Owns(A, demon), "owner owns both private skins");
Check(PlayerInventory.Owns(B, wolf) && !PlayerInventory.Owns(B, demon), "granted player owns only the granted skin");
Check(!PlayerInventory.Owns(C, wolf) && !PlayerInventory.Owns(C, demon), "other players own no private skin");
Check(PlayerInventory.Owns(C, normal) && PlayerInventory.Owns(C, publicBadge), "public items are owned by everyone");
Check(!PlayerInventory.Owns(C, new Key("skin_does_not_exist")), "unknown items are owned by no one");

// 3. Grant and revoke, with the live push to an online player.
var pushed = new List<(uint player, List<InventoryItem> inventory)>();
var region = DispatchProxy.Create<IRegionServerDatabase, Recorder>();
((Recorder)(object)region).OnCall = (m, a) => { if (m.Name == "NotifyInventory") pushed.Add(((uint)a![0]!, (List<InventoryItem>)a[1]!)); };
Databases.SetRegionDatabase(region);
var players = (ConcurrentDictionary<uint, PlayerData>)typeof(PlayerDatabase).GetField("_players", Any)!.GetValue(Databases.PlayerDatabase)!;
foreach (var (id, steam) in new[] { (A, RecoveryOwner), (B, Granted), (C, Other) })
    players[id] = new PlayerData { PlayerId = id, SteamId = steam };

Check(await db.GrantItem(C, "skin_boxer_demon_private", "fixture", "event prize") == InventoryChange.Granted, "grant succeeds");
Check(PlayerInventory.Owns(C, demon), "grant takes effect without a restart");
Check(pushed.Count == 1 && pushed[0].player == C && pushed[0].inventory.Any(i => i.Item == demon), "online player is sent the new inventory");
Check(await db.GrantItem(C, "skin_boxer_demon_private", "fixture", null) == InventoryChange.Unchanged, "granting twice changes nothing");
Check(await db.GrantItem(C, "skin_hunter_s1", "fixture", null) == InventoryChange.PublicItem, "public items cannot be granted");
Check(await db.GrantItem(C, "skin_nope", "fixture", null) == InventoryChange.UnknownItem, "unknown items cannot be granted");
Check(await db.GrantItem(999, "skin_boxer_demon_private", "fixture", null) == InventoryChange.UnknownPlayer, "unknown players cannot be granted");
var grant = (await db.GetInventoryGrants(C)).Single();
Check(grant.GrantedBy == "fixture" && grant.Note == "event prize", "grant records who and why");

Check(await db.RevokeItem(B, "skin_hunter_arctic_wolf_private") == InventoryChange.Revoked, "revoke succeeds");
Check(!PlayerInventory.Owns(B, wolf), "revoke takes effect without a restart");
Check(pushed.Last().player == B && pushed.Last().inventory.All(i => i.Item != wolf), "revoked player is sent the reduced inventory");
Check(await db.RevokeItem(B, "skin_hunter_arctic_wolf_private") == InventoryChange.Unchanged, "revoking twice changes nothing");

// 4. Restart: the database, not the old file or a stale cache, decides.
PlayerInventory.Set(B, ["skin_hunter_arctic_wolf_private"]); // a stale cache must not survive a start
Start(out _);
Check(!PlayerInventory.Owns(B, wolf), "a revoke survives restart (the old file is not re-imported)");
Check(PlayerInventory.Owns(C, demon), "a grant survives restart");

// 5. Real inventory, every category.
Check(await db.GrantItem(A, "badge_fixture_private", "fixture", null) == InventoryChange.Granted, "non-skin items can be private too");
var getInventory = typeof(PlayerDatabase).GetMethod("GetInventory", Any)!;
foreach (var id in new[] { A, B, C })
{
    var items = ((List<InventoryItem>)getInventory.Invoke(Databases.PlayerDatabase, [id])!).Select(i => i.Item).ToHashSet();
    Check(items.Contains(wolf) == PlayerInventory.Owns(id, wolf) && items.Contains(demon) == PlayerInventory.Owns(id, demon) &&
          items.Contains(privateBadge) == (id == A) && items.Contains(normal) && items.Contains(publicBadge),
        $"player {id} inventory holds public items and exactly their private grants");
}

// 6. Loadout rules.
Check(PlayerInventory.CanEquipSkin(A, hunter, wolf) && !PlayerInventory.CanEquipSkin(A, boxer, wolf), "owner can wear a skin only on its hero");
Check(!PlayerInventory.CanEquipSkin(B, hunter, wolf), "a revoked skin cannot be worn");
Check(!PlayerInventory.CanEquipSkin(C, boxer, normal), "public skins are also held to their hero");
Check(PlayerInventory.CanEquipSkin(C, boxer, Key.None), "no skin is always allowed");
Check(PlayerInventory.SafeSkin(B, hunter, wolf) == normal && PlayerInventory.SafeSkin(A, hunter, wolf) == wolf, "unowned loadout skin falls back, owned one is kept");
foreach (var (id, keep) in new[] { (A, true), (B, false) })
{
    var data = new PlayerData { PlayerId = id, HeroLoadouts = new() { [hunter] = new LobbyLoadout { HeroKey = hunter, SkinKey = wolf } } };
    data.SanitizeAgainstCatalogue();
    Check(data.HeroLoadouts.ContainsKey(hunter) == keep, $"saved loadout with a private skin {(keep ? "kept for owner" : "stripped for non-owner")}");
}

Directory.SetCurrentDirectory(Path.GetTempPath());
SQLiteAsyncConnection.ResetPool();
try { Directory.Delete(root, true); } catch (IOException) { } // pooled handles may outlive the run on Windows
Console.WriteLine($"Player inventory fixture passed: {checks} checks.");

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
