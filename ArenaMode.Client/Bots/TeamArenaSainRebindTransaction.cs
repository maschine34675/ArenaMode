using System;

namespace ArenaMode.Bots;

internal enum TeamArenaSainRebindOutcome
{
    ExactExisting,
    ExactRebuilt,
    Missing,
    NativeRebuildFailed,
    RebuiltEnemyMismatch
}

/// <summary>
/// Orders the destructive SAIN stale-enemy repair. SAIN removes the associated
/// native EFT EnemyInfo with its cached wrapper, so the exact current-round EFT
/// relation must be rebuilt before SAIN is allowed to create another wrapper.
/// </summary>
internal static class TeamArenaSainRebindTransaction
{
    internal static TeamArenaSainRebindOutcome TryEnsureExact<TEnemy>(
        Func<TEnemy> resolveOrCreate,
        Predicate<TEnemy> isExact,
        Action removeStale,
        Func<bool> rebuildExactNative,
        out TEnemy exactEnemy)
        where TEnemy : class
    {
        exactEnemy = null;
        if (resolveOrCreate == null
            || isExact == null
            || removeStale == null
            || rebuildExactNative == null)
        {
            return TeamArenaSainRebindOutcome.Missing;
        }

        TEnemy candidate = resolveOrCreate();
        if (candidate == null)
            return TeamArenaSainRebindOutcome.Missing;

        if (isExact(candidate))
        {
            exactEnemy = candidate;
            return TeamArenaSainRebindOutcome.ExactExisting;
        }

        removeStale();
        if (!rebuildExactNative())
            return TeamArenaSainRebindOutcome.NativeRebuildFailed;

        candidate = resolveOrCreate();
        if (candidate == null)
            return TeamArenaSainRebindOutcome.RebuiltEnemyMismatch;

        if (!isExact(candidate))
        {
            // Do not leave the replacement stale wrapper cached. Its removal
            // is destructive to native EFT awareness too, so restore the exact
            // native relation once more before failing closed.
            removeStale();
            if (!rebuildExactNative())
                return TeamArenaSainRebindOutcome.NativeRebuildFailed;
            return TeamArenaSainRebindOutcome.RebuiltEnemyMismatch;
        }

        exactEnemy = candidate;
        return TeamArenaSainRebindOutcome.ExactRebuilt;
    }
}
