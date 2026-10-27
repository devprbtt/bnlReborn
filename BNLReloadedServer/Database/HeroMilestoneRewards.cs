using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

[Table("HeroMilestoneClaims")]
public sealed class HeroMilestoneClaim
{
    [PrimaryKey] public string Id { get; set; } = "";
    [Indexed] public uint PlayerId { get; set; }
    public uint HeroHash { get; set; }
    public int Level { get; set; }
    public long Gold { get; set; }
    public long Platinum { get; set; }
    public long GrantedAt { get; set; }
}

[Table("HeroProgressSettlements")]
public sealed class HeroProgressSettlement
{
    [PrimaryKey] public string Id { get; set; } = "";
}

public static class HeroMilestoneRewards
{
    public sealed record Reward(int Level, long Gold, long Platinum = 0);
    public static readonly IReadOnlyList<Reward> Schedule = Array.AsReadOnly(new Reward[] {
        new(10,250), new(20,500), new(30,750), new(50,1250,100), new(60,1500),
        new(70,1750), new(80,2000), new(90,2250), new(100,3000,500),
        new(150,5000), new(200,10000,1000)
    });

    public static string? SettlementId(EndMatchResults result) => string.IsNullOrEmpty(result.GameInstanceId)
        ? null : $"{result.GameInstanceId}:{result.PlayerId}:{result.MatchEndTime}";

    // Called within the same transaction that stores progression and the wallet.
    public static (long Gold, long Platinum) GrantEarned(SQLiteConnection db, PlayerRecord player, PlayerProgression progression)
    {
        long gold=0, platinum=0;
        foreach (var (hero,xp) in progression.HeroesProgress ?? [])
        {
            if (hero == Key.None || xp is null) continue;
            foreach(var reward in Schedule)
            {
                if (xp.Level < reward.Level) continue;
                var claim = new HeroMilestoneClaim {
                    Id=$"{player.PlayerId}:{hero.Hash}:{reward.Level}", PlayerId=player.PlayerId,
                    HeroHash=hero.Hash, Level=reward.Level, Gold=reward.Gold, Platinum=reward.Platinum,
                    GrantedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                if(db.Insert(claim,"OR IGNORE") == 0) continue;
                gold=checked(gold+reward.Gold); platinum=checked(platinum+reward.Platinum);
            }
        }
        player.Gold=checked(player.Gold+gold);player.Platinum=checked(player.Platinum+platinum);
        return (gold,platinum);
    }

    public static void InstallAndBackfill(SQLiteConnection db)
    {
        db.CreateTable<HeroMilestoneClaim>();
        db.CreateTable<HeroProgressSettlement>();
        foreach(var player in db.Table<PlayerRecord>().ToList())
        {
            if(player.Progression.Length == 0) continue;
            var reward=GrantEarned(db,player,PlayerProgression.ReadByteRecord(player.Progression));
            if(reward != (0L,0L)) db.Update(player);
        }
    }
}
