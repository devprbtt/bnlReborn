using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

internal sealed class DebugDummyMotion
{
    private const float Gravity = 10f;
    private const float ContactSkin = 0.002f;
    private const float MaxMoveStep = 0.2f;
    private Vector3 _velocity;
    private Vector3s? _activePad;
    private float? _fallPeak;
    private ManeuverSlip? _slip;
    private float _slipStartYaw;
    private float _slipElapsed;

    public Vector3 Velocity => _velocity;
    public ManeuverPull? Pull { get; set; }
    public float LandingHeight { get; private set; }
    public float? SlipYaw { get; private set; }

    public void ApplySlip(MapBinary map, Unit unit, ManeuverSlip slip)
    {
        if (slip.Time <= 0 || _velocity.Y > 0 || !IsGrounded(map, unit.Team, unit.Transform.Position)) return;
        _slip = slip;
        _slipStartYaw = ZoneTransformHelper.UnpackFromShort(unit.Transform.Rotation.y);
        _slipElapsed = 0;
    }

    public void ApplyKnockback(MapBinary map, Unit unit, ManeuverKnockback knockback)
    {
        var position = unit.Transform.Position;
        var offset = position + Vector3.UnitY - knockback.Origin;
        var direction = offset == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(offset);
        if (direction.Y < 0.707f)
        {
            var horizontal = direction with { Y = 0 };
            direction = horizontal == Vector3.Zero
                ? Vector3.UnitY
                : Vector3.Normalize(horizontal) * 0.707f + Vector3.UnitY * 0.707f;
        }
        var grounded = _velocity.Y <= 0 && IsGrounded(map, unit.Team, position);
        _velocity += direction * (grounded ? knockback.Force : knockback.MidairForce);
    }

    public Vector3 Step(MapBinary map, TeamType team, Vector3 position, float dt, float airResistance,
        float friction, Vector3? pullOrigin, out bool grounded, out Vector3? bouncedPad)
    {
        bouncedPad = null;
        LandingHeight = 0;
        SlipYaw = null;
        grounded = _velocity.Y <= 0 && IsGrounded(map, team, position);
        TryBounce(map, position, ref bouncedPad);
        _fallPeak = Math.Max(_fallPeak ?? position.Y, position.Y);
        _velocity.Y = grounded && _velocity.Y <= 0 ? 0 : _velocity.Y - Gravity * dt;

        var horizontal = new Vector2(_velocity.X, _velocity.Z);
        var speed = horizontal.Length();
        if (_slip is { } slip && grounded && !bouncedPad.HasValue)
        {
            var yawChange = MathF.IEEERemainder(slip.DirectionAngle, 360);
            var yaw = float.DegreesToRadians(_slipStartYaw + yawChange);
            var activeTime = Math.Min(dt, slip.Time - _slipElapsed);
            var slipSpeed = slip.Distance / slip.Time * activeTime / dt;
            _velocity.X = MathF.Sin(yaw) * slipSpeed;
            _velocity.Z = MathF.Cos(yaw) * slipSpeed;
            _slipElapsed += dt;
            var turn = slip.RotationTime <= 0 ? 1 : Math.Min(1, _slipElapsed / slip.RotationTime);
            SlipYaw = MathF.IEEERemainder(_slipStartYaw + yawChange * turn, 360);
        }
        else if (speed > 0)
        {
            horizontal *= Math.Max(0, 1 - (grounded && !bouncedPad.HasValue ? friction : airResistance) * dt / speed);
            _velocity.X = horizontal.X;
            _velocity.Z = horizontal.Y;
        }

        var pull = Vector3.Zero;
        if (pullOrigin.HasValue && Pull?.Force is > 0 and var pullForce)
        {
            var offset = pullOrigin.Value - position;
            var distance = offset.Length();
            if (distance > 0.6f) pull = offset * Math.Min(1 / dt, pullForce / distance);
        }

        // Sweep the standing hull a fraction of a block at a time. Resolve axes
        // independently so touching a wall does not stop a fall or a slide.
        var steps = Math.Max(1, (int)Math.Ceiling((_velocity + pull).Length() * dt / MaxMoveStep));
        var stepTime = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var delta = (_velocity + pull) * stepTime;
            if (!SlideAxis(map, team, ref position, Vector3.UnitY * delta.Y)) _velocity.Y = 0;
            if (!SlideAxis(map, team, ref position, Vector3.UnitX * delta.X)) _velocity.X = 0;
            if (!SlideAxis(map, team, ref position, Vector3.UnitZ * delta.Z)) _velocity.Z = 0;
            TryBounce(map, position, ref bouncedPad);
        }

