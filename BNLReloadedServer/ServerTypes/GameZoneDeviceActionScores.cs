using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    public static string? DeviceScoreReason(string id)
    {
        bool Family(string family) => Unit.ScoreCardFamily(id, family);
        if (Family("unit_device_astro_orbital_beacon") || Family("unit_dummy_astro_orbital_skybeam")) return "Beacon damage";
        if (Family("unit_device_eliza_acid_trap") || Family("unit_dummy_eliza_acid_cloud_acid_trap")) return "Miasma damage";
        if (Family("unit_dummy_djinn_acid_cloud_lamp")) return "Lamp gas damage";
        if (Family("unit_device_trondson_torpedo")) return "Torpedo damage";
        if (Family("unit_device_boxer_gravity_well")) return "Gravity well hit";
        if (Family("unit_device_kreepy_box")) return "Kreepy box hit";
        if (Family("unit_device_sarge_bomb")) return "C4 hit";
        if (Family("unit_device_cogwheel_mortar")) return "Mortar hit";
        if (Family("unit_device_hunter_tiki")) return "Tiki hit";
        if (Family("unit_device_roly_toxic_waste") || Family("block_toxic_waste_rolydevice")) return "Goo affected";
        if (Family("unit_device_generic_landmine")) return "Mine affected";
        if (Family("unit_device_generic_fire_trap") || id == "block_fire_from_fire_trap") return "Fire trap affected";
        if (Family("block_thorn_bush")) return "Bush affected";
        if (Family("block_glue")) return "Glue affected";
        if (Family("block_bear_trap")) return "Bear trap affected";
        if (Family("block_caltrops")) return "Caltrops affected";
        if (Family("unit_device_generic_bomb") || Family("unit_device_generic_large_bomb") ||
            Family("unit_device_generic_nuke_bomb") || Family("unit_device_ninja_bomb")) return "Bomb affected";
        if (Family("unit_device_engineer_turret") || Family("unit_device_generic_cannon") || Family("unit_device_magnus_tesla_coil")) return "Turret damage";
        return null;
    }

    private void ScoreDeviceUse(Unit device, Unit ally, string reason)
    {
        if (!CanScore || ally.PlayerId is not { } recipient || !ally.IsActive || ally.IsDead ||
            (device.PlayerId ?? device.OwnerPlayerId) is not { } owner || owner == recipient || device.Team != ally.Team ||
            GetPlayerFromPlayerId(owner) is not { } builder || builder.Team != ally.Team || builder.Team == TeamType.Neutral) return;
        // Only portals retain a per-owner/ally interval. Every completed spawn scores.
        if (reason == "Ally spawned" && MapBinary.OnEnemySide(device.GetMidpoint(), builder.Team))
            reason = "Enemy-side ally spawned";
        AwardAction(builder, reason, 1, device.GetMidpoint(), recipient);
    }
}
