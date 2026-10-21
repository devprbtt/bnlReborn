using System.Text.Json;

namespace BNLReloadedServer.ServerTypes;

// Server-owned balance knobs. BNL_ACTION_SCORE_RULES may point to a JSON override file.
public sealed class ActionScoreRules
{
    public int Kill { get; set; } = 50;
    public int FirstKill { get; set; } = 60;
    public int DoubleKill { get; set; } = 75;
    public int MultiKill { get; set; } = 100;
    public int UltraKill { get; set; } = 125;
    public int Fantastic { get; set; } = 150;
    public int Unbelievable { get; set; } = 175;
    public int Assist { get; set; } = 25;
    public int Headshot { get; set; } = 10;
    public int Supply { get; set; } = 25;
    public int Blockbuster { get; set; } = 100;
    public int Cube { get; set; } = 200;
    public int CubeAssist { get; set; } = 75;
    public int Build { get; set; } = 10;
    public int Radar { get; set; } = 5;
    public int AbilitySpot { get; set; } = 10;
    public float HealPerPoint { get; set; } = 10;
    public float DeviceDamagePerPoint { get; set; } = 20;
    public int ContinuousChunk { get; set; } = 5;
    public float DeviceCostPerPoint { get; set; } = 20;
    public int DeviceMin { get; set; } = 5;
    public int DeviceMax { get; set; } = 50;
    public float ObjectiveRadius { get; set; } = 12;
    public float ObjectiveBonus { get; set; } = .25f;
    public float SpotCooldown { get; set; } = 15;
    public float BuildCooldown { get; set; } = 30;
    public static ActionScoreRules Load()
    {
        var path = Environment.GetEnvironmentVariable("BNL_ACTION_SCORE_RULES");
        var rules = string.IsNullOrWhiteSpace(path) ? new() :
            JsonSerializer.Deserialize<ActionScoreRules>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Empty action score configuration");
        rules.Validate(); return rules;
    }
    public void Validate()
    {
        foreach (var p in GetType().GetProperties())
        {
            var value = Convert.ToDouble(p.GetValue(this));
            if (!double.IsFinite(value) || value < 0 || value > 100000) throw new InvalidDataException("Invalid action score setting: " + p.Name);
        }
        if (FirstKill <= Kill || DoubleKill <= FirstKill || MultiKill <= DoubleKill || UltraKill <= MultiKill || Fantastic <= UltraKill || Unbelievable <= Fantastic)
            throw new InvalidDataException("Kill accolade scores must increase above the regular kill value");
        if (HealPerPoint == 0 || DeviceDamagePerPoint == 0 || DeviceCostPerPoint == 0 || ContinuousChunk == 0 || DeviceMax < DeviceMin)
            throw new InvalidDataException("Invalid action score denominator or device range");
    }
}

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
