using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

// Server-only CDB settings on global_logic.action_scores; omitted fields keep defaults.
public sealed class ActionScoreRules
{
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
        if (FirstKill <= Kill || DoubleKill <= FirstKill || MultiKill <= DoubleKill || UltraKill <= MultiKill || Fantastic <= UltraKill || Unbelievable <= Fantastic)
            throw new InvalidDataException("Kill accolade scores must increase above the regular kill value");
        if (BuildCostPerPoint == 0 || BuildMax < BuildMin || TonyRepairPerPoint == 0 || DocAbilityHealPerPoint == 0 || HealPerPoint == 0 || DeviceDamagePerPoint == 0 || DeviceCostPerPoint == 0 || ContinuousChunk == 0 || DeviceMax < DeviceMin)
            throw new InvalidDataException("Invalid action score denominator or device range");
    }
}

