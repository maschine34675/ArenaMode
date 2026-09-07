using ArenaMode.Areas;
using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Respawn;

internal static partial class Program
{
    private static void BoundedGiveUpContract()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, bounded: true);
            ArenaGiveUp.Request();
            Check(raid.Mode.TeamSize == 2 && raid.Match.Deaths == 1
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "bounded 2v2 keeps exactly one opposing team kill for a given-up life");
            ArenaAreaRuntime.IsPending = true;
            ArenaGiveUp.Reset();
            ArenaGiveUp.Request();
            Check(raid.Mode.HoldRespawn && ArenaRespawnDriver.IsRespawnInProgress
                && raid.Match.Deaths == 1
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "pending area/death pipeline holds respawn without duplicating give-up credit");
            ArenaAreaRuntime.IsPending = false;
            raid.Respawn();
            Check(!raid.Mode.HoldRespawn && ArenaAreaRuntime.PositionChecks == 1
                && ArenaAreaRuntime.Aborts.Count == 0,
                "verified bounded replacement resumes the normal give-up lifecycle");
            ArenaGiveUp.Request();
            Check(raid.Match.Deaths == 2
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 2,
                "the next verified life can be given up exactly once");
        }
        ArenaAreaRuntime.Reset();
    }
}
