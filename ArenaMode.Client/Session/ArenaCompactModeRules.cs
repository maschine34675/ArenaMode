using ArenaMode.Modes;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Read-only menu copy, never a second gameplay policy. The caller supplies the
/// current accepted preferences and the actual map layout's objective count.
/// The description explains the mode; controls carry the selected values.
/// </summary>
internal static class ArenaCompactModeRules
{
    internal const int MaxLineCharacters = 44;

    internal static string Describe(string mode, bool team, int rounds, int kills, int seconds,
        bool freeForAll, int teamKills, int teamSeconds, int tacticalWins,
        bool friendlyFire, int controlPoints, int dominationPoints = 200, int dominationSeconds = 600)
    {
        switch (ArenaModeCatalog.NormalizeId(mode))
        {
            case ArenaModeCatalog.GunGameId:
                return "Kills advance your weapon.\nThe weapon ladder repeats.";
            case ArenaModeCatalog.DeathmatchId:
                string objective = kills == 0 && seconds == 0 ? "No round winner without limits."
                    : freeForAll ? "Most kills wins the round."
                    : kills > 0 ? "Reach the kill target." : "Fight until time runs out.";
                return (freeForAll ? "Everyone fights everyone." : "Enemies only target you.") + "\n" + objective;
            case ArenaModeCatalog.TeamDeathmatchId:
                return "USEC versus BEAR.\n" + (teamKills == 0 && teamSeconds == 0
                    ? "No match end without limits." : "Most team kills wins.");
            case ArenaModeCatalog.DominationId:
                return "USEC versus BEAR.\nHold " + (controlPoints > 0
                    ? controlPoints + (controlPoints == 1 ? " point" : " points") : "points") + " to earn team score.";
            case ArenaModeCatalog.TacticalId:
                return team ? "First to " + Math.Max(1, Math.Min(9, tacticalWins)) + " round wins.\nOn death, spectate until next round."
                    : "Eliminate the enemy squad.\nEndless rounds; death / timeout loses.";
            case ArenaModeCatalog.ZombieId:
                return (team ? "Team wipe ends the match." : "Survive escalating waves.")
                    + "\nGear refills after each cleared wave.";
            default:
                return string.Empty;
        }
    }

    internal static string DeathmatchObjective(int kills, int seconds, bool freeForAll)
        => kills == 0 && seconds == 0 ? "Endless combat"
            : freeForAll ? "Most kills" : kills > 0 ? "Reach kill target" : "Survive timer";

    internal static string CompanionHint(string mode, bool team, int companions)
        => mode is ArenaModeCatalog.TacticalId or ArenaModeCatalog.ZombieId
            ? team ? "GAME MODE / PITFIRETEAM" : "GAME MODE"
            : "GAME MODE";
}
