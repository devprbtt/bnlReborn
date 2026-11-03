// Real-catalogue checks for loadout validation: another hero's perks or another class's devices must not get
// into a saved loadout (SetLoadout), a loaded one (GetLoadoutForHero) or the lobby, while every legitimate loadout
// (defaults, own hero perks, own class devices and their skins) still passes.
// Usage: dotnet run -- <live-cards.json>
using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

int checks = 0, failures = 0;
void Check(bool pass, string name) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); }

var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
Key K(string id) => Catalogue.Key(id);
var genie = K("unit_hero_djinn");
var eliza = K("unit_hero_doc_eliza");
var radar = K("device_generic_radar");
var elizaPerk = CatalogueHelper.GlobalLogic.Perks!.Heroes![eliza][0];
var geniePerk = CatalogueHelper.GlobalLogic.Perks!.Heroes![genie][0];

// The reported exploit: Genie with a Doc Eliza hero perk and the Brawn-only radar next to heal and ammo stations.
Check(!LoadoutRules.CanUseDevice(genie, radar), "radar is not a Genie (Brains) device");
Check(LoadoutRules.CanUseDevice(K("unit_hero_abe"), radar), "radar is a Brawn device");
Check(LoadoutRules.CanUseDevice(genie, K("device_generic_heal_station_djinn")), "Genie may use a heal station skin of her class device");
Check(!LoadoutRules.CanUseDevice(genie, K("device_eliza_acid_trap")), "Genie may not use Doc's special device");
Check(!LoadoutRules.CanUsePerk(genie, elizaPerk), "Genie may not use a Doc Eliza hero perk");
Check(LoadoutRules.CanUsePerk(genie, geniePerk), "Genie may use her own hero perk");

var devices = CatalogueHelper.GetDefaultDevices(genie)!;
devices[2] = radar;
var perks = new List<Key> { elizaPerk };
var removed = LoadoutRules.Sanitize(genie, devices, perks);
Check(removed.Contains(radar) && removed.Contains(elizaPerk) && !devices.ContainsValue(radar) && perks.Count == 0,
    "sanitizing the reported loadout drops the radar and the Doc perk");
Check(devices.Count == 5 && devices[1] == K("device_djinn_lamp"), "the rest of that loadout is kept");

// Every hero's legitimate loadouts survive untouched.
var classPerks = CatalogueHelper.GetCards<CardPerk>(CardCategory.Perk).Where(p => p.SlotType == PerkSlotType.Class).ToList();
int heroes = 0, intact = 0;
foreach (var hero in CatalogueHelper.GetHeroes())
{
    heroes++;
    var ok = true;
    var defaults = CatalogueHelper.GetDefaultDevices(hero)!;
    ok &= LoadoutRules.Sanitize(hero, defaults.ToDictionary(), []).Count == 0;
    foreach (var perk in CatalogueHelper.GlobalLogic.Perks.Heroes.GetValueOrDefault(hero) ?? [])
        ok &= LoadoutRules.Sanitize(hero, defaults.ToDictionary(), [perk]).Count == 0;
    var heroClass = ClassPerkCatalogue.HeroClass(hero);
    foreach (var perk in classPerks)
        ok &= LoadoutRules.CanUsePerk(hero, perk.Key) == (ClassPerkCatalogue.ClassOf(perk.Id) == heroClass);
    var heroData = (UnitDataPlayer)hero.GetCard<CardUnit>()!.Data!;
    foreach (var baseDevice in heroData.Class.GetCard<CardHeroClass>()!.AvailableDevices!)
        ok &= LoadoutRules.CanUseDevice(hero, baseDevice);
    if (ok) intact++; else Console.WriteLine($"  rejected a legitimate item for {hero}");
}
Check(heroes >= 15 && intact == heroes, $"defaults, own hero perks, own class perks and class devices pass for all {heroes} heroes");
Check(LoadoutRules.Sanitize(genie, CatalogueHelper.GetDefaultDevices(genie), [geniePerk, CatalogueHelper.GlobalLogic.Perks.Heroes[genie][1]]).Count == 1,
    "only one hero perk per loadout");

// The SetLoadout RPC (the hero menu outside the lobby) stores only the checked loadout.
var saved = new List<LobbyLoadout>();
var database = DispatchProxy.Create<IPlayerDatabase, RecordingDatabase>();
((RecordingDatabase)(object)database).Saved = saved;
var sender = DispatchProxy.Create<ISender, RecordingDatabase>();
sender.AssociatedPlayerId = 7;
var service = (ServicePlayer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ServicePlayer));
var fields = typeof(ServicePlayer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
fields.First(f => f.FieldType == typeof(ISender)).SetValue(service, sender);
fields.First(f => f.Name == "_playerDatabase").SetValue(service, database);
void SetLoadout(LobbyLoadout loadout)
{
    var stream = new MemoryStream();
    var writer = new BinaryWriter(stream);
    writer.Write((byte)30);
    LobbyLoadout.WriteRecord(writer, loadout);
    stream.Position = 0;
    service.Receive(new BinaryReader(stream));
}
var exploit = CatalogueHelper.GetDefaultDevices(genie)!;
exploit[2] = radar;
exploit[3] = K("device_generic_heal_station");
exploit[4] = K("device_generic_ammo_resupply");
SetLoadout(new LobbyLoadout { HeroKey = genie, Devices = exploit, Perks = [elizaPerk], SkinKey = Key.None });
Check(saved.Count == 1 && !saved[0].Devices!.ContainsValue(radar) && saved[0].Perks!.Count == 0 &&
      saved[0].Devices!.ContainsValue(K("device_generic_heal_station")) && saved[0].Devices!.ContainsValue(K("device_generic_ammo_resupply")),
    "SetLoadout stores Genie's loadout without the radar or the Doc perk, keeping heal and ammo stations");
SetLoadout(new LobbyLoadout { HeroKey = K("device_generic_radar"), Devices = exploit, Perks = [], SkinKey = Key.None });
Check(saved.Count == 1, "SetLoadout for a key that is not a hero is ignored");
SetLoadout(new LobbyLoadout { HeroKey = eliza, Devices = CatalogueHelper.GetDefaultDevices(eliza), Perks = [elizaPerk], SkinKey = Key.None });
Check(saved.Count == 2 && saved[1].Perks!.SequenceEqual([elizaPerk]), "Doc Eliza saves her own perk unchanged");

Console.WriteLine($"LOADOUT_RULES checks={checks} failures={failures}");
return failures == 0 ? 0 : 1;

public class RecordingDatabase : DispatchProxy
{
    public List<LobbyLoadout> Saved = [];
    private uint? _player;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        if (method.Name == "UpdateLoadout") Saved.Add((LobbyLoadout)args![2]!);
        if (method.Name == "set_AssociatedPlayerId") _player = (uint?)args![0];
        if (method.Name == "get_AssociatedPlayerId") return _player;
        var type = method.ReturnType;
        return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    }
}
