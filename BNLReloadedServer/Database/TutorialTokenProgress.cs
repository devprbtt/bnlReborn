using System.Globalization;
using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

[Table("TutorialTokenCollections")]
public sealed class TutorialTokenCollection
{
    [PrimaryKey] public string Id { get; set; } = "";
    [Indexed] public uint PlayerId { get; set; }
    public string Token { get; set; } = "";
}

public static class TutorialTokenProgress
{
    // Map positions remain stable across sessions; runtime unit IDs do not.
    public static string TokenId(Key map, MapUnit unit) => string.Create(CultureInfo.InvariantCulture,
        $"{map.Hash}:{unit.UnitKey.Hash}:{unit.Position.X:R}:{unit.Position.Y:R}:{unit.Position.Z:R}");

    public static int Record(SQLiteConnection db,uint playerId,string token,int total)
    {
        var player=db.Find<PlayerRecord>(playerId);
        if(player==null||total<=0||string.IsNullOrEmpty(token))return 0;
        db.Insert(new TutorialTokenCollection{Id=playerId+":"+token,PlayerId=playerId,Token=token},"OR IGNORE");
        int collected=db.Table<TutorialTokenCollection>().Count(t=>t.PlayerId==playerId);
        int progress=Math.Min(total,Math.Max(player.TutorialTokens,collected));
        // Update just this column; never overwrite concurrent account changes.
        db.Execute("UPDATE Users SET tutorial_tokens = ? WHERE id = ?",progress,playerId);
        return progress;
    }
}
