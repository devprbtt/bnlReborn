using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;

var checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

var tony = new Key("unit_hero_engineer");
var turret = new Key("device_engineer_turret");
var snowman = new Key("device_engineer_turret_snowman");
var tesla = new Key("device_magnus_tesla");
var bricks = new Key("device_block_brick");

((ServerCatalogue)Databases.Catalogue).Replicate([
    new CardDevice { Id = "device_engineer_turret" },
    new CardDevice { Id = "device_engineer_turret_snowman" },
    new CardDevice { Id = "device_magnus_tesla" },
    new CardDevice { Id = "device_block_brick" },
    new CardUnit
    {
        Id = "unit_hero_engineer",
        Data = new UnitDataPlayer
        {
            SpecialDevices = [turret, snowman],
            DefaultDevices = [bricks, bricks, bricks, bricks, bricks]
        }
    }
]);

Check(CatalogueHelper.IsHeroSpecialDevice(tony, turret), "Tony accepts his standard turret");
Check(CatalogueHelper.IsHeroSpecialDevice(tony, snowman), "Tony accepts his turret variant");
Check(!CatalogueHelper.IsHeroSpecialDevice(tony, tesla), "Tony rejects Vander's Tesla coil");

var corrupted = new Dictionary<int, Key> { [1] = tesla, [2] = bricks };
Check(CatalogueHelper.RepairHeroSpecialDevice(tony, corrupted, out var replaced, out var replacement),
    "cross-hero signature device is repaired");
Check(replaced == tesla && replacement == turret && corrupted[1] == turret,
    "repair records the Tesla and restores Tony's default turret");

var missing = new Dictionary<int, Key> { [2] = bricks };
Check(CatalogueHelper.RepairHeroSpecialDevice(tony, missing, out replaced, out replacement),
    "missing signature device is repaired");
Check(replaced == Key.None && replacement == turret && missing[1] == turret,
    "missing slot 1 receives Tony's default turret");

var validVariant = new Dictionary<int, Key> { [1] = snowman };
Check(!CatalogueHelper.RepairHeroSpecialDevice(tony, validVariant, out _, out _) && validVariant[1] == snowman,
    "valid turret variant is preserved");

var player = new PlayerData { PlayerId = 26, Nickname = "fixture" };
player.HeroLoadouts[tony] = new LobbyLoadout
{
    HeroKey = tony,
    SkinKey = Key.None,
    Devices = new Dictionary<int, Key> { [1] = tesla, [2] = bricks },
    Perks = []
};
Check(player.SanitizeAgainstCatalogue(), "profile sanitizer reports the corrupted Tony loadout");
Check(player.HeroLoadouts[tony].Devices![1] == turret,
    "profile sanitizer persists Tony's turret in slot 1");

var updates = new List<LobbyUpdate>();
var lobby = (GameLobby)RuntimeHelpers.GetUninitializedObject(typeof(GameLobby));
var lobbyData = new LobbyData();
lobbyData.Players[26] = new PlayerLobbyState
{
    PlayerId = 26,
    Hero = tony,
    Devices = new Dictionary<int, Key> { [1] = turret, [2] = bricks }
};
SetField(lobby, "<LobbyData>k__BackingField", lobbyData);
SetField(lobby, "_serviceLobby", RecordingLobbyService.Create(updates));
// The uninitialized lobby skips field initializers that SendLobbyUpdate reads.
SetField(lobby, "<BannedHeroes>k__BackingField", new HashSet<Key>());

lobby.UpdateDeviceSlot(26, 1, tesla);
Check(lobbyData.Players[26].Devices![1] == turret && updates.Count == 0,
    "live lobby rejects a Tesla write into Tony's signature slot");
lobby.UpdateDeviceSlot(26, 1, null);
Check(lobbyData.Players[26].Devices![1] == turret && updates.Count == 0,
    "live lobby rejects removal of Tony's signature device");
lobby.UpdateDeviceSlot(26, 1, snowman);
Check(lobbyData.Players[26].Devices![1] == snowman && updates.Count == 1,
    "live lobby accepts Tony's valid turret variant");
lobby.UpdateDeviceSlot(26, 7, bricks);
Check(!lobbyData.Players[26].Devices!.ContainsKey(7) && updates.Count == 1,
    "live lobby rejects out-of-range device slots");

