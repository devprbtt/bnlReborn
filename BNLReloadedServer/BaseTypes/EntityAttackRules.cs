using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

// Server-only global_logic.entity_attacks. Omitted fields retain these defaults.
public sealed class EntityAttackRules
{
    public float BeamPlayerDamageMin { get; set; } = 100f * 250f / 1200f;
    public float BeamPlayerDamageMax { get; set; } = 100;
    public float FireballPlayerDamage { get; set; } = 40;

    // Resolve for every attack so replicated CDB edits also affect existing matches.
    public static EntityAttackRules Load()
    {
        var rules = Databases.Catalogue.GetCard<CardGlobalLogic>("global_logic")?.EntityAttacks
                    ?? new EntityAttackRules();
        rules.Validate();
        return rules;
    }

    public void Validate()
    {
        foreach (var value in new[] { BeamPlayerDamageMin, BeamPlayerDamageMax, FireballPlayerDamage })
            if (!float.IsFinite(value) || value < 0 || value > 100000)
                throw new InvalidDataException("Entity player damage must be finite and between 0 and 100000");
        if (BeamPlayerDamageMin > BeamPlayerDamageMax)
            throw new InvalidDataException("Entity beam minimum damage must not exceed maximum damage");
    }
}
