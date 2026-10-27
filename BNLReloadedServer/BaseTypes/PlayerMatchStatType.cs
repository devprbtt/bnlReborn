namespace BNLReloadedServer.BaseTypes;

public enum PlayerMatchStatType
{
    Earned = 1,
    Built = 2,
    Destroyed = 3,
    Objective = 4,
    BlockAssist = 5,
    Kill = 6,
    Death = 7,
    Assist = 8,
    ZonesCaptured = 9,
    ZoneTimeSeconds = 10,
    Destruction = 11,
    ActionScore = 12,
    // Match Gold breakdown (MatchGold). Released clients ignore stat types they do not know.
    GoldEarned = 13,
    GoldParticipation = 14,
    GoldVictory = 15,
    GoldMostHealing = 16,
    GoldMostDamage = 17,
    GoldMostDevicesDestroyed = 18,
    GoldMostKills = 19,
    GoldMostAssists = 20,
    GoldMostObjectiveDamage = 21,
    GoldMostDropsCollected = 22
}
