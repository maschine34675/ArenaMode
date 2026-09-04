namespace ArenaMode.Modes;

internal enum SoloTacticalSpawnDecision
{
    Waiting,
    Active,
    Retry,
}

/// <summary>
/// Pure timing policy for the detached Solo Tactical opponent request.
/// BotCreationData.Create includes backend profile, bundle and prefab-pool work;
/// the first request in a raid therefore commonly outlives a short frame timer.
/// </summary>
internal static class SoloTacticalSpawnPolicy
{
    /// <summary>
    /// Matches protected-team assembly. Measured cold requests took 12.7-13.2s
    /// while the old 12s window discarded them immediately before completion.
    /// </summary>
    public const float AssemblySeconds = 45f;

    /// <summary>A synchronous preflight failure has no detached work to await.</summary>
    public const float PlanFailureRetrySeconds = 3f;

    public static float Deadline(float now, bool requestDispatched)
        => now + (requestDispatched ? AssemblySeconds : PlanFailureRetrySeconds);

    public static SoloTacticalSpawnDecision Evaluate(
        int expected,
        int arrivals,
        float now,
        float deadline)
    {
        if (expected > 0 && arrivals >= expected)
            return SoloTacticalSpawnDecision.Active;

        if (now < deadline)
            return SoloTacticalSpawnDecision.Waiting;

        // Preserve Solo Tactical's established partial-squad behavior. Only a
        // request that produced no opponent at all is retried; one or more live
        // arrivals become the round once the assembly window has elapsed.
        return arrivals == 0
            ? SoloTacticalSpawnDecision.Retry
            : SoloTacticalSpawnDecision.Active;
    }
}
