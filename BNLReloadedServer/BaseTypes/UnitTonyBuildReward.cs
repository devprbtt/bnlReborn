namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    public const string TonyBuildCastEffectId = "effect_hero_engineer_bfs_build_cast";

    private void ReportTonyBuildReceived(IEnumerable<ConstEffectInfo> effects, EffectSource? source)
    {
        if (PlayerId is not { } recipient || IsDead || !IsActive || IsDropped || Team == TeamType.Neutral ||
            source is not UnitSource { Unit.PlayerId: not null, Impact: { } impact } caster ||
            caster.Unit.Team != Team ||
            !ScoreCardFamily(ScoreSourceId(impact.SourceKey), "ability_engineer_bfs") ||
            !effects.Any(e => e.Card.Id == TonyBuildCastEffectId && e.HasDuration)) return;
        // The source belongs to this cast, not to the persistent buff. Repeated
        // application/ticks/cloned impacts can therefore never pay it twice.
        impact.TonyBuildRewardRecipients ??= [];
        if (!impact.TonyBuildRewardRecipients.Add(recipient)) return;
        ReportActionScore("Build boost received", 1, GetMidpoint(), caster.Unit.Id);
    }
}