// Brains III grants device slot 7 only while it is the selected class perk.
var brainsClass = new CardHeroClass { Id = "fixture_brains_class", Type = HeroClassType.Brains };
var brains1 = new CardPerk { Id = ClassPerkCatalogue.BrainsId, SlotType = PerkSlotType.Class, ClassPerk = new ClassPerkBalance() };
var brains2 = new CardPerk { Id = ClassPerkCatalogue.Brains2Id, SlotType = PerkSlotType.Class, ClassPerk = new ClassPerkBalance() };
var brains3 = new CardPerk { Id = ClassPerkCatalogue.Brains3Id, SlotType = PerkSlotType.Class, ClassPerk = new ClassPerkBalance() };
var medic = new CardUnit { Id = "unit_hero_fixture_brains", Data = new UnitDataPlayer { SpecialDevices = [turret] } };
((ServerCatalogue)Databases.Catalogue).Replicate(Databases.Catalogue.All
    .Concat(new Card[] { brainsClass, brains1, brains2, brains3, medic }).ToList());
((UnitDataPlayer)medic.Data!).Class = brainsClass.Key;
lobbyData.Players[27] = new PlayerLobbyState
{
    PlayerId = 27, Hero = medic.Key, Perks = [brains3.Key],
    Devices = new Dictionary<int, Key> { [1] = turret, [2] = bricks }
};
var medicState = lobbyData.Players[27];
updates.Clear();
lobby.UpdateDeviceSlot(27, 7, bricks);
Check(medicState.Devices![7] == bricks && updates.Count == 1, "Brains III accepts a device in slot 7");
lobby.UpdateDeviceSlot(27, 8, bricks);
Check(!medicState.Devices!.ContainsKey(8) && updates.Count == 1, "Brains III still rejects slot 8");
lobby.SwapDevices(27, 7, 3);
Check(medicState.Devices![3] == bricks && !medicState.Devices.ContainsKey(7), "Brains III swaps slot 7 into an empty slot");
lobby.SwapDevices(27, 3, 7);
Check(medicState.Devices![7] == bricks && !medicState.Devices.ContainsKey(3), "Brains III swaps back into slot 7");
lobby.SwapDevices(27, 7, 9);
Check(medicState.Devices![7] == bricks && !medicState.Devices.ContainsKey(9), "swap into slot 9 is rejected");
lobby.SelectPerk(27, brains2.Key);
Check(!medicState.Devices!.ContainsKey(7) && medicState.Devices[2] == bricks, "switching to Brains II drops only slot 7");
lobby.UpdateDeviceSlot(27, 7, bricks);
Check(!medicState.Devices!.ContainsKey(7), "Brains II rejects slot 7");
lobby.SelectPerk(27, brains3.Key);
lobby.UpdateDeviceSlot(27, 7, bricks);
Check(medicState.Devices![7] == bricks, "reselecting Brains III reopens slot 7");
lobby.DeselectPerk(27, brains3.Key);
Check(!medicState.Devices!.ContainsKey(7), "deselecting Brains III (default Brains I) drops slot 7");
var stored = new Dictionary<int, Key> { [1] = turret, [7] = bricks };
Check(!ClassPerkCatalogue.TrimDevices(medic.Key, [brains3.Key], stored) && stored.ContainsKey(7) &&
      ClassPerkCatalogue.TrimDevices(medic.Key, [brains2.Key], stored) && !stored.ContainsKey(7),
    "TrimDevices keeps slot 7 for Brains III and removes it otherwise");

Console.WriteLine($"Hero special-device suite passed: {checks} checks.");

static void SetField(object target, string name, object value)
{
    var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    field.SetValue(target, value);
}

class RecordingLobbyService : DispatchProxy
{
    private List<LobbyUpdate> _updates = null!;

    public static IServiceLobby Create(List<LobbyUpdate> updates)
    {
        var proxy = Create<IServiceLobby, RecordingLobbyService>();
        ((RecordingLobbyService)(object)proxy)._updates = updates;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IServiceLobby.SendLobbyUpdate))
            _updates.Add((LobbyUpdate)args![0]!);
        return targetMethod?.ReturnType == typeof(bool) ? false : null;
    }
}
