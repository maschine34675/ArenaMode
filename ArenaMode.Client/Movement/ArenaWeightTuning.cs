using System;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Pure per-player carry-weight threshold scaling shared with the offline
/// regression harness. The inertia curve's third component controls its shape,
/// not a weight, so only the first two components are scaled.
/// </summary>
internal static class ArenaWeightTuning
{
    internal const float MinimumMultiplier = 1f;
    internal const float MaximumMultiplier = 4f;

    internal static float BoundMultiplier(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            return MinimumMultiplier;

        return Math.Max(MinimumMultiplier, Math.Min(MaximumMultiplier, multiplier));
    }

    internal static Vector2 ScaleLimits(Vector2 limits, float multiplier)
    {
        float bounded = BoundMultiplier(multiplier);
        return new Vector2(limits.x * bounded, limits.y * bounded);
    }

    internal static Vector3 ScaleInertiaLimits(Vector3 limits, float multiplier)
    {
        float bounded = BoundMultiplier(multiplier);
        return new Vector3(limits.x * bounded, limits.y * bounded, limits.z);
    }
}
