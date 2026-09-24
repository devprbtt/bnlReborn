using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

/// <summary>
/// Map-only placement for teleport_to and unit spawns. Unit-octree lookups are passed in so the
/// search can run without a live zone.
/// </summary>
public static class TeleportPlacement
{
    public const float ImpactImprecision = 0.05f;
    public const float ImpactImprecisionInv = 1 - ImpactImprecision;

    // A teleported player is placed as a standing body: the client stands up after landing, and a
    // crouched check converted back with the standing offset landed the body half a block low, where
    // standing put the head inside overhead force fields.
    private const float PlayerHalfHeight = 0.95f;

    // Covers the client character capsule (radius 0.32). The generic 0.25 player footprint let the
    // capsule overlap a force-field wall, and enemy fields are one-sided collider shells on the client.
    private const float PlayerHalfWidth = 0.33f;

    /// <summary>True when no sized unit other than the ignored one occupies the point.</summary>
    public delegate bool PointUnitsClear(Vector3 pos, uint? ignoredUnitId);

    /// <summary>True when no sized unit collides with the given unit placed at pos.</summary>
    public delegate bool UnitUnitsClear(Vector3 pos);

    /// <summary>
    /// Walks from the impact point back toward the caster until the caster fits, returning the
    /// maneuver position to send, or null when no step fits.
    /// </summary>
    public static Vector3? FindTeleportTo(MapBinary map, Unit unit, Vector3 impactPoint, Vector3 midpoint,
        BlockShift? shift, float planePosition, UnitUnitsClear unitsClear, PointUnitsClear pointClear)
    {
        var isPlayer = unit.PlayerId != null;
        if (isPlayer) midpoint = unit.Transform.Position with { Y = unit.Transform.Position.Y + PlayerHalfHeight };
        var dist = Vector3.Distance(impactPoint, midpoint);
        if (dist == 0) return null;

        for (var i = 0; i < dist; i++)
        {
            var telePos = Vector3.Lerp(impactPoint, midpoint, i / dist);
            var doYCheck = true;
            if (telePos.Y < planePosition)
            {
                telePos.Y = planePosition + UnitSizeHelper.ImprecisionVector.Y;
                doYCheck = false;
            }

            if (CanFit(telePos))
            {
                return ToManeuverPosition(telePos);
            }

            var adjustedPosition = i == 0 ? ShiftOffBlock(telePos, shift, doYCheck) : telePos;

            if (i == 0 && CanFit(adjustedPosition))
            {
                return ToManeuverPosition(adjustedPosition);
            }

            var newPos = AdjustToFit(map, adjustedPosition, unit, null, pointClear);
            if (newPos is null)
            {
                continue;
            }

            if (CanFit(newPos.Value))
            {
                return ToManeuverPosition(newPos.Value);
            }
        }

        return null;

        bool CanFit(Vector3 pos) => (isPlayer ? PlayerFits(map, pos) : map.GetCanFit(unit, pos)) && unitsClear(pos);

        Vector3 ToManeuverPosition(Vector3 pos) => isPlayer ? pos with { Y = pos.Y - PlayerHalfHeight } : pos;
    }

    /// <summary>Whether a standing player body centred on midpoint overlaps only passable blocks.</summary>
    public static bool PlayerFits(MapBinary map, Vector3 midpoint)
    {
        var extent = new Vector3(PlayerHalfWidth, PlayerHalfHeight, PlayerHalfWidth) - UnitSizeHelper.HalfImprecisionVector;
        var min = (Vector3s)(midpoint - extent);
        var max = (Vector3s)(midpoint + extent);
        for (var x = min.x; x <= max.x; x++)
        for (var y = min.y; y <= max.y; y++)
        for (var z = min.z; z <= max.z; z++)
        {
            var cell = new Vector3s(x, y, z);
            if (map.ContainsBlock(cell) && map[cell].Card.Passable != BlockPassableType.Any) return false;
        }

        return true;
    }

