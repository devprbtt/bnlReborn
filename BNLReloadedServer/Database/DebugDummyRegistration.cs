using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class DebugDummyRegistration
{
    public const string Id = "unit_reborn_debug_dummy";
    public const int MaxHealth = 10_000_000;
    public static readonly Key Key = new(Id);
    public const string ClassId = "hero_class_reborn_debug_dummy";
    public static readonly Key ClassKey = new(ClassId);

    public static void Register(List<Card> cards)
    {
        if (cards.OfType<CardUnit>().Any(c => c.Id == Id) ||
            cards.OfType<CardUnit>().FirstOrDefault(c => c.Id == "unit_hero_sarge_stone") is not { } source) return;
        // Copy presentation/hero hitboxes without mutating the real hero card.
        using var buffer = new MemoryStream();
        source.Write(new BinaryWriter(buffer));
        buffer.Position = 0;
        var card = new CardUnit();
        card.Read(new BinaryReader(buffer));
        card.Id = Id; card.Key = Key;
        card.Name = new LocalizedString { Text = "Test Dummy", Data = [] };
        card.Health = new UnitHealth { Health = new Health { MaxHealth = 1, HealthType = HealthType.Player } };
        card.Loot = null; card.SpawnPoint = null; card.Capturing = null; card.CountLimit = null; card.Lifetime = null;
        card.Labels = [UnitLabel.DestroyOnMatchEnd];
        card.InitEffects = []; card.EnabledEffects = [];
        card.DeviceType = DeviceType.None; card.BaseCost = null;
        card.Movement = new UnitMovementStatic();
        if (card.Data is UnitDataPlayer data)
        {
            // Shipped clients resolve every player-style card's class while building
            // the Catalogue block browser. None throws and strands the menu transition.
            // A dedicated target class with no devices keeps the dummy out of
            // block availability lists without advertising it as a playable hero.
            if (!cards.OfType<CardHeroClass>().Any(c => c.Id == ClassId))
                cards.Add(new CardHeroClass {
                    Id = ClassId, Key = ClassKey, Scope = ScopeType.Public,
                    Type = HeroClassType.Brawn,
                    Name = new LocalizedString { Text = "Test Dummy", Data = [] },
                    AvailableDevices = []
                });
            data.Class = ClassKey; data.ActiveAbilityKey = Key.None;
            data.Gears = []; data.Passive = []; data.SpecialDevices = []; data.DefaultDevices = [];
        }
        cards.Add(card);
    }
}
