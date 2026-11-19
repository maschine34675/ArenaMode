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
/// What a Zombie wave is made of beyond the ordinary mix. A theme changes the per-wave slow
/// and shooter shares and nothing else: not the wave size, the alive limit, the roles, the
/// profile plan or the spawn path.
/// </summary>
public enum ZombieWaveTheme
{
    /// <summary>The ordinary mix of the step.</summary>
    None = 0,
    /// <summary>No shooters and no slow infected: everything runs at the player.</summary>
    Runners = 1,
    /// <summary>Every infected is a shooter.</summary>
    Shooters = 2,
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
    /// Zombies at Maximum: every this-many waves (from the first multiple) is a special wave.
    /// Five because the slow share has faded to almost nothing by then (waves 4 to 9 are
    /// 35, 10, 15, 20, 0, 0 percent): the plateau after wave 11 needs a landmark every few
    /// minutes, and five leaves four ordinary waves between two of them.
    /// </summary>
    public const int ZombieSpecialWaveInterval = 5;

    /// <summary>
    /// The calendar only, before the user's shooter setting is consulted: Maximum only (Reduced
    /// is "somewhat less of everything" with a flat shooter share, and never fades, so the
    /// plateau this solves does not exist there), every fifth wave from wave 5, odd multiples
    /// of five (5, 15, 25, ...) are Runners and even ones (10, 20, ...) are Shooters. No state
    /// and no random number, so the shares below and the announcement agree by construction.
    /// </summary>
    public static ZombieWaveTheme ZombieScheduledTheme(ArenaDifficultyStep step, int waveNumber)
    {
        if (step != ArenaDifficultyStep.Maximum || waveNumber < ZombieSpecialWaveInterval
            || waveNumber % ZombieSpecialWaveInterval != 0)
            return ZombieWaveTheme.None;
        return (waveNumber / ZombieSpecialWaveInterval) % 2 == 1 ? ZombieWaveTheme.Runners : ZombieWaveTheme.Shooters;
    }

    /// <summary>
    /// The theme that actually applies and is announced: a theme that would take away what the
    /// user fixed is dropped. Shooters never run at a shooter share of 0 (the user switched
    /// them off) and Runners never run at 100 (the user fixed every infected as a shooter, and
    /// the draw is exact at both values). The two share functions below return exactly these
    /// themes' shares, which a test checks for every setting and wave.
    /// </summary>
    public static ZombieWaveTheme ZombieWaveThemeFor(ArenaDifficultyStep step, int configuredShooter, int waveNumber)
    {
        int share = Math.Clamp(configuredShooter, 0, 100);
        var theme = ZombieScheduledTheme(step, waveNumber);
        if (theme == ZombieWaveTheme.Runners && share == 100) return ZombieWaveTheme.None;
        if (theme == ZombieWaveTheme.Shooters && share == 0) return ZombieWaveTheme.None;
        return theme;
    }

    /// <summary>
    /// The melee slow share of a wave given the rhythm's value (<paramref name="baseSlowPercent"/>).
    /// Reduced keeps the rhythm. Maximum subtracts 5 points per wave from wave 4 on and never
    /// goes below 0, so the horde turns all-fast: rhythm 40, 20, 30, 40, 20, ... becomes
    /// 35, 10, 15, 20, 0, 0, 5, 0 from wave 4. A Runners wave (see <see cref="ZombieScheduledTheme"/>)
    /// is 0.
    /// </summary>
    public static int ZombieSlowPercent(ArenaDifficultyStep step, int baseSlowPercent, int waveNumber)
    {
        int slow = Math.Clamp(baseSlowPercent, 0, 100);
        if (step == ArenaDifficultyStep.Reduced || waveNumber < ZombieSlowFadeFromWave)
            return slow;
        // A Runners wave has no slow infected at all. Shooters leave the slow share alone: the
        // roll is never reached when every draw is a shooter. At a shooter share of 100 this
        // function is not consulted either, so reading the calendar without the setting is exact.
        if (ZombieScheduledTheme(step, waveNumber) == ZombieWaveTheme.Runners)
            return 0;
        long fade = (long)ZombieSlowFadePointsPerWave * (waveNumber - (ZombieSlowFadeFromWave - 1));
        return (int)Math.Max(0L, slow - fade);
    }

