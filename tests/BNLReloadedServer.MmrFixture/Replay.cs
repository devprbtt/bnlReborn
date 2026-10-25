using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using Moserware.Skills;

internal static class Replay
{
    static long L(JsonElement e,string key)=>e.GetProperty(key).GetInt64();
    static double? D(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind!=JsonValueKind.Null?v.GetDouble():null;
    static Dictionary<T,V> Map<T,V>(string? hex,Func<byte,T> key,Func<BinaryReader,V> value) where T:notnull
    {
        if(string.IsNullOrEmpty(hex))return [];
        using var r=new BinaryReader(new MemoryStream(Convert.FromHexString(hex)));
        var n=r.Read7BitEncodedInt();var result=new Dictionary<T,V>();
        for(int i=0;i<n;i++)result.Add(key(r.ReadByte()),value(r));
        if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Trailing map data");
        return result;
    }
    // Normal CDF (Abramowitz-Stegun), only for descriptive next-result scoring.
    static double Cdf(double z)
    {
        var x=Math.Abs(z);var t=1/(1+.2316419*x);
        var tail=Math.Exp(-x*x/2)/Math.Sqrt(2*Math.PI)*t*(.319381530+t*(-.356563782+t*(1.781477937+t*(-1.821255978+t*1.330274429))));
        return Math.Clamp(z>=0?1-tail:tail,.000001,.999999);
    }
    static double Predict(CompletedMatchRecord m,Dictionary<uint,Rating> ratings)
    {
        var p=m.Players.Where(PersonalMmr.Original).ToArray();
        var difference=p.Sum(p=>(PersonalMmr.Team(p)==TeamType.Team1?1:-1)*ratings[p.PlayerId].Mean);
        var variance=p.Sum(p=>Math.Pow(ratings[p.PlayerId].StandardDeviation,2)+Math.Pow(Databases.DefaultBeta,2));
        return Cdf(difference/Math.Sqrt(variance));
    }
    public static void Run(string source,string output)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(source));var root=json.RootElement;
        var presences=root.GetProperty("presences").EnumerateArray().GroupBy(p=>(p.GetProperty("match_id").GetString()!,L(p,"player_id"))).ToDictionary(g=>g.Key,g=>g.ToList());
        var players=root.GetProperty("players").EnumerateArray().GroupBy(p=>p.GetProperty("match_id").GetString()!).ToDictionary(g=>g.Key,g=>g.ToList());
        var baseline=new Dictionary<uint,Rating>();var candidate=new Dictionary<uint,Rating>();var sensitivity=new Dictionary<uint,Rating>();
        var results=new List<object>();var details=new List<object>();var excluded=new List<string>();
        var scores=new List<(double b,double c,double s,int y)>();int defaults=0,appearances=0,abandoners=0,backfills=0;
        double baselineDrift=0,candidateDrift=0,sensitivityDrift=0;int minMatches=0;
        var heroCount=new Dictionary<uint,int>();
        foreach(var mm in root.GetProperty("matches").EnumerateArray().OrderBy(m=>L(m,"ended_at_ms")))
        {
            var id=mm.GetProperty("id").GetString()!;
            var m=new CompletedMatchRecord{MatchId=id,RatingVersion=2,StartedAt=(ulong)L(mm,"started_at_ms"),EndedAt=(ulong)L(mm,"ended_at_ms"),
                MapKey=Key.FromHash((uint)L(mm,"map_key")),GameModeKey=Key.FromHash((uint)L(mm,"game_mode_key")),Winner=(TeamType)L(mm,"winner"),EndReason=(MatchEndReason)L(mm,"end_reason")};
            if(!players.TryGetValue(id,out var pp)){excluded.Add(id+": no players");continue;}
            bool invalid=false;
            foreach(var p in pp)
            {
                var playerId=(uint)L(p,"player_id");
                if(!presences.TryGetValue((id,(long)playerId),out var ps)||ps.Count==0||ps.Select(s=>L(s,"team")).Distinct().Count()!=1||ps.Any(s=>L(s,"team") is not (1 or 2))) {invalid=true;break;}
                var row=new CompletedMatchPlayer{PlayerId=playerId,WasInitial=L(p,"was_initial")==1,WasBackfiller=L(p,"was_backfiller")==1,
                    StartingRatingMean=D(p,"starting_rating_mean"),StartingRatingDeviation=D(p,"starting_rating_deviation"),
                    Stats=Map(p.GetProperty("stats").GetString(),b=>(PlayerMatchStatType)b,r=>r.ReadInt32()),
                    RawStats=Map(p.GetProperty("raw_stats").GetString(),b=>(ScoreType)b,r=>r.ReadSingle()),
                    Presences=ps.Select(s=>new CompletedMatchPresence{Sequence=(int)L(s,"sequence"),TeamSlot=(int)L(s,"team_slot"),Team=(TeamType)L(s,"team"),
                        HeroKey=Key.FromHash((uint)L(s,"hero_key")),JoinedAt=(ulong)L(s,"joined_at_ms"),LeftAt=D(s,"left_at_ms") is {} left?(ulong)left:null,
                        JoinKind=(MatchJoinKind)L(s,"join_kind"),LeaveKind=D(s,"leave_kind") is {} kind?(MatchLeaveKind)(int)kind:null}).OrderBy(s=>s.Sequence).ToList()};
                if(row.Presences.Any(s=>s.LeftAt<s.JoinedAt)){invalid=true;break;}
                m.Players.Add(row);
            }
            if(invalid||m.EndedAt<=m.StartedAt||new[]{TeamType.Team1,TeamType.Team2}.Any(t=>!m.Players.Any(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==t)))
            {excluded.Add(id+": malformed or missing initial team");continue;}
            foreach(var p in m.Players)
            {
                if(!baseline.ContainsKey(p.PlayerId))
                {
                    if(p.StartingRatingMean is null||p.StartingRatingDeviation is null)defaults++;
                    var r=new Rating(p.StartingRatingMean??25,p.StartingRatingDeviation??Databases.DefaultSd);
                    baseline[p.PlayerId]=r;candidate[p.PlayerId]=r;sensitivity[p.PlayerId]=r;
                }
            }
            bool decided=m.Winner is TeamType.Team1 or TeamType.Team2;
            if(decided)scores.Add((Predict(m,baseline),Predict(m,candidate),Predict(m,sensitivity),m.Winner==TeamType.Team1?1:0));
            double bsum=0;
            bool rated=m.Players.Count(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==TeamType.Team1)>2||m.Players.Count(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==TeamType.Team2)>2;
            if(decided&&rated)
            {
                // Outcome-only comparator on the original roster, not an exact reproduction of legacy quit bugs.
                var teams=new[]{m.Winner,m.Winner==TeamType.Team1?TeamType.Team2:TeamType.Team1}.Select(t=>m.Players.Where(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==t).ToDictionary(p=>new Player<uint>(p.PlayerId),p=>baseline[p.PlayerId])).ToArray();
                foreach(var (p,r) in TrueSkillCalculator.CalculateNewRatings(Databases.DefaultGameInfo,teams,1,2)) {bsum+=r.Mean-baseline[p.Id].Mean;baseline[p.Id]=r;}
            }
            if(!rated)minMatches++;
            var awards=PersonalMmr.Calculate(m,candidate);
            // Historical active-phase boundaries are absent. This second run explicitly treats the
            // archive start (build phase) as active; it is sensitivity analysis, never authoritative replay.
            m.ActiveStartedAt=m.StartedAt;
            var sensitivityAwards=PersonalMmr.Calculate(m,sensitivity);
            foreach(var a in awards)
            {
                candidate[a.PlayerId]=new(candidate[a.PlayerId].Mean+a.Delta,a.Deviation);
                if(a.Abandoned)abandoners++;if(a.Reason=="backfill")backfills++;
                var sa=sensitivityAwards.Single(s=>s.PlayerId==a.PlayerId);sensitivity[a.PlayerId]=new(sensitivity[a.PlayerId].Mean+sa.Delta,sa.Deviation);
                var p=m.Players.Single(p=>p.PlayerId==a.PlayerId);var hs=p.Presences.Select(s=>s.HeroKey.Hash).Distinct().ToArray();
                if(hs.Length==1&&PersonalMmr.Original(p)&&!a.Abandoned)heroCount[hs[0]]=heroCount.GetValueOrDefault(hs[0])+1;
                details.Add(new{match=id,player=a.PlayerId,reason=a.Reason,abandoned=a.Abandoned,delta=a.Delta*100,sensitivity_delta=sa.Delta*100});
            }
            appearances+=m.Players.Count;baselineDrift+=bsum*100;candidateDrift+=awards.Sum(a=>a.Delta)*100;sensitivityDrift+=sensitivityAwards.Sum(a=>a.Delta)*100;
            results.Add(new{match=id,ended_at=m.EndedAt,players=m.Players.Count,baseline_delta=bsum*100,candidate_delta=awards.Sum(a=>a.Delta)*100,sensitivity_delta=sensitivityAwards.Sum(a=>a.Delta)*100});
        }
        object Metrics(Func<(double b,double c,double s,int y),double> p)=>new{brier=scores.Average(x=>Math.Pow(p(x)-x.y,2)),log_loss=scores.Average(x=>-x.y*Math.Log(p(x))-(1-x.y)*Math.Log(1-p(x)))};
        var report=new{scope="Chronological conditional replay. Comparator is outcome-only original-roster TrueSkill, not exact legacy production. No historical penalties applied.",
            captured_utc=root.GetProperty("captured_utc").GetString(),window_start_ms=L(root,"window_start_ms"),window_end_ms=L(root,"window_end_ms"),
            raw_matches=root.GetProperty("matches").GetArrayLength(),raw_appearances=root.GetProperty("players").GetArrayLength(),matches=results.Count,appearances,
            players=baseline.Count,excluded,missing_seed_defaults=defaults,small_matches=minMatches,flagged_abandonments=abandoners,backfills,
            ranked_matches=root.GetProperty("matches").EnumerateArray().Count(m=>L(m,"game_mode_key")==1844575328),
            baseline_total_delta=baselineDrift,candidate_total_delta=candidateDrift,sensitivity_total_delta=sensitivityDrift,
            predicted_matches=scores.Count,baseline_prediction=Metrics(x=>x.b),candidate_prediction=Metrics(x=>x.c),sensitivity_prediction=Metrics(x=>x.s),
            hero_appearances=heroCount,performance_adjustments=0,
            limitations=new[]{"No historical active-phase boundary, dedicated effective-ally-healing or opportunity counters; performance remains neutral.",
                "Older duplicate-finalization archives are not certified clean; recorded departures can include end-screen timing ambiguity. Flagged abandonment counts are potential policy classifications, not moderation findings.",
                "Missing initial uncertainty is seeded with server default and counted. Comparisons use recorded first-seen means; no rating reset.",
                "No causal hero weights fitted and no claim of predictive improvement. Repeated players and only a small ranked subset limit inference.",
                "Sensitivity enables shortage exposure using archive/build start; primary candidate disables shortage without an active-phase boundary."},results,details};
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"MMR_REPLAY_OK matches={results.Count} appearances={appearances} abandonments={abandoners} backfills={backfills} defaults={defaults}");
    }
}
