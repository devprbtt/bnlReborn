using System.Globalization;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class ClassPerkCatalogue
{
    public const string BrawnId = "perk_class_brawn_1";
    public const string BrainsId = "perk_class_brains_1";
    public const string SkillId = "perk_class_skill_1";
    public const string Brawn2Id = "perk_class_brawn_2";
    public const string Brawn3Id = "perk_class_brawn_3";
    public const string Brains2Id = "perk_class_brains_2";
    public const string Brains3Id = "perk_class_brains_3";
    public const string Skill2Id = "perk_class_skill_2";
    public const string Skill3Id = "perk_class_skill_3";

    private static readonly Dictionary<string, HeroClassType> Classes = new()
    {
        [BrawnId] = HeroClassType.Brawn, [Brawn2Id] = HeroClassType.Brawn, [Brawn3Id] = HeroClassType.Brawn,
        [BrainsId] = HeroClassType.Brains, [Brains2Id] = HeroClassType.Brains, [Brains3Id] = HeroClassType.Brains,
        [SkillId] = HeroClassType.Skills, [Skill2Id] = HeroClassType.Skills, [Skill3Id] = HeroClassType.Skills
    };

    public static bool IsClassPerk(string? id) => id != null && Classes.ContainsKey(id);
    public static HeroClassType? ClassOf(string? id) => id != null && Classes.TryGetValue(id, out var type) ? type : null;
    public static string? DefaultFor(HeroClassType? type) => type switch
    {
        HeroClassType.Brawn => BrawnId,
        HeroClassType.Brains => BrainsId,
        HeroClassType.Skills => SkillId,
        _ => null
    };

    public static HeroClassType? HeroClass(Key hero) =>
        (Databases.Catalogue.GetCard<CardUnit>(hero)?.Data as UnitDataPlayer)?.Class.GetCard<CardHeroClass>()?.Type;

    /// <summary>
    /// The class perk a hero plays with: the selected class card when it belongs to the hero's class and is in the
    /// catalogue, otherwise the class default (I). Loadouts saved before II/III existed hold no class key.
    /// </summary>
    public static string? Selected(Key hero, IEnumerable<Key>? perks)
    {
        var type = HeroClass(hero);
        var chosen = perks?.Select(p => Databases.Catalogue.GetCard<CardPerk>(p))
            .FirstOrDefault(card => card?.SlotType == PerkSlotType.Class && ClassOf(card.Id) == type);
        return chosen?.Id ?? DefaultFor(type);
    }

    /// <summary>
    /// Perks a spawned hero receives: regular perks plus exactly one class perk of its own class, so class cards'
    /// perk_mods (build speed, auras, immunities) apply to defaults too and a foreign class key never does.
    /// </summary>
    public static List<Key> EffectivePerks(Key hero, IEnumerable<Key>? perks, out string? classPerkId)
    {
        classPerkId = Selected(hero, perks);
        var list = (perks ?? []).Where(p => Databases.Catalogue.GetCard<CardPerk>(p)?.SlotType != PerkSlotType.Class).ToList();
        if (classPerkId != null && Databases.Catalogue.GetCard<CardPerk>(Catalogue.Key(classPerkId)) != null)
            list.Add(Catalogue.Key(classPerkId));
        return list;
    }

    public const int BaseDeviceSlots = 6;

    /// <summary>Device (block) slots a hero may fill: Brains III adds slot 7.</summary>
    public static int DeviceSlots(Key hero, IEnumerable<Key>? perks) =>
        Selected(hero, perks) == Brains3Id ? BaseDeviceSlots + 1 : BaseDeviceSlots;

    /// <summary>Drops devices in slots the selected class perk does not grant. Returns true when one was removed.</summary>
    public static bool TrimDevices(Key hero, IEnumerable<Key>? perks, Dictionary<int, Key>? devices)
    {
        if (devices == null) return false;
        var max = DeviceSlots(hero, perks);
        var extra = devices.Keys.Where(slot => slot > max).ToList();
        foreach (var slot in extra) devices.Remove(slot);
        return extra.Count != 0;
    }

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
        Range(b.RespawnReductionPercent, 0, 100, "respawn_reduction_percent");
        if (b.HealedAllyEffect is { } effect && string.IsNullOrWhiteSpace(effect))
            throw new InvalidDataException("Invalid class_perk.healed_ally_effect: expected an effect card id.");
    }
    public static string? Resolve(CardPerk card, string? text)
    {
        if (card.ClassPerk is not { } b || text == null) return text;
        static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        return text.Replace("{out_of_combat_seconds}", F(b.OutOfCombatSeconds))
            .Replace("{healing_return_percent}", F(b.HealingReturnPercent))
            .Replace("{regen_max_health_percent_per_second}", F(b.RegenMaxHealthPercentPerSecond))
            .Replace("{spawn_distance}", F(b.SpawnDistance))
            .Replace("{respawn_reduction_percent}", F(b.RespawnReductionPercent));
    }
    public static LocalizedString? Resolve(CardPerk card, LocalizedString? text)
    {
        if (text == null || card.ClassPerk == null) return text;
        // Templates are English until translated templates are authored in CDB.
        // Never transmit an old translation with unadjusted balance values.
        return new LocalizedString { Text = Resolve(card, text.Text), Data = [] };
    }
}
