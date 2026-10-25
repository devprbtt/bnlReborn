using System.Collections.Concurrent;
using BNLReloadedServer.BaseTypes;
using Moserware.Skills;
using SQLite;
using System.Text.Json;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

namespace BNLReloadedServer.Database;

public sealed record MmrAward(uint PlayerId, double Base, double Performance, double Shortage,
    double Delta, double Deviation, bool Abandoned, string Reason);

// Frozen reference samples must precede the match. Unsupported components stay neutral;
// their weight is never transferred to combat. Values are per active participation minute.
public sealed record MmrReference(uint Hero, uint Mode, uint Map, long Before, string Version,
    int Players, double[] Kills, double[] Assists, double[] Damage,
    double[]? EffectiveAllyHealing = null, double[]? Objective = null, MatchType? GameType = null, string Loadout = "/");

public static class MmrReferenceFile
{
    public static IReadOnlyList<MmrReference> Load()
    {
        var path=Path.Combine(Databases.ConfigsFolderPath,"mmr-references-v2.json");
        if(!File.Exists(path))return [];
        // An invalid calibration file must not prevent outcome/abandonment settlement.
        try { return JsonSerializer.Deserialize<List<MmrReference>>(File.ReadAllText(path))??[]; }
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
}

public static class PersonalMmr
{
    public const int Version = 2;
    public const int ReturnSeconds = 45;
    public static bool Public(Key mode) => mode == new Key("game_mode_friendly") || mode == new Key("game_mode_ranked");
    public static bool Original(CompletedMatchPlayer p) => p.WasInitial || p.Presences.Any(s => s.JoinKind == MatchJoinKind.Initial);
    public static TeamType Team(CompletedMatchPlayer p) => p.Presences.First().Team;
    public static bool Abandoned(CompletedMatchRecord m, CompletedMatchPlayer p)
    {
        var segments = p.Presences.OrderBy(s => s.JoinedAt).ToArray();
        ulong disconnected = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.LeftAt is not { } left || left >= m.EndedAt) continue;
            if (s.LeaveKind is MatchLeaveKind.Quit or MatchLeaveKind.Inactivity) return true;
            if (s.LeaveKind == MatchLeaveKind.Disconnect)
            {
                if (i + 1 == segments.Length) return true;
                disconnected += segments[i + 1].JoinedAt > left ? segments[i + 1].JoinedAt-left : 0;
                if(disconnected > ReturnSeconds * 1000) return true;
            }
        }
        return false;
    }

    public static double Seconds(CompletedMatchRecord m, CompletedMatchPlayer p)
    {
        ulong end = m.ActiveStartedAt ?? m.StartedAt, total = 0;
        foreach (var s in p.Presences.OrderBy(s => s.JoinedAt))
        {
            var start = Math.Max(end, s.JoinedAt); var stop = Math.Min(m.EndedAt, s.LeftAt ?? m.EndedAt);
            if (stop > start) total += stop - start;
            end = Math.Max(end, stop);
        }
        return total / 1000d;
    }

    public static double Exposure(CompletedMatchRecord m)
    {
        var start = m.ActiveStartedAt ?? m.StartedAt;
        if (m.EndedAt <= start) return 0;
        var segments = m.Players.SelectMany(p => p.Presences).ToArray();
        if (segments.Any(s => s.TeamSlot <= 0)) return 0;
        var size = new[] { TeamType.Team1, TeamType.Team2 }.ToDictionary(t => t,
            t => m.Players.Where(Original).Where(p => Team(p) == t).SelectMany(p => p.Presences.Take(1)).Select(s => s.TeamSlot).Distinct().Count());
        var times = segments.SelectMany(s => new[] { Math.Clamp(s.JoinedAt, start, m.EndedAt), Math.Clamp(s.LeftAt ?? m.EndedAt, start, m.EndedAt) })
            .Append(start).Append(m.EndedAt).Distinct().Order().ToArray();
        double missing = 0;
        for (int i = 0; i + 1 < times.Length; i++)
        {
            int Present(TeamType t) => segments.Where(s => s.Team == t && s.JoinedAt <= times[i] && (s.LeftAt ?? m.EndedAt) > times[i]).Select(s => s.TeamSlot).Distinct().Count();
            missing += (double)(times[i+1]-times[i]) * (Math.Max(0,size[TeamType.Team1]-Present(TeamType.Team1)) - Math.Max(0,size[TeamType.Team2]-Present(TeamType.Team2)));
        }
        return Math.Clamp(missing / (m.EndedAt-start), -1, 1);
    }

    public static string Loadout(CompletedMatchPresence p) => string.Join(",",p.Perks.Select(k=>k.Hash).Order()) + "/" +
        string.Join(",",p.Devices.Values.Select(k=>k.Hash).Order());

    public static double Percentile(double value, double[] reference) =>
        (reference.Count(x => x < value) + .5 * reference.Count(x => x == value)) / reference.Length;

    public static double Performance(CompletedMatchRecord m, CompletedMatchPlayer p, IEnumerable<MmrReference> references)
    {
        if (m.MatchType is null || m.ActiveStartedAt is null || Abandoned(m,p) || p.Presences.Any(s => s.LeaveKind == MatchLeaveKind.Disconnect)) return 0;
        if(p.RawStats.Values.Any(v=>!float.IsFinite(v)||v<0)||p.Stats.Values.Any(v=>v<0))return 0;
        var seconds = Seconds(m,p); var duration=(m.EndedAt-m.ActiveStartedAt.Value)/1000d;
        var heroes=p.Presences.Select(s=>s.HeroKey.Hash).Distinct().ToArray();
        var loadouts=p.Presences.Select(Loadout).Distinct().ToArray();
        if (loadouts.Length!=1 || heroes.Length!=1 || seconds<180 || seconds<.8*duration || p.RawStats.Count==0) return 0;
        var r=references.Where(r=>r.Hero==heroes[0] && r.Mode==m.GameModeKey.Hash && r.Map==m.MapKey.Hash && r.GameType==m.MatchType && r.Loadout==loadouts[0] &&
            r.Before<(long)m.StartedAt && r.Players>=20 && r.Kills is {Length:>=100}).OrderByDescending(r=>r.Before).FirstOrDefault();
        if(r is null || new[]{r.Kills,r.Assists,r.Damage}.Any(a=>a is null || a.Length<100 || a.Any(x=>!double.IsFinite(x)||x<0))) return 0;
        double Rate(double v)=>v*60/seconds;
        var combat=(Percentile(Rate(p.Stats.GetValueOrDefault(PlayerMatchStatType.Kill)),r.Kills)+
            Percentile(Rate(p.Stats.GetValueOrDefault(PlayerMatchStatType.Assist)),r.Assists)+
            Percentile(Rate(p.RawStats.GetValueOrDefault(ScoreType.DamagePlayerByHero)+p.RawStats.GetValueOrDefault(ScoreType.DamagePlayerByBlock)),r.Damage))/3;
        // Legacy healing can include self-healing; only separately audited counters may earn utility.
        var utility = p.EffectiveAllyHealing is { } healing && double.IsFinite(healing) && healing>=0 && r.EffectiveAllyHealing is {Length: >=100} heals && heals.All(x=>double.IsFinite(x)&&x>=0)
            ? 2*Percentile(Rate(healing),heals)-1 : 0;
        // Zero objective damage is neutral without opportunity/defense telemetry, not a failure.
        var objective = p.Stats.GetValueOrDefault(PlayerMatchStatType.Objective);
        var objectiveScore=objective>0 && r.Objective is {Length: >=100} objectives && objectives.All(x=>double.IsFinite(x)&&x>=0) ? Math.Max(0,2*Percentile(Rate(objective),objectives)-1):0;
        return Math.Clamp(.4*(2*combat-1)+.3*utility+.3*objectiveScore,-1,1);
    }

    public static List<MmrAward> Calculate(CompletedMatchRecord m, IReadOnlyDictionary<uint,Rating> ratings,
        IEnumerable<MmrReference>? references=null)
    {
        if (!Public(m.GameModeKey) || m.EndedAt<=m.StartedAt || m.Players.Count==0) return [];
        if(m.ActiveStartedAt>m.EndedAt || m.ActiveStartedAt<m.StartedAt || m.Players.Select(p=>p.PlayerId).Distinct().Count()!=m.Players.Count ||
            m.Players.Any(p=>p.Presences.Count==0 || p.Presences.Select(s=>s.Team).Distinct().Count()!=1 ||
                Team(p) is not (TeamType.Team1 or TeamType.Team2) || p.Presences.Any(s=>s.LeftAt<s.JoinedAt)))
            throw new ArgumentException("Invalid MMR participation record");
        var players=m.Players.Where(p=>ratings.ContainsKey(p.PlayerId)).ToArray();
        bool rated=players.Count(p=>Original(p)&&Team(p)==TeamType.Team1)>2 || players.Count(p=>Original(p)&&Team(p)==TeamType.Team2)>2;
        bool decided=m.Winner is TeamType.Team1 or TeamType.Team2;
        // Abandoned matches may penalize documented abandoners; unknown/server-invalid outcomes never do.
        bool valid=decided || m.EndReason==MatchEndReason.Abandoned;
        Dictionary<uint,Rating> Outcome(TeamType winner, bool originalsOnly=false, uint forceFullPlayer=0)
        {
            var selected=originalsOnly?players.Where(Original).ToArray():players;
            var teams=new[]{winner,winner==TeamType.Team1?TeamType.Team2:TeamType.Team1}.Select(t=>selected.Where(p=>Team(p)==t)
                .ToDictionary(p=>new Player<uint>(p.PlayerId, originalsOnly || p.PlayerId==forceFullPlayer || (Original(p) && !Abandoned(m,p) && p.Presences.Any(s=>s.LeaveKind==MatchLeaveKind.Disconnect)) ? 1 : Math.Clamp(Seconds(m,p)/Math.Max(1,(m.EndedAt-(m.ActiveStartedAt??m.StartedAt))/1000d),.01,1),1),p=>ratings[p.PlayerId])).ToArray();
            if(teams.Any(t=>t.Count==0))return [];
            return TrueSkillCalculator.CalculateNewRatings(Databases.DefaultGameInfo,teams,1,2).ToDictionary(x=>x.Key.Id,x=>x.Value);
        }
        // Initial roster is frozen for abandonment: later departures cannot cheapen a quitter's loss.
        var loss1=Outcome(TeamType.Team2,true); var loss2=Outcome(TeamType.Team1,true);
        var updated=decided?Outcome(m.Winner):new Dictionary<uint,Rating>();
        var exposure=m.ActiveStartedAt.HasValue?Exposure(m):0;
        var result=new List<MmrAward>();
        foreach(var p in players)
        {
            var old=ratings[p.PlayerId]; bool abandoned=valid&&Abandoned(m,p);
            var reason=!valid?"void":!Original(p)?"backfill":!rated?"small-match":abandoned?"abandonment":!decided?"void":"outcome";
            double b=0, delta=0, performance=0, shortage=0, sd=old.StandardDeviation;
            if(valid&&Original(p)&&rated)
            {
                if(abandoned)
                {
                    var loss=(Team(p)==TeamType.Team1?loss1:loss2).GetValueOrDefault(p.PlayerId);
                    b=loss is null?-.25:Math.Min(-.25,loss.Mean-old.Mean);
                    var finalLoss=Outcome(Team(p)==TeamType.Team1?TeamType.Team2:TeamType.Team1,false,p.PlayerId).GetValueOrDefault(p.PlayerId);
                    if(finalLoss is not null) b=Math.Min(b,finalLoss.Mean-old.Mean);
                    // At least the maximum ordinary full loss: leaving cannot avoid a weak-play penalty.
                    delta=1.25*b; sd=loss?.StandardDeviation??sd;
                }
                else if(decided&&updated.TryGetValue(p.PlayerId,out var next))
                {
                    b=next.Mean-old.Mean; performance=Performance(m,p,references??[]);
                    var h=Team(p)==TeamType.Team1?exposure:-exposure;
                    bool gap=p.Presences.Any(s=>s.LeaveKind==MatchLeaveKind.Disconnect);
                    // A reconnect does not earn a time discount or protection for its own absence.
                    double shortageFactor=gap?1:h>0?(b>0?1+.5*h:1-.75*h):(b>0?1-.75*Math.Abs(h):1);
                    shortage=b*(shortageFactor-1);
                    var performanceFactor=b>=0?1+.5*performance:performance>=0?1-.75*performance:1-.25*performance;
                    // Returning must not erase a possible weak-performance loss: use the ordinary
                    // loss ceiling, while preserving a genuine win and avoiding an abandonment cooldown.
                    if(gap && b<0) performanceFactor=1.25;
                    var factor=Math.Clamp(shortageFactor*performanceFactor,.25,b>=0?1.5:1.25);
                    delta=b*factor;
                    var variance=old.StandardDeviation*old.StandardDeviation;
                    sd=Math.Sqrt(Math.Max(.000001,variance+Math.Min(1,factor)*(next.StandardDeviation*next.StandardDeviation-variance)));
                }
            }
            result.Add(new(p.PlayerId,b,performance,shortage,delta,sd,abandoned,reason));
        }
        return result;
    }
}

