namespace BNLReloadedServer.Clans;

// Mutated only under ClanCompetition's gate. Clock supplied by caller for reproducible timeout tests.
public sealed class ClanFightRoom
{
    public int Id;
    public int HomeClan, AwayClan;
    public uint Leader, OpponentCaptain;
    public bool Swapped;
    public string Phase = "room";
    public long Deadline;
    public bool AwayTurn;
    public readonly List<FightMember> Players = [];
    public readonly List<uint> Maps = [], Heroes = [], BannedMaps = [], BannedHeroes = [];
    public uint Map => Maps.Single(m => !BannedMaps.Contains(m));
    public uint Captain => AwayTurn ? OpponentCaptain : Leader;
    public bool CanStart => Phase == "room" && AwayClan != 0 && Players.All(p => p.Ready) &&
        Players.Count(p => p.Clan == HomeClan) is 4 or 5 &&
        Players.Count(p => p.Clan == HomeClan) == Players.Count(p => p.Clan == AwayClan);

    public bool Join(uint player, int clan)
    {
        if (Phase != "room" || clan == 0 || Players.Any(p => p.Id == player)) return false;
        if (clan != HomeClan && AwayClan != 0 && clan != AwayClan) return false;
        if (Players.Count(p => p.Clan == clan) >= 5) return false;
        if (clan != HomeClan && AwayClan == 0) { AwayClan = clan; OpponentCaptain = player; }
        Players.Add(new FightMember { Id = player, Clan = clan });
        ResetReady();
        return true;
    }

    public void Leave(uint player)
    {
        Players.RemoveAll(p => p.Id == player);
        if (Phase != "room") { Phase = "room"; BannedMaps.Clear(); BannedHeroes.Clear(); Deadline = 0; }
        if (Leader == player) Leader = Players.FirstOrDefault(p => p.Clan == HomeClan)?.Id ?? 0;
        if (OpponentCaptain == player) OpponentCaptain = Players.FirstOrDefault(p => p.Clan == AwayClan)?.Id ?? 0;
        if (OpponentCaptain == 0) AwayClan = 0;
        ResetReady();
    }

    public void ResetReady() { foreach (var p in Players) p.Ready = false; }
    public bool Start(uint player, long now)
    {
        if (player != Leader || !CanStart || Maps.Count == 0 || Heroes.Count < 2) return false;
        BannedMaps.Clear(); BannedHeroes.Clear();
        Phase = Maps.Count > 1 ? "maps" : "heroes";
        AwayTurn = Phase == "maps";
        Deadline = now + 10000;
        return true;
    }
    public bool Ban(uint player, uint key, long now)
    {
        if (player != Captain || now >= Deadline || Phase is not ("maps" or "heroes")) return false;
        return ApplyBan(key, now);
    }
    public bool Tick(long now)
    {
        if (Phase is not ("maps" or "heroes") || now < Deadline) return false;
        var pool = Phase == "maps" ? Maps.Except(BannedMaps) : Heroes.Except(BannedHeroes);
        return ApplyBan(pool.First(), now);
    }
    private bool ApplyBan(uint key, long now)
    {
        var pool = Phase == "maps" ? Maps : Heroes;
        var banned = Phase == "maps" ? BannedMaps : BannedHeroes;
        if (!pool.Contains(key) || banned.Contains(key)) return false;
        banned.Add(key); AwayTurn = !AwayTurn; Deadline = now + 10000;
        if (Phase == "maps" && Maps.Count - BannedMaps.Count == 1) { Phase = "heroes"; AwayTurn = false; }
        else if (Phase == "heroes" && BannedHeroes.Count == 2) { Phase = "draft"; Deadline = 0; }
        return true;
    }
}

public sealed class FightMember { public uint Id; public int Clan; public bool Ready; }
