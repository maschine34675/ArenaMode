using System;

namespace ArenaMode.Pickups;

internal enum ArenaPickupPhase { Available, Pending, Cooldown, Retired }

/// <summary>One winner per appearance. A timed-out transfer never creates a second weapon.</summary>
internal sealed class ArenaPickupState
{
    internal ArenaPickupPhase Phase { get; private set; } = ArenaPickupPhase.Available;
    internal float ReadyAt { get; private set; }
    private float _pendingUntil;
    private int _reservation;

    internal int Reserve(float now)
    {
        if (!ArenaPickupLayouts.Finite(now) || Phase != ArenaPickupPhase.Available) return 0;
        Phase = ArenaPickupPhase.Pending;
        _pendingUntil = now + 10f;
        return ++_reservation;
    }

    internal bool Complete(int reservation, bool success, float now, float cooldown)
    {
        if (Phase != ArenaPickupPhase.Pending || reservation != _reservation || reservation == 0
            || !ArenaPickupLayouts.Finite(now) || now >= _pendingUntil
            || !ArenaPickupLayouts.Finite(cooldown) || cooldown < 10 || cooldown > 600) return false;
        Phase = success ? ArenaPickupPhase.Cooldown : ArenaPickupPhase.Available;
        ReadyAt = success ? now + cooldown : 0f;
        return true;
    }

    internal bool Tick(float now)
    {
        if (!ArenaPickupLayouts.Finite(now)) { Retire(); return false; }
        if (Phase == ArenaPickupPhase.Pending && now >= _pendingUntil) Retire();
        if (Phase != ArenaPickupPhase.Cooldown || now < ReadyAt) return false;
        Phase = ArenaPickupPhase.Available;
        return true;
    }

    internal void Retire() { Phase = ArenaPickupPhase.Retired; ++_reservation; }

    // Sampling a short movement segment catches fast movement without collecting
    // through a teleport, on the next floor, or along stale pre-respawn motion.
    internal static bool InReach(float fromX, float fromY, float fromZ, float toX, float toY, float toZ,
        float pointX, float pointY, float pointZ, bool continuous, out float fraction)
    {
        fraction = 1f;
        if (!ArenaPickupLayouts.Finite(fromX) || !ArenaPickupLayouts.Finite(fromY) || !ArenaPickupLayouts.Finite(fromZ)
            || !ArenaPickupLayouts.Finite(toX) || !ArenaPickupLayouts.Finite(toY) || !ArenaPickupLayouts.Finite(toZ)
            || !ArenaPickupLayouts.Finite(pointX) || !ArenaPickupLayouts.Finite(pointY) || !ArenaPickupLayouts.Finite(pointZ)) return false;
        double dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
        double planar = dx * dx + dz * dz;
        if (continuous && planar + dy * dy <= 25 && planar > .000001)
            fraction = (float)Math.Max(0, Math.Min(1, ((pointX - fromX) * dx + (pointZ - fromZ) * dz) / planar));
        double x = fromX + dx * fraction - pointX, y = fromY + dy * fraction - pointY, z = fromZ + dz * fraction - pointZ;
        return Math.Abs(y) <= .9 && x * x + z * z <= 1.0;
    }
}
