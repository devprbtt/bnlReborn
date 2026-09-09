namespace BNLReloadedServer.ServerTypes;

// Deliberate public projection: no Steam IDs, sessions, IPs, role, or private profile fields.
public record PublicHomeSnapshot(int Schema, string Scope, string GeneratedAt, PublicHomePlayer[] Players)
{
    public static string Classify(bool spectator, bool custom, bool game, bool queue) =>
        spectator ? "spectating" : custom ? "custom" : game ? "game" : queue ? "queue" : "menu";
}
public record PublicHomePlayer(uint Id, string Name, string Activity);
