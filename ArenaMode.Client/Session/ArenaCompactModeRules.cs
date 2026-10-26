using ArenaMode.Modes;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Read-only menu copy, never a second gameplay policy. The caller supplies the
/// current accepted preferences and the actual map layout's objective count.
/// Two short status lines fit the existing description slot; no prose expansion.
/// </summary>
internal static class ArenaCompactModeRules
{
    internal const int MaxLineCharacters = 44;
    internal const string GearProtection = "Gear restored · rewards optional";

    internal static string Describe(string mode, bool team, int rounds, int kills, int seconds,
        bool freeForAll, int teamKills, int teamSeconds, int tacticalWins,
        bool friendlyFire, int controlPoints)
    {
        switch (ArenaModeCatalog.NormalizeId(mode))
        {
            case ArenaModeCatalog.GunGameId:
                return "ENDLESS · weapon ladder repeats\nKills advance your assigned weapon";
            case ArenaModeCatalog.DeathmatchId:
                string ending = kills == 0 && seconds == 0 ? "ENDLESS · no round winner"
                    : (rounds == 0 ? "ENDLESS ROUNDS" : rounds + (rounds == 1 ? " ROUND" : " ROUNDS")) + " · first limit ends round";
                return ending + "\n" + (freeForAll ? "FFA · everyone fights everyone"
                    : "ALL VS YOU · enemies ignore each other");
            case ArenaModeCatalog.TeamDeathmatchId:
                return (teamKills == 0 && teamSeconds == 0 ? "ENDLESS · no automatic match end"
                    : "MATCH END · first active limit")
                    + "\nFRIENDLY FIRE · " + (friendlyFire ? "ON" : "OFF");
            case ArenaModeCatalog.DominationId:
                return (controlPoints > 0 ? "HOLD " + controlPoints + (controlPoints == 1 ? " POINT" : " POINTS") + " · earn team score"
                    : "HOLD POINTS · select a map")
                    + "\nMATCH END · 200 points / 10 min";
            case ArenaModeCatalog.TacticalId:
                return team ? "FIRST TO " + Math.Max(1, Math.Min(9, tacticalWins)) + " WINS · death = spectate\nFallen allies return next round"
                    : "ENDLESS ROUNDS · death / timeout loses\nARENA CASH · buy / keep surviving kit";
            case ArenaModeCatalog.ZombieId:
                return team ? "TEAM WIPE ENDS MATCH\nWAVE CLEAR · gear + fallen allies return"
                    : "ONE LIFE · death ends match\nWAVE CLEAR · gear refills";
            default:
                return string.Empty;
        }
    }

    internal static string DeathmatchObjective(int kills, int seconds, bool freeForAll)
        => kills == 0 && seconds == 0 ? "Endless combat"
            : freeForAll ? "Most kills" : kills > 0 ? "Reach kill target" : "Survive timer";

    internal static string CompanionHint(string mode, bool team, int companions)
        => mode is ArenaModeCatalog.TacticalId or ArenaModeCatalog.ZombieId
            ? team ? "PITFIRETEAM · " + companions + " FROM MY SQUAD"
                : "COMPANIONS: PITFIRETEAM / MY SQUAD"
            : "GAME MODE";
}
