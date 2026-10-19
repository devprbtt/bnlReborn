using BNLReloadedServer.Database;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    /// <summary>
    /// How to reach one player's zone connection with their Skill II lead list, or null when that connection
    /// cannot receive it (older client, not loaded). Set by GameInstance; the object identifies the connection,
    /// so a reconnect is sent the current list again.
    /// </summary>
    public Func<uint, (object Connection, Action<IReadOnlyCollection<uint>> Send)?>? AssistLeadSender { get; set; }

    private readonly Dictionary<uint, (object Connection, HashSet<uint> Victims)> _assistLeadSent = new();

    /// <summary>
    /// Skill II (Accomplice) makes an assist count as a kill for on-kill perks only while the assister dealt the
    /// victim the most damage. Tell each Accomplice player which enemy heroes they currently lead on, by unit id,
    /// with exactly the rule ApplyClassKillPerks uses (TopDamagers: the assist window, ties count). Sent only
    /// when a player's list changes; runs on the buff-check cadence because the window also expires.
    /// </summary>
    private void RefreshAssistLeads()
    {
        if (AssistLeadSender == null) return;
        var leads = new Dictionary<uint, HashSet<uint>>();
        foreach (var unit in _playerUnits.Values)
        {
            if (unit.PlayerId is { } playerId && unit.ActiveClassPerk == ClassPerkCatalogue.Skill2Id)
                leads[playerId] = [];
        }

        if (leads.Count > 0)
        {
            foreach (var victim in _playerUnits.Values)
            {
                if (victim.IsDead || victim.PlayerId == null || victim.RecentDamageTaken.Count == 0) continue;
                foreach (var top in TopDamagers(victim))
                    if (leads.TryGetValue(top, out var victims)) victims.Add(victim.Id);
            }
        }

        // Players who lost Skill II (hero change) or left get one empty list, then nothing.
        foreach (var playerId in _assistLeadSent.Keys.Where(id => !leads.ContainsKey(id)).ToList())
        {
            var connection = AssistLeadSender(playerId);
            if (connection != null && _assistLeadSent[playerId].Victims.Count > 0) connection.Value.Send([]);
            _assistLeadSent.Remove(playerId);
        }

        foreach (var (playerId, victims) in leads)
        {
            if (AssistLeadSender(playerId) is not { } connection) continue;
            if (_assistLeadSent.TryGetValue(playerId, out var sent) && ReferenceEquals(sent.Connection, connection.Connection) &&
                sent.Victims.SetEquals(victims))
                continue;
            connection.Send(victims.ToList());
            _assistLeadSent[playerId] = (connection.Connection, victims);
        }
    }
}
