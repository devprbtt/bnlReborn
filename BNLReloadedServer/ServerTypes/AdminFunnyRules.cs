using System.Numerics;
using BNLReloadedServer.BaseTypes;
namespace BNLReloadedServer.ServerTypes;

public static class AdminFunnyRules
{
    public const double LeaseSeconds = 1.5;
    public const float ChargeSeconds = 2f;
    public static bool IsAdmin(PlayerRole? role) => role is PlayerRole.Admin or PlayerRole.Core;
    public static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    public static bool ValidAim(Vector3 direction, float distance, float mapDiagonal) =>
        Finite(direction) && float.IsFinite(distance) && distance >= 3 && distance <= mapDiagonal &&
        direction.LengthSquared() is >= 0.9f and <= 1.1f;
    public static float Charge(double seconds) => Math.Clamp((float)seconds / ChargeSeconds, 0, 1);
    public static Vector3 Launch(Vector3 direction, double seconds)
    {
        var charge = Charge(seconds);
        return Vector3.Normalize(direction) * (15 + 105 * charge) + Vector3.UnitY * (8 + 22 * charge);
    }
}
