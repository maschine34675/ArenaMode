using System;

namespace ArenaMode.Bots;

internal enum TeamArenaConsumerDecision
{
    Fallback,
    AwaitingAcknowledgement,
    Active,
    DisableAndFallback
}

/// <summary>
/// Pure ownership rules for the optional BigBrain action. Constructor or layer
/// registration is not execution: a concrete generation must acknowledge its
/// first native movement update within a short bounded window.
/// </summary>
internal static class TeamArenaPursuitOwnerPolicy
{
    internal const float AcknowledgementTimeoutSeconds = 2f;
    internal const float ProgressMeters = 0.5f;
    internal const int StalledWindowsBeforeRetarget = 3;

    internal static TeamArenaConsumerDecision Evaluate(
        bool publicationAccepted,
        bool executionStarted,
        float ageSeconds)
    {
        if (!publicationAccepted)
            return TeamArenaConsumerDecision.Fallback;

        if (executionStarted)
            return TeamArenaConsumerDecision.Active;

        if (float.IsNaN(ageSeconds)
            || float.IsInfinity(ageSeconds)
            || ageSeconds < 0f
            || ageSeconds >= AcknowledgementTimeoutSeconds)
        {
            return TeamArenaConsumerDecision.DisableAndFallback;
        }

        return TeamArenaConsumerDecision.AwaitingAcknowledgement;
    }

    internal static bool CanAcceptConsumerReport(
        bool consumerMatches,
        bool sourceReady,
        int activeEpoch,
        int leaseEpoch,
        long leaseGeneration,
        int reportedEpoch,
        long reportedGeneration)
        => consumerMatches
           && sourceReady
           && activeEpoch >= 0
           && activeEpoch == leaseEpoch
           && leaseEpoch == reportedEpoch
           && leaseGeneration > 0
           && leaseGeneration == reportedGeneration;

    internal static bool HasRouteProgress(
        float displacementMeters,
        float previousRouteDistanceMeters,
        float currentRouteDistanceMeters)
        => IsFinite(displacementMeters)
           && IsFinite(previousRouteDistanceMeters)
           && IsFinite(currentRouteDistanceMeters)
           && displacementMeters >= ProgressMeters
           && previousRouteDistanceMeters - currentRouteDistanceMeters >= ProgressMeters;

    internal static int NextStalledWindowCount(
        int previousStalledWindows,
        float displacementMeters,
        float previousRouteDistanceMeters,
        float currentRouteDistanceMeters)
    {
        if (HasRouteProgress(
                displacementMeters,
                previousRouteDistanceMeters,
                currentRouteDistanceMeters))
            return 0;

        if (previousStalledWindows < 0)
            previousStalledWindows = 0;

        return previousStalledWindows == int.MaxValue
            ? int.MaxValue
            : previousStalledWindows + 1;
    }

    internal static bool ShouldRetarget(int stalledWindows)
        => stalledWindows >= StalledWindowsBeforeRetarget;

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool IsFreshHeartbeat(
        float now,
        float heartbeatAt,
        float maximumAgeSeconds)
        => !float.IsNaN(now)
           && !float.IsInfinity(now)
           && !float.IsNaN(heartbeatAt)
           && !float.IsInfinity(heartbeatAt)
           && !float.IsNaN(maximumAgeSeconds)
           && !float.IsInfinity(maximumAgeSeconds)
           && maximumAgeSeconds > 0f
           && now >= heartbeatAt
           && now - heartbeatAt <= maximumAgeSeconds;
}
