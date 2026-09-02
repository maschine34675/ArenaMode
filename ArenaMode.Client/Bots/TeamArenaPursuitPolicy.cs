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
}
