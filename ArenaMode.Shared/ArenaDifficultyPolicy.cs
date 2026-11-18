#nullable enable
using System;

namespace ArenaMode.Shared;

/// <summary>The one difficulty setting: the highest strength, or somewhat less of everything.</summary>
public enum ArenaDifficultyStep
{
    Maximum = 0,
    Reduced = 1,
}

/// <summary>
/// Arena's own bot difficulty levels. Shared code cannot reference the EFT enum, so the
/// client maps these one to one onto <c>BotDifficulty</c> at the spawn call.
/// </summary>
public enum ArenaBotLevel
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    Impossible = 3,
}

/// <summary>
/// The difficulty table of docs/reviews/DIFFICULTY-MODEL-20261118.md as named members.
///
/// Pure and static: client, server and tests read the same numbers, and no call site holds
/// a level literal. Progress is 1 in every mode except Gun Game, so a fixed value holds from
/// the first spawn request; only Gun Game ramps, over the map's configured ramp minutes.
/// </summary>
public static class ArenaDifficultyPolicy
{
    public const string WireMaximum = "maximum";
    public const string WireReduced = "reduced";

    public const string GunGameModeId = "gungame";

    /// <summary>Same id as ArenaPopulationPolicy.Zombie; repeated so harnesses linking only this file still compile.</summary>
    public const string ZombieModeId = "zombie";

    /// <summary>
    /// The highest gear level Arena may ever claim, a hard safety bound rather than a taste.
    /// The bot system looks the written level up in its tier table and that lookup throws
    /// when no row matches, inside its code and outside any guard here: a config value past
    /// the end would kill every bot of the raid with nothing in the log. The experience table
    /// runs out at 79, and past roughly 84 the level range collapses onto a single level.
    /// The config is clamped to this instead of being trusted.
    /// </summary>
    public const int MaxGearLevel = 79;

    /// <summary>Reduced gear level as a share of the map's Maximum gear level.</summary>
    public const double ReducedGearShare = 0.6;

    /// <summary>Gun Game: the PMC level stays <see cref="GunGamePmcOpening"/> below this progress.</summary>
    public const double GunGamePmcStepProgress = 0.8;

    /// <summary>Gun Game: the scav level stays <see cref="GunGameScavOpening"/> below this progress.</summary>
    public const double GunGameScavStepProgress = 0.5;

    public const ArenaBotLevel GunGamePmcOpening = ArenaBotLevel.Hard;
    public const ArenaBotLevel GunGameScavOpening = ArenaBotLevel.Normal;

    public const int PmcBonusMaximum = 4;
    public const int PmcBonusReduced = 2;

    public const double CooldownFactorMaximum = 0.5;
    public const double CooldownFactorReduced = 0.75;

    /// <summary>Share of the user's bot speed surplus over 1.0 that is kept.</summary>
    public const double SpeedSurplusScaleMaximum = 1.0;
    public const double SpeedSurplusScaleReduced = 0.5;

    /// <summary>Multiplier on the position-report (HuntPlayer sweep) interval.</summary>
    public const double ReportIntervalFactorMaximum = 1.0;
    public const double ReportIntervalFactorReduced = 1.5;

    /// <summary>Null or empty is Maximum; anything not exactly a wire string is refused.</summary>
    public static bool TryParse(string? wire, out ArenaDifficultyStep step)
    {
        step = ArenaDifficultyStep.Maximum;
        if (string.IsNullOrEmpty(wire) || string.Equals(wire, WireMaximum, StringComparison.Ordinal))
            return true;
        if (string.Equals(wire, WireReduced, StringComparison.Ordinal))
        {
            step = ArenaDifficultyStep.Reduced;
            return true;
        }
        return false;
    }

