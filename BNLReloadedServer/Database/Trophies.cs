using System.Buffers.Binary;
using System.Text.RegularExpressions;
using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>A trophy players can be awarded. Retired trophies stay visible to their holders but cannot be granted.</summary>
[Table("Trophies")]
public sealed class TrophyRecord
{
    [PrimaryKey, Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
    // Bumped on every image upload; clients use it to refresh cached art.
    [Column("image_version")] public int ImageVersion { get; set; }
    [Column("created_at_ms")] public long CreatedAt { get; set; }
    [Column("retired")] public bool Retired { get; set; }
}

/// <summary>One award. A trophy can be awarded to the same player more than once (shown as a count).</summary>
[Table("PlayerTrophies")]
public sealed class PlayerTrophyRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed, Column("player_id")] public long PlayerId { get; set; }
    [Indexed, Column("trophy_id")] public string TrophyId { get; set; } = string.Empty;
    [Column("granted_at_ms")] public long GrantedAt { get; set; }
    [Column("granted_by")] public string GrantedBy { get; set; } = string.Empty;
    [Column("notified")] public bool Notified { get; set; }
    [Column("note")] public string Note { get; set; } = string.Empty;
}

/// <summary>A player's pinned trophies, shown on their profile summary to everyone.</summary>
[Table("TrophyShowcase")]
public sealed class TrophyShowcaseRecord
{
    [PrimaryKey, AutoIncrement, Column("row_id")] public long RowId { get; set; }
    [Indexed(Name = "IX_TrophyShowcase_Slot", Order = 1, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Indexed(Name = "IX_TrophyShowcase_Slot", Order = 2, Unique = true), Column("slot")] public int Slot { get; set; }
    [Column("trophy_id")] public string TrophyId { get; set; } = string.Empty;
}

public sealed record TrophyDefinition(string Id, string Name, string Description, string Image, bool Retired);
public sealed record TrophyCatalogue(int Version, List<TrophyDefinition> Trophies);
public sealed record OwnedTrophy(string Id, int Count, long FirstGrantedAt, long LastGrantedAt);
public sealed record TrophyCollection(int Version, uint PlayerId, List<OwnedTrophy> Trophies, List<string> Showcase);
public sealed record TrophyGrantView(long Id, uint PlayerId, string Nickname, string TrophyId, long GrantedAt, string GrantedBy, string Note);

public enum TrophyResult { Ok, InvalidId, InvalidName, InvalidDescription, InvalidImage, NoSuchTrophy, NoSuchPlayer, Retired, NoSuchGrant, NotOwned, TooMany, Duplicate }

/// <summary>
/// Admin-awarded trophies (control panel) with a player-chosen showcase (clan service request). Pure functions over a
/// SQLite connection, so the rules are tested without a server.
/// </summary>
public static partial class Trophies
{
    public const int ShowcaseSlots = 5, MaxNameLength = 40, MaxDescriptionLength = 240, MaxNoteLength = 200;
    public const int MaxImageBytes = 1024 * 1024, MaxImageSide = 1024;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,47}$")]
    private static partial Regex IdPattern();

    public static bool ValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static void CreateTables(SQLiteConnection db)
    {
        db.CreateTable<TrophyRecord>(); db.CreateTable<PlayerTrophyRecord>(); db.CreateTable<TrophyShowcaseRecord>();
    }

    public static string ImageUrl(TrophyRecord t) => "/api/trophies/" + t.Id + ".png?v=" + t.ImageVersion;