    public static Vector3 ShiftOffBlock(Vector3 pos, BlockShift? shift, bool doYCheck) =>
        shift switch
        {
            BlockShift.Left when pos.X - float.Truncate(pos.X) < ImpactImprecision
                => pos with
                {
                    X = pos.X - ImpactImprecision
                },
            BlockShift.Right when pos.X - float.Truncate(pos.X) > ImpactImprecisionInv
                => pos with
                {
                    X = pos.X + ImpactImprecision
                },
            BlockShift.Bottom when pos.Y - float.Truncate(pos.Y) < ImpactImprecision && doYCheck
                => pos with
                {
                    Y = pos.Y - ImpactImprecision
                },
            BlockShift.Top when pos.Y - float.Truncate(pos.Y) > ImpactImprecisionInv && doYCheck
                => pos with
                {
                    Y = pos.Y + ImpactImprecision
                },
            BlockShift.Back when pos.Z - float.Truncate(pos.Z) < ImpactImprecision
                => pos with
                {
                    Z = pos.Z - ImpactImprecision
                },
            BlockShift.Front when pos.Z - float.Truncate(pos.Z) > ImpactImprecisionInv
                => pos with
                {
                    Z = pos.Z + ImpactImprecision
                },
            _ => pos
        };

    public static Vector3? AdjustToFit(MapBinary map, Vector3 pos, Unit? placementUnit, Vector3s? sizeOverride,
        PointUnitsClear pointClear)
    {
        var uSize = sizeOverride ?? placementUnit?.UnitCard?.Size ?? Vector3s.Zero;
        var isPlayer = placementUnit?.PlayerId != null;
        // Only teleport_to places players here, so they use the same standing body as FindTeleportTo.
        var vecX = isPlayer ? PlayerHalfWidth : uSize.x * 0.5f;
        var vecY = isPlayer ? PlayerHalfHeight : uSize.y * 0.5f;
        var vecZ = isPlayer ? vecX : uSize.z * 0.5f;
        var ignoredUnitId = placementUnit?.Id;


        var fitXPos = CanFitPoint(pos with
        {
            X = pos.X + vecX - UnitSizeHelper.HalfImprecisionVector.X
        });

        var fitXNeg = CanFitPoint(pos with
        {
            X = pos.X - vecX + UnitSizeHelper.HalfImprecisionVector.X
        });

        if (!fitXPos && !fitXNeg)
        {
            return null;
        }
        if (!fitXPos)
        {
            pos.X = float.Floor(pos.X + vecX) - vecX;
        }
        else if (!fitXNeg)
        {
            pos.X = float.Ceiling(pos.X - vecX) + vecX;
        }

        var fitYPos = CanFitPoint(pos with
        {
            Y = pos.Y + vecY - UnitSizeHelper.HalfImprecisionVector.Y
        });

        var fitYNeg = CanFitPoint(pos with
        {
            Y = pos.Y - vecY + UnitSizeHelper.HalfImprecisionVector.Y
        });

        if (!fitYPos && !fitYNeg)
        {
            return null;
        }
        if (!fitYPos)
        {
            pos.Y = float.Floor(pos.Y + vecY) - vecY;
        }
        else if (!fitYNeg)
        {
            pos.Y = float.Ceiling(pos.Y - vecY) + vecY;
        }

        var fitZPos = CanFitPoint(pos with
        {
            Z = pos.Z + vecZ - UnitSizeHelper.HalfImprecisionVector.Z
        });

        var fitZNeg = CanFitPoint(pos with
        {
            Z = pos.Z - vecZ + UnitSizeHelper.HalfImprecisionVector.Z
        });

        if (!fitZPos && !fitZNeg)
        {
            return null;
        }
        if (!fitZPos)
        {
            pos.Z = float.Floor(pos.Z + vecZ) - vecZ;
        }
        else if (!fitZNeg)
        {
            pos.Z = float.Ceiling(pos.Z - vecZ) + vecZ;
        }

        return pos;

        bool CanFitPoint(Vector3 point) =>
            (!map.ContainsBlock((Vector3s)point) ||
             map[(Vector3s)point].Card.Passable == BlockPassableType.Any) &&
            pointClear(point, ignoredUnitId);
    }
}
