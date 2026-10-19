using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.Clans;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using SQLite;

public static class TrophyTests
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    /// <summary>Signature, IHDR (width x height) and IEND: enough for the server's header check.</summary>
    static byte[] Png(uint width, uint height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        var size = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(size, width); BinaryPrimitives.WriteUInt32BigEndian(size.AsSpan(4), height);
        bytes.AddRange(size); bytes.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });
        bytes.AddRange(new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 });
        return bytes.ToArray();
    }

    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bnl-trophies-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "players.db");
        try
        {
            Rules(dbPath, folder);
            PublicGate();
            Service(dbPath).GetAwaiter().GetResult();
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    static void Rules(string dbPath, string folder)
    {
        Check(Trophies.ValidId("winter-cup_2026") && !Trophies.ValidId("Winter") && !Trophies.ValidId("../x") && !Trophies.ValidId("a") && !Trophies.ValidId(null),
            "trophy IDs are lowercase slugs that cannot escape the image folder");
        Check(Trophies.ValidPng(Png(256, 256)) && !Trophies.ValidPng(Png(2048, 256)) && !Trophies.ValidPng(Png(0, 10)) &&
              !Trophies.ValidPng([.. "GIF89a"u8.ToArray(), .. new byte[40]]), "only PNG art within 1024 px is accepted");
        Trophies.SaveImage(folder, "cup", Png(128, 128));
        Check(File.ReadAllBytes(Trophies.ImagePath(folder, "cup")).SequenceEqual(Png(128, 128)) && !File.Exists(Trophies.ImagePath(folder, "cup") + ".incoming"),
            "images are written atomically under their ID");
        bool rejected = false;
        try { Trophies.SaveImage(folder, "../escape", Png(8, 8)); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "an unsafe ID cannot write an image");

        using var db = new SQLiteConnection(dbPath);
        Trophies.CreateTables(db); db.CreateTable<PlayerRecord>();
        db.Insert(new PlayerRecord { Username = "Atlas" }); db.Insert(new PlayerRecord { Username = "Nova" });

        Check(Trophies.Save(db, "cup", "Winter Cup", "Won the cup.", false, 1) == TrophyResult.InvalidImage, "a new trophy needs art");
        Check(Trophies.Save(db, "cup", "", "x", true, 1) == TrophyResult.InvalidName && Trophies.Save(db, "cup", new string('n', 41), "", true, 1) == TrophyResult.InvalidName,
            "names are required and limited");
        Check(Trophies.Save(db, "cup", "Winter Cup", new string('d', 241), true, 1) == TrophyResult.InvalidDescription, "descriptions are limited");
        Check(Trophies.Save(db, "cup", "  Winter Cup ", "Won the cup.", true, 10) == TrophyResult.Ok, "a trophy is created with its art");
        Check(Trophies.Save(db, "cup", "Winter Cup Champion", "Won the 2026 cup.", false, 20) == TrophyResult.Ok && db.Find<TrophyRecord>("cup").ImageVersion == 1,
            "editing text keeps the image version");
        Trophies.Save(db, "cup", "Winter Cup Champion", "Won the 2026 cup.", true, 30);
        Trophies.Save(db, "medal", "Medal", "", true, 40);
        Trophies.Save(db, "old", "Old", "", true, 50);
        var catalogue = Trophies.Catalogue(db);
        Check(catalogue.Trophies[0].Image == "/api/trophies/cup.png?v=2" && catalogue.Trophies[0].Name == "Winter Cup Champion",
            "new art bumps the image URL so clients refetch it");
        Check(catalogue.Trophies.Select(t => t.Id).SequenceEqual(["cup", "medal", "old"]), "the catalogue lists trophies in creation order");

        Check(Trophies.Grant(db, 1, "nope", "admin", "", 60).Result == TrophyResult.NoSuchTrophy, "unknown trophies cannot be granted");
        Check(Trophies.Grant(db, 99, "cup", "admin", "", 60).Result == TrophyResult.NoSuchPlayer, "unknown players cannot be granted trophies");
        var first = Trophies.Grant(db, 1, "cup", "admin", "  Final 3-1 " + new string('x', 300), 100);
        Trophies.Grant(db, 1, "cup", "admin", "", 200);
        Trophies.Grant(db, 1, "medal", "mod", "", 150);
        Trophies.Grant(db, 1, "old", "mod", "", 120);
        Check(Trophies.SetRetired(db, "old", true) == TrophyResult.Ok && Trophies.Grant(db, 2, "old", "admin", "", 130).Result == TrophyResult.Retired,
            "retired trophies cannot be granted");
        Check(db.Find<PlayerTrophyRecord>(first.GrantId).Note.Length == Trophies.MaxNoteLength && db.Find<PlayerTrophyRecord>(first.GrantId).Note.StartsWith("Final"),
            "notes are trimmed and limited");
        var mine = Trophies.Collection(db, 1);
        Check(mine.Trophies.Single(t => t.Id == "cup") is { Count: 2, FirstGrantedAt: 100, LastGrantedAt: 200 }, "repeat awards are counted with first and latest dates");
        Check(mine.Trophies.Select(t => t.Id).SequenceEqual(["cup", "medal", "old"]), "trophies list newest award first, retired ones kept");
        Check(Trophies.Collection(db, 2).Trophies.Count == 0, "collections are per player");

        Check(Trophies.SetShowcase(db, 1, ["cup", "medal", "old", "cup"]) == TrophyResult.Duplicate, "a trophy is shown once");
        Check(Trophies.SetShowcase(db, 1, ["cup", "medal", "a", "b", "c", "d"]) == TrophyResult.TooMany, "the showcase holds five");
        Check(Trophies.SetShowcase(db, 1, ["cup", "unowned"]) == TrophyResult.NotOwned, "only held trophies can be shown");
        Check(Trophies.SetShowcase(db, 1, ["medal", "cup"]) == TrophyResult.Ok && Trophies.Collection(db, 1).Showcase.SequenceEqual(["medal", "cup"]),
            "the showcase keeps the player's order");
        Check(Trophies.SetShowcase(db, 1, ["old"]) == TrophyResult.Ok && Trophies.Collection(db, 1).Showcase.SequenceEqual(["old"]), "a new showcase replaces the old");
        Trophies.SetShowcase(db, 1, ["cup", "medal"]);

        Check(Trophies.Revoke(db, first.GrantId) == TrophyResult.Ok && Trophies.Collection(db, 1).Showcase.Contains("cup"),
            "revoking one of two awards keeps the trophy on show");
        var medalGrant = db.Table<PlayerTrophyRecord>().First(g => g.TrophyId == "medal").Id;
        Check(Trophies.Revoke(db, medalGrant) == TrophyResult.Ok && Trophies.Collection(db, 1).Showcase.SequenceEqual(["cup"]),
            "revoking the last award removes it from the showcase");
        Check(Trophies.Revoke(db, medalGrant) == TrophyResult.NoSuchGrant, "an award can be revoked once");

        var recent = Trophies.RecentGrants(db, 10);
        Check(recent.First().Nickname == "Atlas" && recent.All(g => g.TrophyId != "medal"), "recent awards list holders by name, newest first");
        var json = JsonSerializer.Serialize(Trophies.Collection(db, 1), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Check(json.StartsWith("{\"version\":1,\"player_id\":1,") && json.Contains("\"first_granted_at\":") && json.Contains("\"showcase\":[\"cup\"]") &&
              !json.Contains("note") && !json.Contains("granted_by"), "a public collection carries no admin notes or names");
    }

    static void PublicGate()
    {
        var gate = typeof(BNLReloadedServer.ControlPanel.ControlPanelServer).GetMethod("IsPublicReadRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Public(string method, string path) => (bool)gate.Invoke(null, [method, path])!;
        Check(Public("GET", "/api/trophies") && Public("GET", "/api/trophies/cup.png") && Public("GET", "/api/trophies/players/3"),
            "the catalogue, art and player collections are public");
        Check(!Public("GET", "/api/trophies/grants"), "the award log with admin notes is not public");
        Check(!Public("POST", "/api/trophies") && !Public("POST", "/api/trophies/grant") && !Public("POST", "/api/trophies/revoke"),
            "creating, awarding and revoking need an admin session");
    }

    public class NullProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    sealed class AllowNames : IOffensiveText { public bool IsOffensive(string text) => false; }

    sealed class Capture : ISender
    {
        public uint? AssociatedPlayerId { get; set; }
        public int SenderCount => 1;
        public List<byte[]> Packets { get; } = [];
        public void Send(BinaryWriter writer) { lock (Packets) Packets.Add(((MemoryStream)writer.BaseStream).ToArray()); }
        public void Send(byte[] buffer) { lock (Packets) Packets.Add(buffer); }
        public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
        public void Subscribe(Guid id) { } public void Unsubscribe(Guid id) { } public void UnsubscribeAll() { }
    }

    static byte[] Frame(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(w);
        return stream.ToArray();
    }

    static async Task<byte?> Result(Capture sender, ushort requestId)
    {
        for (int i = 0; i < 100; i++)
        {
            lock (sender.Packets)
                foreach (var p in sender.Packets)
                    if (p.Length == 5 && p[1] == 1 && BitConverter.ToUInt16(p, 2) == requestId) return p[4];
            await Task.Delay(20);
        }
        return null;
    }

    static async Task Service(string dbPath)
    {
        var store = new ClanStore(new SQLiteAsyncConnection(dbPath), new AllowNames());
        await store.Load();
        typeof(ClanHub).GetField("_store", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, store);
        // No connected players: clan pushes after Hello find no service and do nothing.
        Databases.SetRegionDatabase(DispatchProxy.Create<IRegionServerDatabase, NullProxy>());
        ServiceClan.WriteShowcase = (id, ids) => Task.Run(() => { using var db = new SQLiteConnection(dbPath); return Trophies.SetShowcase(db, id, ids); });

        var legacy = new Capture { AssociatedPlayerId = 1 };
        var legacyService = new ServiceClan(legacy);
        legacyService.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)0); w.Write(3); }))));
        Check(legacy.Packets.All(p => p[1] != 9), "a capability-3 client is not told about trophies");

        var sender = new Capture { AssociatedPlayerId = 1 };
        var service = new ServiceClan(sender);
        service.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)0); w.Write(4); }))));
        Check(service.SupportsTrophies && sender.Packets.Any(p => p.Length == 3 && p[0] == 16 && p[1] == 9 && p[2] == Trophies.ShowcaseSlots),
            "a capability-4 Hello is answered with trophy support and the slot count");

        uint acknowledgedBy = 0; long[] acknowledged = [];
        ServiceClan.ReadTrophyNotices = id => Task.FromResult(new List<LeaderboardTrophies.Notice> { new(42, "leaderboard-kills", "Top 1 - Kills") });
        ServiceClan.AckTrophyNotices = (id, ids) => { acknowledgedBy = id; acknowledged = ids.ToArray(); return Task.FromResult(true); };
        var modern = new Capture { AssociatedPlayerId = 2 };
        var modernService = new ServiceClan(modern);
        modernService.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)0); w.Write(5); }))));
        for (int i = 0; i < 100; i++) { lock (modern.Packets) { if (modern.Packets.Any(p => p[1] == 10)) break; } await Task.Delay(10); }
        byte[] packet; lock (modern.Packets) packet = modern.Packets.Single(p => p[1] == 10);
        using (var notice = new BinaryReader(new MemoryStream(packet)))
        {
            Check(notice.ReadByte() == 16 && notice.ReadByte() == 10 && notice.ReadUInt32() == 2 && notice.ReadByte() == 1 &&
                notice.ReadInt64() == 42 && notice.ReadString() == "leaderboard-kills" && notice.ReadString() == "Top 1 - Kills", "capability 5 login sends exact trophy notice wire format");
        }
        Check(sender.Packets.All(p => p[1] != 10), "capability 4 receives no unknown notification message");
        modernService.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)23); w.Write((ushort)200); w.Write((byte)1); w.Write(42L); }))));
        Check((ClanResult?)await Result(modern, 200) == ClanResult.Ok && acknowledgedBy == 2 && acknowledged.SequenceEqual([42L]), "acknowledgement identity comes from authenticated connection");
        service.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)23); w.Write((ushort)201); w.Write((byte)1); w.Write(42L); }))));
        Check((ClanResult?)await Result(sender, 201) == ClanResult.NotAllowed, "older client cannot acknowledge notifications");
        modernService.Receive(new BinaryReader(new MemoryStream(Frame(w => { w.Write((byte)23); w.Write((ushort)202); w.Write((byte)18); }))));
        Check((ClanResult?)await Result(modern, 202) == ClanResult.NotAllowed, "oversized notification ack rejected before payload read");

        async Task<ClanResult?> Pin(ServiceClan target, Capture capture, ushort id, params string[] trophies)
        {
            target.Receive(new BinaryReader(new MemoryStream(Frame(w =>
            {
                w.Write((byte)22); w.Write(id); w.Write((byte)trophies.Length);
                foreach (var t in trophies) w.Write(t);
            }))));
            return (ClanResult?)await Result(capture, id);
        }
        Check(await Pin(service, sender, 100, "cup") == ClanResult.Ok, "a player pins a held trophy");
        using (var db = new SQLiteConnection(dbPath))
            Check(Trophies.Collection(db, 1).Showcase.SequenceEqual(["cup"]), "the pin is stored");
        Check(await Pin(service, sender, 101, "medal") == ClanResult.TrophyNotOwned, "pinning a trophy the player does not hold is refused");
        Check(await Pin(service, sender, 102, "a", "b", "c", "d", "e", "f") == ClanResult.TooManyTrophies, "more than five pins are refused before reading them");
        Check(await Pin(legacyService, legacy, 103, "cup") == ClanResult.NotAllowed, "a client that did not opt in cannot pin");
        using (var db = new SQLiteConnection(dbPath))
            Check(Trophies.Collection(db, 1).Showcase.SequenceEqual(["cup"]), "refused pins leave the showcase unchanged");
    }
}