    /// <summary>
    /// The shooter share of a wave given the configured share. Reduced keeps it flat.
    /// Maximum adds a tenth of the configured share per wave after the first, up to twice the
    /// configured share and never above 100 %; exactly 0 % and 100 % stay exact. A Runners wave
    /// is 0 % and a Shooters wave 100 % (<see cref="ZombieScheduledTheme"/>), except at those two exact settings.
    /// </summary>
    public static int ZombieShooterPercent(ArenaDifficultyStep step, int configured, int waveNumber)
    {
        int share = Math.Clamp(configured, 0, 100);
        if (step == ArenaDifficultyStep.Reduced || waveNumber <= 1 || share == 0 || share == 100)
            return share;
        // The special waves override the growth after the exact-0/100 return above, so a user's
        // 0 % never meets a Shooters wave and 100 % never loses a shooter (ZombieWaveThemeFor
        // drops exactly those two themes). Step is Maximum here: Reduced has returned already.
        switch (ZombieScheduledTheme(step, waveNumber))
        {
            case ZombieWaveTheme.Runners: return 0;
            case ZombieWaveTheme.Shooters: return 100;
        }
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

    // ------------------------------------------------------------------------------------------
    // Tactical opening rounds (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md). The enemy
    // squad of the first two rounds is kitted lighter than the step's full gear level, so a
    // player on a starter budget does not meet the top of the tier table in round 1. One
    // function decides every round; the server band, the client pool purge and the text the
    // player reads are all defined through it and so cannot disagree.
    // ------------------------------------------------------------------------------------------

    /// <summary>Rounds that may be lighter than the step's full gear level; from the next round on the gear is full.</summary>
    public const int TacticalOpeningRounds = 2;

    /// <summary>Maximum step: the gear level (band centre) of Tactical round 1 before the clamps.</summary>
    public const int TacticalOpeningCentreRound1 = 25;

    /// <summary>Maximum step: the gear level (band centre) of Tactical round 2 before the clamps.</summary>
    public const int TacticalOpeningCentreRound2 = 45;

    /// <summary>
    /// An opening stage counts only when its centre is at least this many levels below the full
    /// level: one gear tier (the bot system's tiers are ten levels wide). A smaller gap would be
    /// a lighter round in the table and an identical kit on the field, so the round is full instead
    /// and the player is never told about a difference that is not there.
    /// </summary>
    public const int TacticalOpeningMinGap = 10;

    /// <summary>Same spread the server writes around the band centre (<c>ArenaBotTierScalePatch.Spread</c>; a test pins the equality).</summary>
    public const int GearBandSpread = 5;

    /// <summary>The mode id compare, in one place for the server band and the client purge.</summary>
    public static bool IsTacticalMode(string? mode)
        => string.Equals(mode, TacticalModeId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The raw opening centre of a Tactical round (1 or 2; any other round has no opening centre
    /// and answers <see cref="int.MaxValue"/>, "full"), before the map clamps and the minimum gap.
    /// Maximum has the two numbers above. Reduced is DERIVED from them with the same share the
    /// full level uses (<see cref="ReducedGearShare"/>, rounded away from zero: 15 and 27), so the
    /// two tables cannot drift apart when the share is tuned.
    /// </summary>
    public static int TacticalOpeningCentre(ArenaDifficultyStep step, int round)
    {
        int maximum = round switch
        {
            1 => TacticalOpeningCentreRound1,
            2 => TacticalOpeningCentreRound2,
            _ => int.MaxValue,
        };
        if (maximum == int.MaxValue || step == ArenaDifficultyStep.Maximum)
            return maximum;
        return (int)Math.Round(maximum * ReducedGearShare, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The gear level (band centre) of a Tactical round, the one function that decides it.
    /// From round 3 on, and whenever an opening centre is not at least
    /// <see cref="TacticalOpeningMinGap"/> below it, this is the step's full level
    /// (<see cref="GearLevel"/>). Otherwise it is the opening centre of the round, clamped into
    /// [the map's start level, the full level] so the start level stays a floor (the clamped
    /// start level is read through <see cref="GearLevelAt"/> at progress 0, no clamp line is
    /// copied). A round of 0 or less counts as round 1. Never above the full level, and
    /// non-decreasing in the round: a clamp is monotonic in the centre, the centres rise with the
    /// round, and a gap rule that turns round 1 into "full" turns round 2 into "full" as well.
    /// </summary>
    public static int TacticalGearLevel(ArenaDifficultyStep step, int startLevel, int endLevel, int round)
    {
        int full = GearLevel(step, startLevel, endLevel);
        if (round > TacticalOpeningRounds)
            return full;
        int low = GearLevelAt(step, startLevel, endLevel, 0d);
        int centre = Math.Clamp(TacticalOpeningCentre(step, Math.Max(1, round)), low, full);
        return full - centre < TacticalOpeningMinGap ? full : centre;
    }

    /// <summary>True when round 1 is lighter than the full level for these map numbers. Defined through <see cref="TacticalGearLevel"/>.</summary>
    public static bool TacticalOpeningDiffers(ArenaDifficultyStep step, int startLevel, int endLevel)
        => TacticalOpeningRoundsThatDiffer(step, startLevel, endLevel) > 0;

    /// <summary>
    /// How many of the rounds 1 to <see cref="TacticalOpeningRounds"/> are lighter than the full
    /// level (0, 1 or 2). A round differs exactly when <see cref="TacticalGearLevel"/> is not the
    /// full level, so the count is a prefix of the opening rounds.
    /// </summary>
    public static int TacticalOpeningRoundsThatDiffer(ArenaDifficultyStep step, int startLevel, int endLevel)
    {
        int full = GearLevel(step, startLevel, endLevel);
        int count = 0;
        for (int round = 1; round <= TacticalOpeningRounds; round++)
            if (TacticalGearLevel(step, startLevel, endLevel, round) != full)
                count++;
        return count;
    }

    /// <summary>
    /// True when a pooled profile is outside the band the server writes for this centre:
    /// below max(1, centre - <see cref="GearBandSpread"/>) or above centre + the spread.
    /// </summary>
    public static bool IsTacticalGearOutOfBand(int profileLevel, int centre)
        => profileLevel < Math.Max(1, centre - GearBandSpread) || profileLevel > centre + GearBandSpread;
}