    public static string ToWire(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? WireReduced : WireMaximum;

    /// <summary>Only Gun Game scales with time; every other mode is at progress 1.</summary>
    public static bool ScalesWithTime(string? mode)
        => string.Equals(mode, GunGameModeId, StringComparison.OrdinalIgnoreCase);

    public static ArenaBotLevel PmcLevel(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? ArenaBotLevel.Hard : ArenaBotLevel.Impossible;

    public static ArenaBotLevel ScavLevel(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? ArenaBotLevel.Normal : ArenaBotLevel.Hard;

    public static int PmcBonus(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? PmcBonusReduced : PmcBonusMaximum;

    public static double LegacyCooldownFactor(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? CooldownFactorReduced : CooldownFactorMaximum;

    public static double BotSpeedSurplusScale(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? SpeedSurplusScaleReduced : SpeedSurplusScaleMaximum;

    public static double ReportIntervalFactor(ArenaDifficultyStep step)
        => step == ArenaDifficultyStep.Reduced ? ReportIntervalFactorReduced : ReportIntervalFactorMaximum;

    /// <summary>User's bot speed multipliers are bounded here before the step scales their surplus.</summary>
    public const double BotSpeedUserCap = 1.5;

    /// <summary>
    /// The speed multiplier a bot gets: the user's setting capped at <see cref="BotSpeedUserCap"/>
    /// first, then only the surplus over 1.0 scaled by the step (cap before scale, so Reduced
    /// with a setting of 99 gives 1.25, not 1.5).
    /// </summary>
    public static double BotSpeedFactor(double requested, ArenaDifficultyStep step)
        => 1d + (Math.Min(requested, BotSpeedUserCap) - 1d) * BotSpeedSurplusScale(step);

    /// <summary>
    /// The HuntPlayer sweep interval: the per-map value, then the Tactical halving (kept at
    /// two seconds at least), then the step's factor last. The caller keeps its own floor.
    /// </summary>
    public static double SweepInterval(double mapSeconds, bool tactical, ArenaDifficultyStep step)
    {
        double interval = tactical ? Math.Max(2d, mapSeconds / 2d) : mapSeconds;
        return interval * ReportIntervalFactor(step);
    }

    /// <summary>
    /// The step's gear level of a map: Maximum is the map's end level, Reduced 60 % of it,
    /// never below the start level; both clamped to [1, 79] like the server does today.
    /// The end level is clamped to 79 first and the 60 % taken of that clamped value, so an
    /// absurd config (end 100) gives round(0.6 x 79), the safe order for the bot tier table.
    /// </summary>
    public static int GearLevel(ArenaDifficultyStep step, int startLevel, int endLevel)
    {
        int start = Math.Clamp(startLevel, 1, MaxGearLevel);
        int end = Math.Clamp(endLevel, start, MaxGearLevel);
        if (step == ArenaDifficultyStep.Maximum)
            return end;
        int reduced = (int)Math.Round(end * ReducedGearShare, MidpointRounding.AwayFromZero);
        return Math.Clamp(reduced, start, end);
    }

    /// <summary>Progress of the mode at the given time: the Gun Game ramp, 1 everywhere else.</summary>
    public static double Progress(string? mode, double elapsedSeconds, double rampMinutes)
        => ScalesWithTime(mode) ? GunGameProgress(elapsedSeconds, rampMinutes) : 1d;

    /// <summary>clamp01(elapsed / ramp); NaN, negative and a non-positive ramp are safe.</summary>
    public static double GunGameProgress(double elapsedSeconds, double rampMinutes)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsNaN(rampMinutes))
            return 0d;
        double ramp = Math.Max(1d, rampMinutes) * 60d;
        double progress = elapsedSeconds / ramp;
        return progress <= 0d ? 0d : progress >= 1d ? 1d : progress;
    }

    /// <summary>
    /// The gear level at a progress: start to the step's gear level, linear and rounded.
    /// Never below the start level, never above 79.
    /// </summary>
    public static int GearLevelAt(ArenaDifficultyStep step, int startLevel, int endLevel, double progress)
    {
        int start = Math.Clamp(startLevel, 1, MaxGearLevel);
        int target = GearLevel(step, startLevel, endLevel);
        double p = double.IsNaN(progress) ? 0d : Math.Clamp(progress, 0d, 1d);
        int level = (int)Math.Round(start + (target - start) * p);
        return Math.Clamp(level, start, Math.Max(start, target));
    }

    /// <summary>Gun Game: hard below progress 0.8, then the step's PMC level.</summary>
    public static ArenaBotLevel PmcLevelAt(ArenaDifficultyStep step, double progress)
        => double.IsNaN(progress) || progress < GunGamePmcStepProgress ? GunGamePmcOpening : PmcLevel(step);

    /// <summary>Gun Game: normal below progress 0.5, then the step's scav level.</summary>
    public static ArenaBotLevel ScavLevelAt(ArenaDifficultyStep step, double progress)
        => double.IsNaN(progress) || progress < GunGameScavStepProgress ? GunGameScavOpening : ScavLevel(step);

    /// <summary>Gun Game: round(progress x the step's bonus).</summary>
    public static int PmcBonusAt(ArenaDifficultyStep step, double progress)
    {
        double p = double.IsNaN(progress) ? 0d : Math.Clamp(progress, 0d, 1d);
        return (int)Math.Round(p * PmcBonus(step), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The PMC levels the client can request in a mode for a frozen step: exactly the pairs
    /// the PMC profile backup has to cover. Fixed modes ask for one level (progress 1 from
    /// the first request); Gun Game opens at <see cref="GunGamePmcOpening"/> and ends at the
    /// step's row, so it asks for both (one when they coincide, as at Reduced). Zombie never
    /// requests a PMC, so it needs none. Ordered, without duplicates.
    /// </summary>
    public static ArenaBotLevel[] PmcRequestLevels(string? mode, ArenaDifficultyStep step)
    {
        if (string.Equals(mode, ZombieModeId, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<ArenaBotLevel>();
        var end = PmcLevel(step);
        if (ScalesWithTime(mode) && GunGamePmcOpening != end)
            return new[] { GunGamePmcOpening, end };
        return new[] { end };
    }

    /// <summary>
    /// The AI level of a mode at a progress. Outside Gun Game progress is 1, so callers pass
    /// <see cref="Progress"/> and get the step's flat row from the first request.
    /// </summary>
    public static ArenaBotLevel BotLevel(ArenaDifficultyStep step, bool pmc, double progress)
        => pmc ? PmcLevelAt(step, progress) : ScavLevelAt(step, progress);
}
