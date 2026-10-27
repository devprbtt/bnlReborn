using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class DebugDummyRegistration
{
    public const string Id = "unit_reborn_debug_dummy";
    public const int MaxHealth = 10_000_000;
    public static readonly Key Key = new(Id);

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
            data.Class = Key.None; data.ActiveAbilityKey = Key.None;
            data.Gears = []; data.Passive = []; data.SpecialDevices = []; data.DefaultDevices = [];
        }
        cards.Add(card);
    }
}
