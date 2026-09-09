using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Shared;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// How hard the arena is pushing back right now.
///
/// A normal raid scales its bots to the player's PROFILE — that is what the
/// progressive bot system installed alongside this mod does, and it is why the
/// bots here never change: the arena deliberately freezes the profile, so the
/// level that decides their tier is pinned to whatever it was before the raid
/// started, for every raid, forever. The axis that system scales along does not
/// exist in here.
///
/// So the arena scales along the only axis it actually has: the match. Two
/// signals, and they do different jobs.
///
/// TIME is the backbone and it only ever goes up. Without it a long raid gets
/// steadily easier — the player warms up, learns the spawns, climbs the gun
/// game ladder — while the opposition stands still.
///
/// The KILL STREAK is a surge on top, and it is the half that answers back: run
/// away with it and the arena leans on you within seconds. Dying clears the
/// surge but never the backbone, so a bad patch is a breather, not a reset.
///
/// One asymmetry worth knowing, because it cannot be designed away: difficulty
/// is baked into a bot when it spawns, and nothing re-rates one that is already
/// walking around. The climb therefore arrives within a burst, while the drop
/// after a death only reaches bots that have not spawned yet — the ones already
/// hunting you stay as hard as the moment that produced them.
///
/// Nothing here ever makes the arena EASIER than it was before this existed:
/// the baselines are the difficulties the spawner used to hardcode, and pressure
/// only climbs above them.
/// </summary>
internal static class ArenaPressure
{
    /// <summary>Kills in a row that count as fully on fire. Deliberately not a setting — the influence slider already scales it.</summary>
    private const int StreakForFullSurge = 8;

    /// <summary>Above this, PMCs go to the hardest setting the cap allows.</summary>
    private const float PmcPeakAt = 0.8f;

    /// <summary>Above this, even scavs stop being filler.</summary>
    private const float ScavPeakAt = 0.5f;

    /// <summary>0 = raid just started and nothing has happened. 1 = fully wound up.</summary>
    public static float Level
    {
        get
        {
            if (!Settings.ScaleDifficulty.Value)
                return 0f;

            var match = ArenaMatchService.Current;
            if (match == null)
                return 0f;

            try
            {
                var rampSeconds = Mathf.Max(60f, Settings.PressureRampMinutes.Value * 60f);
                float elapsed = match.Elapsed;
                int duration = 0;
                if (match.Mode is TeamDeathmatchMode teams && !teams.TryGetDifficultyClock(out elapsed, out duration))
                    return 0f;
                var overTime = (float)ArenaDifficultyClock.Progress(elapsed, duration, rampSeconds);

                var surge = Mathf.Clamp01(match.Streak / (float)StreakForFullSurge)
                            * Mathf.Clamp01(Settings.PressureStreakPercent.Value / 100f);

                return Mathf.Clamp01(overTime + surge);
            }
            catch (Exception)
            {
                // A difficulty curve is never worth a thrown frame.
                return 0f;
            }
        }
    }

    /// <summary>
    /// The difficulty to spawn at. The baselines are what the spawner used to
    /// hardcode, so pressure can only ever raise them.
    /// </summary>
    public static BotDifficulty DifficultyFor(bool isPmc)
    {
        var level = Level;

        var wanted = isPmc
            ? (level >= PmcPeakAt ? BotDifficulty.impossible : BotDifficulty.hard)
            : (level >= ScavPeakAt ? BotDifficulty.hard : BotDifficulty.normal);

        // The cap bounds the CLIMB, never the baseline. Its setting only offers
        // hard and impossible, but an old config file or a hand edit can still
        // carry something lower, and that must not undercut the floor.
        var cap = (int)Settings.MaxBotDifficulty.Value;
        var floor = isPmc ? (int)BotDifficulty.hard : (int)BotDifficulty.normal;
        return (BotDifficulty)Math.Max(floor, Math.Min((int)wanted, cap));
    }

    /// <summary>
    /// More of the fight comes from PMCs as pressure rises. This is the lever
    /// worth leaning on: a heavier mix is felt as a harder raid without making
    /// any single bot into something that kills through a wall.
    /// </summary>
    public static int PmcFloor(int configured)
    {
        // A floor of zero is the panel slider's way of saying "stop forcing PMC
        // top-ups at all". Adding pressure on top would quietly override the one
        // control a player reaches for when a raid has become too much.
        if (configured <= 0)
            return 0;

        return configured + Mathf.RoundToInt(Level * Mathf.Max(0, Settings.PressurePmcBonus.Value));
    }

    /// <summary>Refills come faster under pressure, down to half the configured wait.</summary>
    public static float BurstCooldown(float configured)
        => configured * Mathf.Lerp(1f, 0.5f, Level);

    /// <summary>One line for the panel, so the curve can be watched while tuning.</summary>
    public static string Describe()
    {
        if (!Settings.ScaleDifficulty.Value)
            return "off";

        var level = Level;
        return $"{level * 100f:0}%  (PMC {DifficultyFor(true)}, scav {DifficultyFor(false)})";
    }
}
