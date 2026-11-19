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

    /// <summary>Same id as ArenaPopulationPolicy.Tactical; a test pins the equality.</summary>
    public const string TacticalModeId = "tactical";

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
    /// The step the position reports use. Zombies are not touched by the step's report
    /// interval: the horde has its own hunt nudge, and the contract's Zombie section changes
    /// only the alive limit and the composition, so every step reports at the configured pace.
    /// </summary>
    public static ArenaDifficultyStep ReportStep(string? mode, ArenaDifficultyStep step)
        => string.Equals(mode, ZombieModeId, StringComparison.OrdinalIgnoreCase) ? ArenaDifficultyStep.Maximum : step;

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

    /// <summary>
    /// Gun Game: how many levels below the current gear level a pooled bot profile may sit before it is
    /// discarded. The server's band is the centre +/-5 (<c>Spread</c> in ArenaBotTierScalePatch), so a
    /// pooled profile below centre - 5 is below anything the server would generate now. A native Gun Game
    /// raid with a slack of 10 let profiles made shortly before the end of the ramp feed the spawns for
    /// minutes (levels 55..65 against a band of 61..71). A fresh batch is not stale on arrival as long as
    /// the server's clock is not behind the client's; the purge's 30-second limit bounds the cost if it is.
    /// </summary>
    public const int GunGameStaleGearLevels = 5;

    /// <summary>True when a pooled profile's gear level fell more than <see cref="GunGameStaleGearLevels"/> behind the ramp.</summary>
    public static bool IsGunGameGearStale(int profileLevel, int currentGearLevel)
        => profileLevel < currentGearLevel - GunGameStaleGearLevels;

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
    /// The PMC levels worth pooling at raid start, a subset of <see cref="PmcRequestLevels"/>.
    /// The server builds a profile with the gear band of the moment it is asked, and only
    /// Gun Game's band moves. A level pooled at second 0 would reach the field minutes later
    /// in start-level gear, so in Gun Game only the opening level is warmed (its AI level is
    /// requested while the band is still near the start, and later refills use the live band);
    /// the step's level is requested fresh once progress reaches its threshold. Nothing is
    /// warmed outside an active session, for Zombie, or for a Tactical squad of scavs.
    /// </summary>
    public static ArenaBotLevel[] PrewarmLevels(bool active, string? mode, bool tacticalSquadIsPmc, ArenaDifficultyStep step)
    {
        if (!active)
            return Array.Empty<ArenaBotLevel>();
        if (string.Equals(mode, TacticalModeId, StringComparison.OrdinalIgnoreCase) && !tacticalSquadIsPmc)
            return Array.Empty<ArenaBotLevel>();
        if (ScalesWithTime(mode))
            return new[] { GunGamePmcOpening };
        return PmcRequestLevels(mode, step);
    }

    /// <summary>Zombies at Reduced: the most infected alive at once (the lower of the setting and this).</summary>
    public const int ZombieMaxAliveReduced = 24;

    /// <summary>Zombies at Maximum: the melee slow share fades from this wave on.</summary>
    public const int ZombieSlowFadeFromWave = 4;

    /// <summary>Zombies at Maximum: percentage points the slow share loses per wave from the fade wave on.</summary>
    public const int ZombieSlowFadePointsPerWave = 5;

    /// <summary>Zombies at Maximum: the shooter share grows by this share of the configured value per wave.</summary>
    public const double ZombieShooterGrowthPerWave = 0.1;

    /// <summary>Zombies at Maximum: the shooter share never exceeds this multiple of the configured value.</summary>
    public const double ZombieShooterGrowthCap = 2d;

    /// <summary>
    /// Infected alive at once: Maximum keeps the setting (32 from wave one stays), Reduced
    /// takes the lower of the setting and <see cref="ZombieMaxAliveReduced"/>.
    /// </summary>
    public static int ZombieMaxAlive(ArenaDifficultyStep step, int configured)
        => step == ArenaDifficultyStep.Reduced ? Math.Min(configured, ZombieMaxAliveReduced) : configured;

    /// <summary>
    /// The melee slow share of a wave given the rhythm's value (<paramref name="baseSlowPercent"/>).
    /// Reduced keeps the rhythm. Maximum subtracts 5 points per wave from wave 4 on and never
    /// goes below 0, so the horde turns all-fast: rhythm 40, 20, 30, 40, 20, ... becomes
    /// 35, 10, 15, 20, 0, 0, 5, 0 from wave 4.
    /// </summary>
    public static int ZombieSlowPercent(ArenaDifficultyStep step, int baseSlowPercent, int waveNumber)
    {
        int slow = Math.Clamp(baseSlowPercent, 0, 100);
        if (step == ArenaDifficultyStep.Reduced || waveNumber < ZombieSlowFadeFromWave)
            return slow;
        long fade = (long)ZombieSlowFadePointsPerWave * (waveNumber - (ZombieSlowFadeFromWave - 1));
        return (int)Math.Max(0L, slow - fade);
    }

    /// <summary>
    /// The shooter share of a wave given the configured share. Reduced keeps it flat.
    /// Maximum adds a tenth of the configured share per wave after the first, up to twice the
    /// configured share and never above 100 %; exactly 0 % and 100 % stay exact.
    /// </summary>
    public static int ZombieShooterPercent(ArenaDifficultyStep step, int configured, int waveNumber)
    {
        int share = Math.Clamp(configured, 0, 100);
        if (step == ArenaDifficultyStep.Reduced || waveNumber <= 1 || share == 0 || share == 100)
            return share;
        // Two bounds, one enforcer each: the growth cap holds the share at twice the setting
        // (and keeps the product small at any wave number), the clamp holds it at 100 %.
        double growth = Math.Min(ZombieShooterGrowthCap - 1d, ZombieShooterGrowthPerWave * ((long)waveNumber - 1));
        int grown = (int)Math.Round(share * (1d + growth), MidpointRounding.AwayFromZero);
        return Math.Clamp(grown, share, 100);
    }

    /// <summary>
    /// The AI level of a mode at a progress. Outside Gun Game progress is 1, so callers pass
    /// <see cref="Progress"/> and get the step's flat row from the first request.
    /// </summary>
    public static ArenaBotLevel BotLevel(ArenaDifficultyStep step, bool pmc, double progress)
        => pmc ? PmcLevelAt(step, progress) : ScavLevelAt(step, progress);
}
