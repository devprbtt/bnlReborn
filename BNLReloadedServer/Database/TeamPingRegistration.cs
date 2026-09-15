using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class TeamPingRegistration
{
    public const string EnemySpottedNotificationId = "note_command_enemy_spotted";
    public static readonly Key EnemySpottedNotification = new(EnemySpottedNotificationId);

    public static void Register(List<Card> cards)
    {
        var existing=cards.OfType<CardNotification>().FirstOrDefault(card => card.Id == EnemySpottedNotificationId);
        if (existing != null)
        {
            existing.PlayerNotifySound = "command_needs_help";
            return;
        }
        cards.Add(new CardNotification
        {
            Id = EnemySpottedNotificationId,
            Key = EnemySpottedNotification,
            SmallNotifyText = new LocalizedString { Text = "<PLAYER>: Enemy spotted", Data = [] },
            PlayerNotifySound = "command_needs_help",
            ShowOnKillScroll = false
        });
    }
}
