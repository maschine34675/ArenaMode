using System;

namespace ArenaMode.Combat;

/// <summary>
/// Pure timing transform used by the live medical patch and its offline
/// regression harness. The current phase keeps the same percentage complete,
/// while every part of the application timeline advances by one multiplier.
/// </summary>
internal static class ArenaMedicalTiming
{
    internal const float MinimumMultiplier = 1f;
    internal const float MaximumMultiplier = 4f;

    internal static float NormalizeMultiplier(float multiplier)
    {
        if (!IsFinite(multiplier) || multiplier <= MinimumMultiplier)
            return MinimumMultiplier;

        return Math.Min(multiplier, MaximumMultiplier);
    }

    internal static bool TryScale(
        float multiplier,
        bool waitingForStart,
        float delay,
        float work,
        float stateDuration,
        float stateElapsed,
        float wholeElapsed,
        float buildUp,
        out ArenaMedicalTimingState scaled)
    {
        scaled = new ArenaMedicalTimingState(
            delay,
            work,
            stateDuration,
            stateElapsed,
            wholeElapsed,
            buildUp);

        multiplier = NormalizeMultiplier(multiplier);
        if (multiplier <= MinimumMultiplier
            || !IsFiniteNonNegative(delay)
            || !IsFinitePositive(work)
            || !IsFiniteNonNegative(stateDuration)
            || !IsFiniteNonNegative(stateElapsed)
            || !IsFiniteNonNegative(wholeElapsed)
            || !IsFiniteNonNegative(buildUp))
        {
            return false;
        }

        float scaledDelay = delay / multiplier;
        float scaledWork = work / multiplier;
        float oldPhaseDuration = stateDuration > 0f
            ? stateDuration
            : waitingForStart ? delay : work;
        float progress = oldPhaseDuration > 0f
            ? Clamp01(stateElapsed / oldPhaseDuration)
            : 0f;
        float scaledPhaseDuration = waitingForStart ? scaledDelay : scaledWork;

        scaled = new ArenaMedicalTimingState(
            scaledDelay,
            scaledWork,
            scaledPhaseDuration,
            progress * scaledPhaseDuration,
            wholeElapsed / multiplier,
            Math.Min(buildUp / multiplier, scaledWork));
        return true;
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFiniteNonNegative(float value)
        => IsFinite(value) && value >= 0f;

    private static bool IsFinitePositive(float value)
        => IsFinite(value) && value > 0f;

    private static float Clamp01(float value)
        => Math.Max(0f, Math.Min(1f, value));
}

internal readonly struct ArenaMedicalTimingState
{
    internal ArenaMedicalTimingState(
        float delay,
        float work,
        float stateDuration,
        float stateElapsed,
        float wholeElapsed,
        float buildUp)
    {
        Delay = delay;
        Work = work;
        StateDuration = stateDuration;
        StateElapsed = stateElapsed;
        WholeElapsed = wholeElapsed;
        BuildUp = buildUp;
    }

    internal float Delay { get; }
    internal float Work { get; }
    internal float StateDuration { get; }
    internal float StateElapsed { get; }
    internal float WholeElapsed { get; }
    internal float BuildUp { get; }
}