        grounded = _velocity.Y <= 0 && IsGrounded(map, team, position);
        if (_slip is { } activeSlip && (!grounded || bouncedPad.HasValue || _slipElapsed >= activeSlip.Time))
        {
            _slip = null;
            if (!bouncedPad.HasValue)
            {
                _velocity.X *= 0.3f;
                _velocity.Z *= 0.3f;
            }
        }
        if (grounded)
        {
            LandingHeight = Math.Max(0, _fallPeak.Value - position.Y);
            _fallPeak = position.Y;
        }
        return position;
    }

    private static bool IsGrounded(MapBinary map, TeamType team, Vector3 position) =>
        !Fits(map, team, position - Vector3.UnitY * (ContactSkin * 4));

    private static bool SlideAxis(MapBinary map, TeamType team, ref Vector3 position, Vector3 delta)
    {
        if (delta == Vector3.Zero) return true;
        if (Fits(map, team, position + delta))
        {
            position += delta;
            return true;
        }

        var low = 0f;
        var high = 1f;
        for (var i = 0; i < 8; i++)
        {
            var mid = (low + high) * 0.5f;
            if (Fits(map, team, position + delta * mid)) low = mid;
            else high = mid;
        }
        position += delta * low;
        return false;
    }

    internal static bool Fits(MapBinary map, TeamType team, Vector3 feet)
    {
        var width = TeleportPlacement.PlayerHalfWidth - ContactSkin;
        var min = (Vector3s)(feet + new Vector3(-width, ContactSkin, -width));
        var max = (Vector3s)(feet + new Vector3(width, TeleportPlacement.PlayerHalfHeight * 2 - ContactSkin, width));
        for (var x = min.x; x <= max.x; x++)
        for (var y = min.y; y <= max.y; y++)
        for (var z = min.z; z <= max.z; z++)
        {
            var cell = new Vector3s(x, y, z);
            if (!map.ContainsBlock(cell)) continue;
            var block = map[cell];
            if (block.Card.Passable == BlockPassableType.Any ||
                block.Card.Passable == BlockPassableType.Ally && block.Team == team ||
                block.Card.Passable == BlockPassableType.Enemy && block.Team != team) continue;
            return false;
        }
        return true;
    }

    private void TryBounce(MapBinary map, Vector3 position, ref Vector3? bouncedPad)
    {
        for (var y = -ContactSkin * 4; y <= 1; y += 1)
        {
            var cell = (Vector3s)(position + Vector3.UnitY * y);
            if (!map.ContainsBlock(cell)) continue;
            var block = map[cell];
            if (block.Card.Special is not BlockSpecialBounce pad) continue;
            if (_activePad != cell)
            {
                _activePad = cell;
                var normal = block.Card.Visual?.FaceAlign == true ? ((BlockFace)(block.VData & 7)) switch
                {
                    BlockFace.Top => -Vector3.UnitY,
                    BlockFace.Right => -Vector3.UnitX,
                    BlockFace.Left => Vector3.UnitX,
                    BlockFace.Forward => -Vector3.UnitZ,
                    BlockFace.Back => Vector3.UnitZ,
                    _ => Vector3.UnitY
                } : Vector3.UnitY;
                var force = normal * pad.BounceForce + Vector3.UnitY * 0.5f;
                var axis = Vector3.Normalize(force);
                _velocity += force - axis * Vector3.Dot(_velocity, axis);
                _fallPeak = position.Y;
                bouncedPad = cell.ToVector3() + new Vector3(0.5f);
            }
            return;
        }
        _activePad = null;
    }
}
