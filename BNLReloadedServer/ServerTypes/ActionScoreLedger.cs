using System.Text.Json;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public sealed class ActionScoreLedger(ActionScoreRules rules)
{
    public sealed record Award(long seq, uint player, string reason, int points);
    private readonly Dictionary<uint, int> totals = [];
    // Points by the hero the player was on when each was granted: hero XP follows the hero that earned it.
    private readonly Dictionary<uint, Dictionary<Key, int>> heroTotals = [];
    /// <summary>The player's hero at award time; null when unknown.</summary>
    public Func<uint, Key?>? HeroOf { get; init; }
    private readonly Dictionary<(uint, string, bool), double> fractions = [];
    private readonly Dictionary<(uint, string), double> cooldowns = [];
    private readonly Queue<Award> events = new();
    private long sequence;
    public string MatchId { get; } = Guid.NewGuid().ToString("N");
    public bool Dirty { get; private set; }
    public int Total(uint player) => totals.GetValueOrDefault(player);
    public IReadOnlyDictionary<Key, int> HeroTotals(uint player) =>
        heroTotals.TryGetValue(player, out var byHero) ? byHero : new Dictionary<Key, int>();
    public void Ensure(uint player) { if (totals.TryAdd(player, 0)) Dirty = true; }
    public bool Ready(uint player, string key, double now, double interval)
    {
        var id = (player, key);
        if (cooldowns.TryGetValue(id, out var previous) && now - previous < interval) return false;
        cooldowns[id] = now; return true;
    }
    public int BuildValue(float cost) => !float.IsFinite(cost) || cost <= 0 ? 0 :
        (int)Math.Clamp(Math.Ceiling((double)cost / rules.BuildCostPerPoint), rules.BuildMin, rules.BuildMax);
    public int DeviceValue(float cost) => !float.IsFinite(cost) ? 0 :
        Math.Clamp((int)Math.Ceiling(Math.Max(0, cost) / rules.DeviceCostPerPoint), rules.DeviceMin, rules.DeviceMax);
    public void Accumulate(uint player, string reason, float amount, float perPoint, bool nearObjective)
    {
        if (!float.IsFinite(amount) || amount <= 0) return;
        var key = (player, reason, nearObjective);
        double sum = fractions.GetValueOrDefault(key) + amount / perPoint;
        int points = (int)Math.Min(100000, Math.Floor(sum / rules.ContinuousChunk) * rules.ContinuousChunk);
        fractions[key] = sum - points;
        if (points > 0) Grant(player, reason, points, nearObjective);
    }
    public void Grant(uint player, string reason, int points, bool nearObjective)
    {
        if (points <= 0) return;
        Add(player, reason, points);
        if (!nearObjective) return;
        int bonus = rules.ObjectiveBonus > 0 ? Math.Max(1, (int)Math.Floor(points * rules.ObjectiveBonus)) : 0;
        if (IsKill(reason)) bonus = Math.Max(bonus, rules.KillObjectiveBonus);
        if (bonus > 0) Add(player, "Objective bonus", bonus);
    }
    public static bool IsKill(string reason) => reason is "Kill" or "First kill" or "Double kill" or "Multi kill" or
        "Ultra kill" or "Fantastic" or "Unbelievable";
    private void Add(uint player, string reason, int points)
    {
        int granted = Math.Min(points, int.MaxValue - Total(player));
        if (granted <= 0) return;
        totals[player] = Total(player) + granted;
        if (HeroOf?.Invoke(player) is { } hero && hero != Key.None)
        {
            if (!heroTotals.TryGetValue(player, out var byHero)) heroTotals[player] = byHero = [];
            byHero[hero] = byHero.GetValueOrDefault(hero) + granted;
        }
        events.Enqueue(new Award(++sequence, player, reason, granted));
        while (events.Count > 256) events.Dequeue();
        Dirty = true;
    }
    public string Snapshot(bool baseline) => JsonSerializer.Serialize(new {
        match = MatchId, baseline, sequence,
        players = totals.Select(p => new { player = p.Key, total = p.Value }).ToArray(),
        awards = baseline ? Array.Empty<Award>() : events.ToArray()
    });
    // Zone updates use the reliable ordered service; retain only unpublished awards.
    public void Published() { Dirty = false; events.Clear(); }
}
