using System.Text.Json;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public sealed class ActionScoreLedger(ActionScoreRules rules)
{
    public sealed record Award(long seq, uint player, string reason, int points);
    private readonly Dictionary<uint, int> totals = [];
    private readonly Dictionary<(uint, string, bool), double> fractions = [];
    private readonly Dictionary<(uint, string), double> cooldowns = [];
    private readonly Queue<Award> events = new();
    private long sequence;
    public string MatchId { get; } = Guid.NewGuid().ToString("N");
    public bool Dirty { get; private set; }
    public int Total(uint player) => totals.GetValueOrDefault(player);
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
        if (nearObjective && rules.ObjectiveBonus > 0)
            Add(player, "Objective bonus", Math.Max(1, (int)Math.Floor(points * rules.ObjectiveBonus)));
    }
    private void Add(uint player, string reason, int points)
    {
        int granted = Math.Min(points, int.MaxValue - Total(player));
        if (granted <= 0) return;
        totals[player] = Total(player) + granted;
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