    public static TrophyCatalogue Catalogue(SQLiteConnection db) =>
        new(1, db.Table<TrophyRecord>().ToList().OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new TrophyDefinition(t.Id, t.Name, t.Description, ImageUrl(t), t.Retired)).ToList());

    /// <summary>Create or update a definition. <paramref name="imageUploaded"/> bumps the image version.</summary>
    public static TrophyResult Save(SQLiteConnection db, string id, string name, string description, bool imageUploaded, long nowMs)
    {
        if (!ValidId(id)) return TrophyResult.InvalidId;
        name = name.Trim(); description = description.Trim();
        if (name.Length is 0 or > MaxNameLength) return TrophyResult.InvalidName;
        if (description.Length > MaxDescriptionLength) return TrophyResult.InvalidDescription;
        var existing = db.Find<TrophyRecord>(id);
        if (existing is null)
        {
            if (!imageUploaded) return TrophyResult.InvalidImage; // a new trophy needs its art
            db.Insert(new TrophyRecord { Id = id, Name = name, Description = description, ImageVersion = 1, CreatedAt = nowMs });
            return TrophyResult.Ok;
        }
        existing.Name = name; existing.Description = description;
        if (imageUploaded) existing.ImageVersion++;
        db.Update(existing);
        return TrophyResult.Ok;
    }

    public static TrophyResult SetRetired(SQLiteConnection db, string id, bool retired)
    {
        var trophy = db.Find<TrophyRecord>(id);
        if (trophy is null) return TrophyResult.NoSuchTrophy;
        trophy.Retired = retired; db.Update(trophy);
        return TrophyResult.Ok;
    }

    public static (TrophyResult Result, long GrantId) Grant(SQLiteConnection db, uint playerId, string trophyId, string grantedBy, string note, long nowMs)
    {
        var trophy = db.Find<TrophyRecord>(trophyId);
        if (trophy is null) return (TrophyResult.NoSuchTrophy, 0);
        if (trophy.Retired) return (TrophyResult.Retired, 0);
        if (db.Find<PlayerRecord>(playerId) is null) return (TrophyResult.NoSuchPlayer, 0);
        note = note.Trim();
        if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
        var grant = new PlayerTrophyRecord { PlayerId = playerId, TrophyId = trophyId, GrantedAt = nowMs, GrantedBy = grantedBy, Note = note };
        db.Insert(grant);
        return (TrophyResult.Ok, grant.Id);
    }

    /// <summary>Removes one award; once a player holds none of that trophy it also leaves their showcase.</summary>
    public static TrophyResult Revoke(SQLiteConnection db, long grantId)
    {
        var grant = db.Find<PlayerTrophyRecord>(grantId);
        if (grant is null) return TrophyResult.NoSuchGrant;
        db.Delete(grant);
        long holder = grant.PlayerId; string trophy = grant.TrophyId;
        if (!db.Table<PlayerTrophyRecord>().Any(g => g.PlayerId == holder && g.TrophyId == trophy))
            db.Execute("DELETE FROM TrophyShowcase WHERE player_id = ? AND trophy_id = ?", grant.PlayerId, grant.TrophyId);
        return TrophyResult.Ok;
    }

    public static TrophyCollection Collection(SQLiteConnection db, uint playerId)
    {
        long id = playerId;
        var owned = db.Table<PlayerTrophyRecord>().Where(g => g.PlayerId == id).ToList()
            .GroupBy(g => g.TrophyId)
            .Select(g => new OwnedTrophy(g.Key, g.Count(), g.Min(x => x.GrantedAt), g.Max(x => x.GrantedAt)))
            .OrderByDescending(t => t.LastGrantedAt).ThenBy(t => t.Id).ToList();
        var showcase = db.Table<TrophyShowcaseRecord>().Where(s => s.PlayerId == id).ToList()
            .OrderBy(s => s.Slot).Select(s => s.TrophyId).ToList();
        return new TrophyCollection(1, playerId, owned, showcase);
    }

    /// <summary>Replaces the player's showcase. Every trophy must be held, at most once, up to five.</summary>
    public static TrophyResult SetShowcase(SQLiteConnection db, uint playerId, IReadOnlyList<string> trophyIds)
    {
        if (trophyIds.Count > ShowcaseSlots) return TrophyResult.TooMany;
        if (trophyIds.Distinct().Count() != trophyIds.Count) return TrophyResult.Duplicate;
        long id = playerId;
        var held = db.Table<PlayerTrophyRecord>().Where(g => g.PlayerId == id).ToList().Select(g => g.TrophyId).ToHashSet();
        if (trophyIds.Any(id => !held.Contains(id))) return TrophyResult.NotOwned;
        db.Execute("DELETE FROM TrophyShowcase WHERE player_id = ?", (long)playerId);
        for (int i = 0; i < trophyIds.Count; i++)
            db.Insert(new TrophyShowcaseRecord { PlayerId = playerId, Slot = i, TrophyId = trophyIds[i] });
        return TrophyResult.Ok;
    }

    public static List<TrophyGrantView> RecentGrants(SQLiteConnection db, int limit)
    {
        var names = db.Table<PlayerRecord>().ToList().ToDictionary(p => p.PlayerId, p => p.Username);
        return db.Table<PlayerTrophyRecord>().OrderByDescending(g => g.Id).Take(limit).ToList()
            .Select(g => new TrophyGrantView(g.Id, (uint)g.PlayerId, names.GetValueOrDefault((uint)g.PlayerId) ?? "", g.TrophyId, g.GrantedAt, g.GrantedBy, g.Note))
            .ToList();
    }

    /// <summary>True for a real PNG within the size limits (checks the signature and IHDR dimensions).</summary>
    public static bool ValidPng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length is < 33 or > MaxImageBytes || !bytes.AsSpan(0, 8).SequenceEqual(signature)) return false;
        if (!bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= MaxImageSide && height is > 0 and <= MaxImageSide;
    }

    public static string ImagePath(string folder, string id) => Path.Combine(folder, id + ".png");

    /// <summary>Writes the image atomically. Call only with a <see cref="ValidId"/> id and a <see cref="ValidPng"/> image.</summary>
    public static void SaveImage(string folder, string id, byte[] png)
    {
        if (!ValidId(id) || !ValidPng(png)) throw new ArgumentException("Invalid trophy image");
        Directory.CreateDirectory(folder);
        var target = ImagePath(folder, id);
        var temp = target + ".incoming";
        File.WriteAllBytes(temp, png);
        File.Move(temp, target, overwrite: true);
    }
}
