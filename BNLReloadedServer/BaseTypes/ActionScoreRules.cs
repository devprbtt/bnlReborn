using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

// Server-only CDB settings on global_logic.action_scores; omitted fields keep defaults.
public sealed class ActionScoreRules
{
    public int AmmoSupply { get; set; } = 5;
    // Legacy intervals remain parseable but no longer gate device actions.
    public float AmmoCooldown { get; set; } = 15;
    public int RadarDevice { get; set; } = 5;
    public int SpawnUse { get; set; } = 10;
    public float SpawnCooldown { get; set; } = 30;
    public int PortalUse { get; set; } = 10;
    public float PortalCooldown { get; set; } = 60;
    public int BombAffected { get; set; } = 10;
    public int SargeC4Hit { get; set; } = 10;
    public int SargeC4Destroy { get; set; } = 10;
    public int MortarHit { get; set; } = 10;
    public int TikiHit { get; set; } = 5;
    public int BearTrapAffected { get; set; } = 10;
    public int CaltropsAffected { get; set; } = 5;
    public float DeviceHitCooldown { get; set; } = 1;
    public float TurretDamageChunk { get; set; } = 100;
    public int EnemySideSpawnUse { get; set; } = 20;
    public int FakeBlockTriggered { get; set; } = 10;
    public float HeroDeviceDamageChunk { get; set; } = 50;
    public int HeroDeviceDamagePoints { get; set; } = 5;
    public int GravityWellHit { get; set; } = 5;
    public int KreepyBoxHit { get; set; } = 5;
    public int TurretDamagePoints { get; set; } = 5;
    public int MineAffected { get; set; } = 10;
    public int FireTrapAffected { get; set; } = 5;
    public int GlueAffected { get; set; } = 5;
    public int BushAffected { get; set; } = 5;
    public float TrapEffectCooldown { get; set; } = 15;
    public int TonyBuildAssist { get; set; } = 5;
    public int TrondsonDemolitionAssist { get; set; } = 10;
    public int GenieLampPickup { get; set; } = 10;
    public int GenieJinx { get; set; } = 10;
    public int VanderCage { get; set; } = 10;
    public int SweetScienceEmp { get; set; } = 10;
    public int YetiAvalanche { get; set; } = 10;
    public int SargeGrenadeHit { get; set; } = 10;
    public int RolyGoo { get; set; } = 10;
    public int CogMortarDestroy { get; set; } = 10;
    public int AstarellaBoost { get; set; } = 10;
    public int KiraReloadBoost { get; set; } = 5;
    public int KreepyFear { get; set; } = 10;
    public float TonyRepairPerPoint { get; set; } = 10;
    public float DocAbilityHealPerPoint { get; set; } = 10;
    public float HeroEffectCooldown { get; set; } = 15;
    public int Kill { get; set; } = 50;
    public int FirstKill { get; set; } = 60;
    public int DoubleKill { get; set; } = 75;
    public int MultiKill { get; set; } = 100;
    public int UltraKill { get; set; } = 125;
    public int Fantastic { get; set; } = 150;
    public int Unbelievable { get; set; } = 175;
    public int Assist { get; set; } = 25;
    // Replaces the assist for the assister who dealt the victim the most damage (ties share it).
    public int KeyAssist { get; set; } = 35;
    public int Headshot { get; set; } = 10;
    public int Supply { get; set; } = 25;
    public int Blockbuster { get; set; } = 100;
    public int Cube { get; set; } = 250;
    public int CubeAssist { get; set; } = 75;
    public float BuildCostPerPoint { get; set; } = 20;
    public int BuildMin { get; set; } = 1;
    public int BuildMax { get; set; } = 50;
    public int Radar { get; set; } = 5;
    public int AbilitySpot { get; set; } = 10;
    public float HealPerPoint { get; set; } = 10;
    public float DeviceDamagePerPoint { get; set; } = 20;
    public int ContinuousChunk { get; set; } = 5;
    public float DeviceCostPerPoint { get; set; } = 20;
    public int DeviceMin { get; set; } = 5;
    public int DeviceMax { get; set; } = 50;
    public float ObjectiveRadius { get; set; } = 12;
    public float ObjectiveBonus { get; set; } = .25f;
    // Kills near an objective get at least this objective bonus (the percentage bonus when that is larger).
    public int KillObjectiveBonus { get; set; } = 20;
    public float SpotCooldown { get; set; } = 15;
    public float BuildCooldown { get; set; } = 30;
    public float CubeAssistWindow { get; set; } = 30;
    public static ActionScoreRules Load()
    {
        var configured = Databases.Catalogue.GetCard<CardGlobalLogic>("global_logic")?.ActionScores;
        var rules = configured == null ? new ActionScoreRules() : (ActionScoreRules)configured.MemberwiseClone();
        rules.Validate(); return rules;
    }
    public void Validate()
    {
        foreach (var p in GetType().GetProperties())
        {
            var value = Convert.ToDouble(p.GetValue(this));
            if (!double.IsFinite(value) || value < 0 || value > 100000) throw new InvalidDataException("Invalid action score setting: " + p.Name);
        }
        if (HeroDeviceDamageChunk <= 0 || TurretDamageChunk <= 0 || PortalCooldown < 60 || DeviceHitCooldown <= 0 || SpotCooldown <= 0)
            throw new InvalidDataException("Invalid device action threshold or repeat interval");
        if (FirstKill <= Kill || DoubleKill <= FirstKill || MultiKill <= DoubleKill || UltraKill <= MultiKill || Fantastic <= UltraKill || Unbelievable <= Fantastic)
            throw new InvalidDataException("Kill accolade scores must increase above the regular kill value");
        if (KeyAssist < Assist) throw new InvalidDataException("Key assist must not be worth less than an assist");
        if (BuildCostPerPoint == 0 || BuildMax < BuildMin || TonyRepairPerPoint == 0 || DocAbilityHealPerPoint == 0 || HealPerPoint == 0 || DeviceDamagePerPoint == 0 || DeviceCostPerPoint == 0 || ContinuousChunk == 0 || DeviceMax < DeviceMin)
            throw new InvalidDataException("Invalid action score denominator or device range");
    }
}

