using System.Globalization;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class ClassPerkCatalogue
{
    public const string BrawnId = "perk_class_brawn_1";
    public const string BrainsId = "perk_class_brains_1";
    public const string SkillId = "perk_class_skill_1";
    public static bool IsClassPerk(string? id) => id is BrawnId or BrainsId or SkillId;
    public static ClassPerkBalance Balance(string id) => Databases.Catalogue.GetCard<CardPerk>(id)?.ClassPerk ?? new ClassPerkBalance();
    public static long QuietMilliseconds(string id) => (long)Math.Round(Balance(id).OutOfCombatSeconds * 1000d);
    public static void Validate(CardPerk card)
    {
        if (!IsClassPerk(card.Id) && card.ClassPerk == null) return;
        if (!IsClassPerk(card.Id) || card.SlotType != PerkSlotType.Class || card.ClassPerk == null)
            throw new InvalidDataException("Class perk requires a known class-perk id, class slot and class_perk settings.");
        var b = card.ClassPerk;
        static void Range(float n, float min, float max, string field)
        { if (!float.IsFinite(n) || n < min || n > max) throw new InvalidDataException($"Invalid class_perk.{field}: expected {min}..{max}."); }
        Range(b.OutOfCombatSeconds, 0, 60, "out_of_combat_seconds");
        Range(b.HealingReturnPercent, 0, 100, "healing_return_percent");
        Range(b.RegenMaxHealthPercentPerSecond, 0, 100, "regen_max_health_percent_per_second");
        Range(b.SpawnDistance, 1, 3, "spawn_distance");
    }
    public static string? Resolve(CardPerk card, string? text)
    {
        if (card.ClassPerk is not { } b || text == null) return text;
        static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        return text.Replace("{out_of_combat_seconds}", F(b.OutOfCombatSeconds))
            .Replace("{healing_return_percent}", F(b.HealingReturnPercent))
            .Replace("{regen_max_health_percent_per_second}", F(b.RegenMaxHealthPercentPerSecond))
            .Replace("{spawn_distance}", F(b.SpawnDistance));
    }
    public static LocalizedString? Resolve(CardPerk card, LocalizedString? text)
    {
        if (text == null || card.ClassPerk == null) return text;
        // Templates are English until translated templates are authored in CDB.
        // Never transmit an old translation with unadjusted balance values.
        return new LocalizedString { Text = Resolve(card, text.Text), Data = [] };
    }
}
