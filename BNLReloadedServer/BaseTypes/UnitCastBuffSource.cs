namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    private void RecordActivationBuff(IEnumerable<ConstEffectInfo> effects, EffectSource? source)
    {
        if (!IsDead && IsActive && !IsDropped && source?.Impact is
            { ActivationBuffMarker: { } marker, ActivationBuffRecipients: { } recipients } &&
            effects.Any(e => e.Key == marker)) recipients.Add(this);
    }
}