[Table("MmrSettlements")]
public sealed class MmrSettlement
{
    [PrimaryKey] public string Id {get;set;}=""; // match/player: version cannot grant a second award
    public string MatchId {get;set;}="";
    public uint PlayerId {get;set;}
    public int Version {get;set;}=PersonalMmr.Version;
    public long EndedAt {get;set;}
    public double OldMean {get;set;}
    public double NewMean {get;set;}
    public double OldDeviation {get;set;}
    public double NewDeviation {get;set;}
    public double Base {get;set;}
    public double Performance {get;set;}
    public double Shortage {get;set;}
    public string Reason {get;set;}="";
    public bool Abandoned {get;set;}
}

[Table("MmrQueuePenalties")]
public sealed class MmrQueuePenalty
{
    [PrimaryKey] public uint PlayerId {get;set;}
    public long Until {get;set;}
}

public static class MmrQueueGuard
{
    private static readonly ConcurrentDictionary<uint,string> Pending=new();
    private static readonly ConcurrentDictionary<uint,long> Bans=new();
    public static void Reserve(uint player,string match)=>Pending[player]=match;
    public static bool Blocked(uint player,long now)=>Pending.ContainsKey(player)||Bans.GetValueOrDefault(player)>now;
    public static string Message(uint player,long now) => Pending.ContainsKey(player)
        ? "Finish or rejoin your current match. Matchmaking stays locked until its result is saved."
        : $"Matchmaking cooldown for leaving a match: {Math.Max(1,(int)Math.Ceiling((Bans.GetValueOrDefault(player)-now)/60000d))} minute(s) remaining.";
    public static void Release(uint player,string match)=>Pending.TryRemove(new KeyValuePair<uint,string>(player,match));
    public static void Ban(uint player,long until)=>Bans[player]=until;
    public static int CooldownMinutes(int previous24Hours)=>previous24Hours==0?5:previous24Hours==1?15:60;
}
