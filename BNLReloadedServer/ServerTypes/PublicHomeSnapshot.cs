namespace BNLReloadedServer.ServerTypes;

// Deliberate public projection: no Steam IDs, sessions, IPs, role, or private profile fields.
public record PublicHomeSnapshot(int Schema, string Scope, string GeneratedAt, PublicHomePlayer[] Players)
{
    public PublicPlaySnapshot? Play { get; init; }
    public static string Classify(bool spectator, bool custom, bool game, bool queue) =>
        spectator ? "spectating" : custom ? "custom" : game ? "game" : queue ? "queue" : "menu";
}
public record PublicHomePlayer(uint Id, string Name, string Activity);

public record PublicPlaySnapshot(long ServerTime, PublicQueuePlayer[] Queue, PublicMatch[] Matches, PublicMap[] FriendlyMaps, PublicMap[] RankedMaps)
{
    public PublicMap[] Friendly5v5Maps { get; init; } = [];
    public int MapPreferencesVersion { get; init; } = MapPreferences.Version;
}
public record PublicQueuePlayer(uint Id, string Name, long JoinedAt, string Mode, bool InDeathmatch = false, int? Mmr = null) { public bool HasMmr => Mmr.HasValue; }
public record PublicMatch(string Id, string Name, string MapId, long StartedAt, PublicMatchPlayer[] Team1, PublicMatchPlayer[] Team2);
public record PublicMatchPlayer(uint Id, string Name, int Mmr);
public record PublicMap(string Id, string Name);
