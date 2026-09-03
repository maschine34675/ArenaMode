using System.Collections.Generic;

namespace ArenaMode.Bots;

internal enum TeamArenaApproachDecision
{
    Approach,
    Engaged,
    ImmediateDanger,
    Busy,
    InvalidDistance
}

internal enum TeamArenaSainAwarenessState
{
    NotPresent,
    Exact,
    TargetUnavailable,
    SourceIncompatible
}

internal enum TeamArenaRouteCandidate
{
    None,
    Partial,
    Complete
}

internal enum TeamArenaRouteSelection
{
    None,
    Mover,
    Body
}

/// <summary>
/// Pure guard for Arena-owned movement after the local player has died. Combat
/// actions and short-range engagements keep ownership; only an idle, separated
/// bot receives an approach command.
/// </summary>
internal static class TeamArenaPursuitPolicy
{
    // Keep hidden opponents moving until they are nearly co-located. A larger
    // air-distance radius can put two bots on opposite sides of a wall or on
    // adjacent floors and recreate the stand-off this service exists to break.
    internal const float DestinationReachDistanceMeters = 1f;

    internal static TeamArenaApproachDecision Decide(
        float squaredDistance,
        bool enemyVisible,
        bool enemyShootable,
        bool healing,
        bool reloading,
        bool throwingGrenade,
        bool usingDoor,
        bool immediateDanger)
    {
        if (float.IsNaN(squaredDistance) || float.IsInfinity(squaredDistance) || squaredDistance < 0f)
            return TeamArenaApproachDecision.InvalidDistance;

        if (immediateDanger)
            return TeamArenaApproachDecision.ImmediateDanger;

        if (healing || reloading || throwingGrenade || usingDoor)
            return TeamArenaApproachDecision.Busy;

        if (enemyVisible && enemyShootable)
            return TeamArenaApproachDecision.Engaged;

        // Euclidean proximity does not prove contact: on Labs two bots can be
        // less than a metre apart through a wall, door, ceiling or floor. Keep
        // routing until combat is real; the bounded stall pass will rotate an
        // unreachable close target.
        return TeamArenaApproachDecision.Approach;
    }

    /// <summary>
    /// Keeps rejected targets out of consideration until every currently live
    /// candidate has been tried. This guarantees that a nearer unreachable bot
    /// cannot starve a farther reachable one.
    /// </summary>
    internal static void PrepareRejectedTargetPass(
        HashSet<string> rejectedTargets,
        HashSet<string> liveTargets)
    {
        rejectedTargets.RemoveWhere(profile => !liveTargets.Contains(profile));
        if (liveTargets.Count == 0)
        {
            rejectedTargets.Clear();
            return;
        }

        foreach (string profile in liveTargets)
        {
            if (!rejectedTargets.Contains(profile))
                return;
        }

        rejectedTargets.Clear();
    }

    /// <summary>
    /// A partial NavMesh path is still useful when its last corner moves the bot
    /// farther than the Arena arrival radius. The movement owner receives that
    /// reachable corner, never the unreachable requested target.
    /// </summary>
    internal static bool CanUsePartialRoute(
        int cornerCount,
        float squaredAdvanceFromAnchor,
        float squaredAdvanceFromBody)
        => cornerCount >= 2
           && IsUsefulAdvance(squaredAdvanceFromAnchor)
           && IsUsefulAdvance(squaredAdvanceFromBody);

    internal static TeamArenaRouteSelection SelectRouteCandidate(
        TeamArenaRouteCandidate mover,
        float moverSquaredAdvance,
        TeamArenaRouteCandidate body,
        float bodySquaredAdvance)
    {
        if (mover == TeamArenaRouteCandidate.Complete)
            return TeamArenaRouteSelection.Mover;
        if (body == TeamArenaRouteCandidate.Complete)
            return TeamArenaRouteSelection.Body;
        if (mover != TeamArenaRouteCandidate.Partial)
            return body == TeamArenaRouteCandidate.Partial
                ? TeamArenaRouteSelection.Body
                : TeamArenaRouteSelection.None;
        if (body != TeamArenaRouteCandidate.Partial)
            return TeamArenaRouteSelection.Mover;
        return bodySquaredAdvance > moverSquaredAdvance
            ? TeamArenaRouteSelection.Body
            : TeamArenaRouteSelection.Mover;
    }

    /// <summary>
    /// BigBrain only needs the exact native EFT relation to own approach movement.
    /// A target-specific SAIN cache miss must not leave the bot without any owner;
    /// an incompatible source wrapper remains unsafe because SAIN resolves sources
    /// globally by profile ID.
    /// </summary>
    internal static bool CanPublishAfterAwareness(
        TeamArenaSainAwarenessState awareness,
        bool exactNativeRelation)
        => exactNativeRelation
           && (awareness == TeamArenaSainAwarenessState.Exact
               || awareness == TeamArenaSainAwarenessState.NotPresent
               || awareness == TeamArenaSainAwarenessState.TargetUnavailable);

    private static bool IsUsefulAdvance(float squaredAdvance)
        => !float.IsNaN(squaredAdvance)
           && !float.IsInfinity(squaredAdvance)
           && squaredAdvance > DestinationReachDistanceMeters * DestinationReachDistanceMeters;
}
