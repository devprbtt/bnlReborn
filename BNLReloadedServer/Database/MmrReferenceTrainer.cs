using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class MmrReferenceTrainer
{
    // Only v2 telemetry. Legacy aggregate healing and build-inclusive duration cannot silently
    // become reference data. The resulting versioned file is an offline/shadow calibration artifact.
    public static List<MmrReference> Build(IEnumerable<CompletedMatchRecord> matches, long before)
        => BuildCore(matches,before,false);

    public static List<MmrReference> BuildBeta(IEnumerable<CompletedMatchRecord> matches, long before)
        => BuildCore(matches,before,true);

    private static List<MmrReference> BuildCore(IEnumerable<CompletedMatchRecord> matches, long before,bool beta)
    {
        var samples=new List<(CompletedMatchRecord m,CompletedMatchPlayer p,double minutes,uint hero)>();
        foreach(var m in matches.Where(m=>m.RatingVersion==2 && PersonalMmr.Public(m.GameModeKey) &&
            m.ActiveStartedAt.HasValue && m.MatchType.HasValue && m.EndedAt>m.ActiveStartedAt && (long)m.EndedAt<before &&
            (!beta || (long)m.EndedAt>=before-30L*86400000) &&
            m.Winner is TeamType.Team1 or TeamType.Team2 &&
            (!beta || m.Players.Count(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==TeamType.Team1)>2 ||
                m.Players.Count(p=>PersonalMmr.Original(p)&&PersonalMmr.Team(p)==TeamType.Team2)>2)).GroupBy(m=>m.MatchId).Select(g=>g.First()))
        foreach(var p in m.Players.Where(p=>PersonalMmr.Original(p)&&!PersonalMmr.Abandoned(m,p)))
        {
            var heroes=p.Presences.Select(s=>s.HeroKey.Hash).Distinct().ToArray();
            if(p.Presences.Select(PersonalMmr.Loadout).Distinct().Count()!=1)continue;var seconds=PersonalMmr.Seconds(m,p);
            if(heroes.Length!=1||seconds<180||seconds<.8*(m.EndedAt-m.ActiveStartedAt!.Value)/1000d||
                p.Presences.Any(s=>s.LeaveKind==MatchLeaveKind.Disconnect)||p.RawStats.Count==0||
                p.RawStats.Values.Any(v=>!float.IsFinite(v)||v<0)||p.Stats.Values.Any(v=>v<0)||
                !p.Stats.ContainsKey(PlayerMatchStatType.Kill)||!p.Stats.ContainsKey(PlayerMatchStatType.Assist))continue;
            samples.Add((m,p,seconds/60,heroes[0]));
        }
        return samples.GroupBy(s=>(s.hero,s.m.GameModeKey,MapKey:beta?Key.FromHash(0):s.m.MapKey,s.m.MatchType,Loadout:beta?"*":PersonalMmr.Loadout(s.p.Presences[0])))
            .Where(g=>g.Count()>=(beta?10:100)&&g.Select(s=>s.p.PlayerId).Distinct().Count()>=(beta?5:20))
            .Select(g=>new MmrReference(g.Key.hero,g.Key.GameModeKey.Hash,g.Key.MapKey.Hash,before,$"{(beta?"beta-v2":"v2")}-{before}",g.Select(s=>s.p.PlayerId).Distinct().Count(),
                g.Select(s=>s.p.Stats.GetValueOrDefault(PlayerMatchStatType.Kill)/s.minutes).ToArray(),
                g.Select(s=>s.p.Stats.GetValueOrDefault(PlayerMatchStatType.Assist)/s.minutes).ToArray(),
                g.Select(s=>(s.p.RawStats.GetValueOrDefault(ScoreType.DamagePlayerByHero)+s.p.RawStats.GetValueOrDefault(ScoreType.DamagePlayerByBlock))/s.minutes).ToArray(),
                g.All(s=>s.p.EffectiveAllyHealing.HasValue)?g.Select(s=>s.p.EffectiveAllyHealing!.Value/s.minutes).ToArray():null,
                g.All(s=>s.p.Stats.ContainsKey(PlayerMatchStatType.Objective))?g.Select(s=>s.p.Stats[PlayerMatchStatType.Objective]/s.minutes).ToArray():null,g.Key.MatchType,g.Key.Loadout,beta?"beta-v2":"exact")).ToList();
    }
}
