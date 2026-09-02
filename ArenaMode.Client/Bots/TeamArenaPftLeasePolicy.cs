namespace ArenaMode.Bots;

internal enum TeamArenaPftLeaseState
{
    Stale,
    Active,
    CancelPending,
    Foreign
}

/// <summary>
/// Pure ownership decision for the reflection-backed PitFireTeam adapter. It
/// separates Arena's first post-death takeover from later producer changes and
/// prevents cached bookkeeping from being mistaken for an active PFT command.
/// </summary>
internal static class TeamArenaPftLeasePolicy
{
    internal static TeamArenaPftLeaseState Evaluate(
        bool hasTrackedRoute,
        bool cancelPending,
        bool hasRawCommand,
        bool rawIsPush,
        bool hasTargetLock,
        bool targetLockMatches,
        bool hasPushLeaseForTarget,
        bool pushSequenceMatches,
        bool hasTacticalLeaseForTarget,
        bool tacticalRawMatches,
        bool tacticalActionMatches)
    {
        if (cancelPending)
            return TeamArenaPftLeaseState.CancelPending;

        if (hasPushLeaseForTarget)
        {
            if (!pushSequenceMatches)
            {
                return hasRawCommand || hasTargetLock
                    ? TeamArenaPftLeaseState.Foreign
                    : TeamArenaPftLeaseState.Stale;
            }

            if (hasRawCommand && !rawIsPush)
            {
                return tacticalRawMatches
                    ? TeamArenaPftLeaseState.Active
                    : TeamArenaPftLeaseState.Foreign;
            }

            if (hasTargetLock && !targetLockMatches)
                return TeamArenaPftLeaseState.Foreign;

            if (rawIsPush || targetLockMatches)
                return TeamArenaPftLeaseState.Active;

            if (hasRawCommand || hasTargetLock)
                return TeamArenaPftLeaseState.Foreign;
        }

        if (hasTacticalLeaseForTarget)
        {
            if (tacticalRawMatches)
                return TeamArenaPftLeaseState.Active;

            if (hasRawCommand || hasTargetLock)
                return TeamArenaPftLeaseState.Foreign;

            if (tacticalActionMatches)
                return TeamArenaPftLeaseState.Active;
        }

        // The initial Arena claim intentionally replaces a pre-death PFT
        // command. Once Arena has issued a route, a new command/lock belongs to
        // another producer and must be preserved.
        return hasTrackedRoute && (hasRawCommand || hasTargetLock)
            ? TeamArenaPftLeaseState.Foreign
            : TeamArenaPftLeaseState.Stale;
    }
}
