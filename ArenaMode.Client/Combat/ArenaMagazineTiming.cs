using System;

namespace ArenaMode.Combat;

/// <summary>
/// Pure process-constructor delay transform shared by the live patches and the
/// offline regression harness.
/// </summary>
internal static class ArenaMagazineTiming
{
    internal const float MinimumMultiplier = 1f;
    internal const float MaximumMultiplier = 4f;
    internal const float MinimumDelaySeconds = 0.025f;

    internal static float NormalizeMultiplier(float multiplier)
    {
        if (!IsFinite(multiplier) || multiplier <= MinimumMultiplier)
            return MinimumMultiplier;

        return Math.Min(multiplier, MaximumMultiplier);
    }

    internal static bool TryScale(float delaySeconds, float multiplier,
        out float scaledDelaySeconds)
    {
        scaledDelaySeconds = delaySeconds;
        multiplier = NormalizeMultiplier(multiplier);
        if (multiplier <= MinimumMultiplier
            || !IsFinite(delaySeconds)
            || delaySeconds <= MinimumDelaySeconds)
        {
            return false;
        }

        scaledDelaySeconds = Math.Max(
            MinimumDelaySeconds,
            delaySeconds / multiplier);
        return scaledDelaySeconds < delaySeconds;
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
