using System;

namespace ArenaMode.Areas;

/// <summary>Visual-only proximity policy; never controls colliders, carving or containment.</summary>
internal static class ArenaAreaGridVisibility
{
    internal const float FullDistance = 6f;
    internal const float HiddenDistance = 20f;
    internal const float RefreshSeconds = .1f;

    internal static byte SideAlpha(ArenaAreaEnclosurePlan.GridSide side, float x, float z)
    {
        if (!Finite(x) || !Finite(z)) return 255; // Missing observation must not hide the warning.
        double dx = side.End.X - (double)side.Start.X, dz = side.End.Z - (double)side.Start.Z;
        double lengthSquared = dx * dx + dz * dz;
        if (!(lengthSquared > 0)) return 255;
        double t = ((x - (double)side.Start.X) * dx + (z - (double)side.Start.Z) * dz) / lengthSquared;
        t = Math.Max(0, Math.Min(1, t));
        double ex = x - (side.Start.X + dx * t), ez = z - (side.Start.Z + dz * t);
        return DistanceAlpha(Math.Sqrt(ex * ex + ez * ez));
    }

    internal static byte CapAlpha(float height, float observerY)
        => DistanceAlpha(Math.Abs(height - (double)observerY));

    private static byte DistanceAlpha(double distance)
    {
        if (double.IsNaN(distance) || double.IsInfinity(distance)) return 255;
        if (distance <= FullDistance) return 255;
        if (distance >= HiddenDistance) return 0;
        return (byte)Math.Round(255 * (HiddenDistance - distance) / (HiddenDistance - FullDistance));
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
