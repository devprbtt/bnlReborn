using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// A presentation-only marker follows the aura's existing authoritative membership.
// No prefab paths: older clients safely ignore it. No Buff effect: no buff scores.
public static class GenieHealingVisualRegistration
{
    public const string AuraId = "effect_hero_djinn_heavy_orb_heal_aura";
    public const string RecipientId = "effect_genie_healing_orb_recipient";
    public static readonly Key RecipientKey = new(RecipientId);

    public static void Register(List<Card> cards)
    {
        if (!cards.OfType<CardEffect>().Any(card => card.Id == RecipientId))
            cards.Add(new CardEffect { Id = RecipientId, Key = RecipientKey, Positive = true });
        foreach (var card in cards.OfType<CardEffect>()) RegisterAura(card);
    }

    public static void RegisterAura(Card card)
    {
        if (card is not CardEffect { Id: AuraId, Effect: ConstEffectAura aura }) return;
        aura.ConstantEffects ??= [];
        if (!aura.ConstantEffects.Contains(RecipientKey)) aura.ConstantEffects.Add(RecipientKey);
    }
}
