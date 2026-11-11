using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

/// <summary>
/// Which devices and perks a hero may carry, matching the client's own loadout screen: the hero's special
/// device in slot 1, otherwise only devices of its class (hero_class available_devices) or their skins, and
/// perks that are not tied to another hero or class, one per slot type. The client only filters what it
/// offers, so every loadout a client sends or the database returns goes through here.
/// </summary>
public static class LoadoutRules
{
    public static bool CanUseDevice(Key hero, Key device, uint? playerId = null)
    {
        if (DeviceSkinPresentation.IsRetired(device) || RetiredCountryCrates.IsRetired(device) || device.GetCard<CardDevice>() == null || Databases.Catalogue.GetCard<CardUnit>(hero)?.Data is not UnitDataPlayer heroData)
            return false;
        if (playerId is { } id && !PlayerInventory.Owns(id, device)) return false;
        var bases = (heroData.SpecialDevices ?? []).Concat(heroData.Class.GetCard<CardHeroClass>()?.AvailableDevices ?? []);
        return bases.Any(baseDevice => baseDevice == device || GroupOf(baseDevice)?.Contains(device) == true);
    }

    public static bool CanUsePerk(Key hero, Key perk, uint? playerId = null)
    {
        if (RetiredPerks.IsRetired(perk) || perk.GetCard<CardPerk>() is not { } card) return false;
        if (playerId is { } id && !PlayerInventory.Owns(id, perk)) return false;
        if (card.HeroDependency is { } owner ? owner != hero : card.SlotType == PerkSlotType.Hero) return false;
        return card.SlotType != PerkSlotType.Class ||
               ClassPerkCatalogue.ClassOf(card.Id) is { } perkClass && perkClass == ClassPerkCatalogue.HeroClass(hero);
    }

    /// <summary>Drops devices and perks the hero may not carry, in place. Returns what was removed.</summary>
    public static List<Key> Sanitize(Key hero, Dictionary<int, Key>? devices, List<Key>? perks, uint? playerId = null)
    {
        var removed = new List<Key>();
        if (perks != null)
        {
            var slotTypes = new HashSet<PerkSlotType>();
            for (var i = 0; i < perks.Count; i++)
            {
                if (CanUsePerk(hero, perks[i], playerId) && slotTypes.Add(perks[i].GetCard<CardPerk>()!.SlotType)) continue;
                removed.Add(perks[i]);
                perks.RemoveAt(i--);
            }
        }

        if (devices != null)
        {
            foreach (var (slot, key) in devices.ToList())
                if (DeviceSkinPresentation.IsRetired(key))
                {
                    devices[slot] = DeviceSkinPresentation.StockTesla;
                    removed.Add(key);
                }
                else if (RetiredCountryCrates.IsRetired(key))
                {
                    devices[slot] = RetiredCountryCrates.Stock;
                    removed.Add(key);
                }
            CatalogueHelper.RepairHeroSpecialDevice(hero, devices, out _, out _);
            var maxSlot = ClassPerkCatalogue.DeviceSlots(hero, perks);
            foreach (var (slot, device) in devices.ToList())
            {
                if (slot == 1 && CatalogueHelper.IsHeroSpecialDevice(hero, device)) continue;
                if (slot is > 1 && slot <= maxSlot && CanUseDevice(hero, device, playerId)) continue;
                removed.Add(device);
                devices.Remove(slot);
            }
        }
        return removed;
    }

    private static List<Key>? GroupOf(Key firstDevice) =>
        CatalogueHelper.GlobalLogic?.DeviceGroups?
            .Select(group => group.GetCard<CardDeviceGroup>()?.Devices)
            .FirstOrDefault(group => group is { Count: > 0 } && group[0] == firstDevice);
}
