using System;

namespace ArenaMode.Movement;

/// <summary>
/// Pure calculation shared with the offline regression harness. The setting is
/// a reduction of the penalty rather than a multiplier of the already-low speed:
/// 50% therefore moves 0.3 halfway back to the neutral limit 1.0, yielding 0.65.
/// </summary>
internal static class ArenaLegInjuryTuning
{
    internal const int MinimumReductionPercent = 0;
    internal const int MaximumReductionPercent = 100;

    internal static float ReducePenalty(float vanillaSpeedLimit, int reductionPercent)
    {
        if (float.IsNaN(vanillaSpeedLimit)
            || float.IsInfinity(vanillaSpeedLimit)
            || vanillaSpeedLimit < 0f
            || vanillaSpeedLimit > 1f)
        {
            return vanillaSpeedLimit;
        }

        int boundedPercent = Math.Max(
            MinimumReductionPercent,
            Math.Min(MaximumReductionPercent, reductionPercent));
        float reduction = boundedPercent / 100f;
        return vanillaSpeedLimit + (1f - vanillaSpeedLimit) * reduction;
    }
}
