using ArenaMode.Kits;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>Pins the table and the Gun Game curve of docs/reviews/DIFFICULTY-MODEL-20261118.md.</summary>
public sealed class ArenaDifficultyPolicyTests
{
    private const ArenaDifficultyStep Max = ArenaDifficultyStep.Maximum;
    private const ArenaDifficultyStep Red = ArenaDifficultyStep.Reduced;

    [Fact]
    public void TableValuesAreThoseOfTheContract()
    {
        Assert.Equal(ArenaBotLevel.Impossible, ArenaDifficultyPolicy.PmcLevel(Max));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevel(Red));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.ScavLevel(Max));
        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevel(Red));
        Assert.Equal(4, ArenaDifficultyPolicy.PmcBonus(Max));
        Assert.Equal(2, ArenaDifficultyPolicy.PmcBonus(Red));
        Assert.Equal(0.5, ArenaDifficultyPolicy.LegacyCooldownFactor(Max));
        Assert.Equal(0.75, ArenaDifficultyPolicy.LegacyCooldownFactor(Red));
        Assert.Equal(1.0, ArenaDifficultyPolicy.BotSpeedSurplusScale(Max));
        Assert.Equal(0.5, ArenaDifficultyPolicy.BotSpeedSurplusScale(Red));
        Assert.Equal(1.0, ArenaDifficultyPolicy.ReportIntervalFactor(Max));
        Assert.Equal(1.5, ArenaDifficultyPolicy.ReportIntervalFactor(Red));
        Assert.Equal(0.6, ArenaDifficultyPolicy.ReducedGearShare);
        Assert.Equal(0.8, ArenaDifficultyPolicy.GunGamePmcStepProgress);
        Assert.Equal(0.5, ArenaDifficultyPolicy.GunGameScavStepProgress);
        Assert.Equal(79, ArenaDifficultyPolicy.MaxGearLevel);
    }

    [Theory]
    [InlineData(15, false, 15.0, 22.5)]     // the step factor comes last: map value x 1.5
    [InlineData(15, true, 7.5, 11.25)]      // Tactical halves first, then x 1.5
    [InlineData(3, true, 2.0, 3.0)]         // the Tactical two-second minimum is kept, then x 1.5
    [InlineData(1, true, 2.0, 3.0)]
    [InlineData(1, false, 1.0, 1.5)]
    public void SweepIntervalAppliesTheStepFactorAfterTheTacticalHalving(int map, bool tactical, double maximum, double reduced)
    {
        Assert.Equal(maximum, ArenaDifficultyPolicy.SweepInterval(map, tactical, Max));
        Assert.Equal(reduced, ArenaDifficultyPolicy.SweepInterval(map, tactical, Red));
    }

    [Theory]
    [InlineData(1.25, 1.25, 1.125)]
    [InlineData(1.3, 1.3, 1.15)]
    [InlineData(1.5, 1.5, 1.25)]
    [InlineData(99.0, 1.5, 1.25)]           // the user's cap of 1.5 comes before the scale
    [InlineData(1.0, 1.0, 1.0)]
    public void BotSpeedFactorCapsTheSettingBeforeScalingTheSurplus(double setting, double maximum, double reduced)
    {
        Assert.Equal(maximum, ArenaDifficultyPolicy.BotSpeedFactor(setting, Max), 6);
        Assert.Equal(reduced, ArenaDifficultyPolicy.BotSpeedFactor(setting, Red), 6);
    }

    [Fact]
    public void GearLevelIsTheMapEndLevelAtMaximumAndSixtyPercentAtReduced()
    {
        Assert.Equal(66, ArenaDifficultyPolicy.GearLevel(Max, 1, 66));
        Assert.Equal(40, ArenaDifficultyPolicy.GearLevel(Red, 1, 66));      // 39.6 rounds up
        Assert.Equal(47, ArenaDifficultyPolicy.GearLevel(Red, 1, 100));     // the end is clamped to 79 first
        Assert.Equal(47, ArenaDifficultyPolicy.GearLevel(Red, 1, 79));
        Assert.Equal(50, ArenaDifficultyPolicy.GearLevel(Red, 50, 66));     // never below the start level
        Assert.Equal(66, ArenaDifficultyPolicy.GearLevel(Red, 66, 66));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 200, 1, 79)]
    [InlineData(100, 200, 79, 79)]
    [InlineData(40, 30, 40, 40)]      // end below start behaves as end = start
    public void GearLevelNeverLeavesTheStartAndTheServerClamp(int start, int end, int expectedStart, int expectedMax)
    {
        foreach (var step in new[] { Max, Red })
        {
            for (double p = 0; p <= 1.0001; p += 0.05)
            {
                int level = ArenaDifficultyPolicy.GearLevelAt(step, start, end, p);
                Assert.InRange(level, expectedStart, expectedMax);
                Assert.InRange(level, 1, 79);
            }
        }
    }

    [Theory]
    [InlineData("maximum", true, ArenaDifficultyStep.Maximum)]
    [InlineData("reduced", true, ArenaDifficultyStep.Reduced)]
    [InlineData(null, true, ArenaDifficultyStep.Maximum)]
    [InlineData("", true, ArenaDifficultyStep.Maximum)]
    [InlineData("Maximum", false, ArenaDifficultyStep.Maximum)]
    [InlineData("REDUCED", false, ArenaDifficultyStep.Maximum)]
    [InlineData("easy", false, ArenaDifficultyStep.Maximum)]
    [InlineData(" reduced", false, ArenaDifficultyStep.Maximum)]
    [InlineData("0", false, ArenaDifficultyStep.Maximum)]
    public void TryParseDefaultsNullAndRefusesUnknownValues(string? wire, bool ok, ArenaDifficultyStep expected)
    {
        Assert.Equal(ok, ArenaDifficultyPolicy.TryParse(wire, out var step));
        Assert.Equal(expected, step);
    }

    [Fact]
    public void WireStringsRoundTrip()
    {
        Assert.Equal("maximum", ArenaDifficultyPolicy.ToWire(Max));
        Assert.Equal("reduced", ArenaDifficultyPolicy.ToWire(Red));
        foreach (var step in new[] { Max, Red })
        {
            Assert.True(ArenaDifficultyPolicy.TryParse(ArenaDifficultyPolicy.ToWire(step), out var back));
            Assert.Equal(step, back);
        }
    }

    [Theory]
    [InlineData("gungame", true)]
    [InlineData("GunGame", true)]
    [InlineData("deathmatch", false)]
    [InlineData("teamdeathmatch", false)]
    [InlineData("domination", false)]
    [InlineData("tactical", false)]
    [InlineData("zombie", false)]
    [InlineData(null, false)]
    public void OnlyGunGameScalesWithTime(string? mode, bool scales)
        => Assert.Equal(scales, ArenaDifficultyPolicy.ScalesWithTime(mode));

    [Theory]
    [InlineData("deathmatch")]
    [InlineData("teamdeathmatch")]
    [InlineData("domination")]
    [InlineData("tactical")]
    [InlineData("zombie")]
    public void EveryModeOutsideGunGameIsAtProgressOneAtAnyTime(string mode)
    {
        foreach (double elapsed in new[] { 0d, 1d, 600d, 5400d, double.NaN })
            Assert.Equal(1d, ArenaDifficultyPolicy.Progress(mode, elapsed, 15));
    }

    [Theory]
    [InlineData(ArenaDifficultyStep.Maximum)]
    [InlineData(ArenaDifficultyStep.Reduced)]
    public void NaNProgressIsTheOpeningRowForEveryLever(ArenaDifficultyStep step)
    {
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevelAt(step, double.NaN));
        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevelAt(step, double.NaN));
        Assert.Equal(0, ArenaDifficultyPolicy.PmcBonusAt(step, double.NaN));
        Assert.Equal(1, ArenaDifficultyPolicy.GearLevelAt(step, 1, 66, double.NaN));
    }

    [Fact]
    public void ReducedGearTakesSixtyPercentOfTheClampedEndLevel()
    {
        Assert.Equal(47, ArenaDifficultyPolicy.GearLevel(ArenaDifficultyStep.Reduced, 1, 100));
        Assert.Equal(79, ArenaDifficultyPolicy.GearLevel(ArenaDifficultyStep.Maximum, 1, 100));
        // The floor is the map's start level, not a fixed 40.
        Assert.Equal(30, ArenaDifficultyPolicy.GearLevel(ArenaDifficultyStep.Reduced, 1, 50));
        Assert.Equal(45, ArenaDifficultyPolicy.GearLevel(ArenaDifficultyStep.Reduced, 45, 50));
    }

    [Fact]
    public void GunGameProgressIsElapsedOverRampMinutesClamped()
    {
        Assert.Equal(0d, ArenaDifficultyPolicy.Progress("gungame", 0, 15));
        Assert.Equal(0.5, ArenaDifficultyPolicy.Progress("gungame", 450, 15));
        Assert.Equal(1d, ArenaDifficultyPolicy.Progress("gungame", 900, 15));
        Assert.Equal(1d, ArenaDifficultyPolicy.Progress("gungame", 99999, 15));
        Assert.Equal(0d, ArenaDifficultyPolicy.Progress("gungame", -3, 15));
        Assert.Equal(0d, ArenaDifficultyPolicy.Progress("gungame", double.NaN, 15));
        // A non-positive ramp must not divide by zero; it degrades to one minute.
        Assert.Equal(1d, ArenaDifficultyPolicy.Progress("gungame", 60, 0));
        Assert.Equal(0.5, ArenaDifficultyPolicy.Progress("gungame", 30, -7));
    }

    [Fact]
    public void GunGameGearRunsFromTheStartLevelToTheStepLevel()
    {
        Assert.Equal(1, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, 0));
        Assert.Equal(34, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, 0.5));    // 33.5 rounds to even, as the old ramp did
        Assert.Equal(66, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, 1));
        Assert.Equal(1, ArenaDifficultyPolicy.GearLevelAt(Red, 1, 66, 0));
        Assert.Equal(20, ArenaDifficultyPolicy.GearLevelAt(Red, 1, 66, 0.5));    // 20.5 rounds to even
        Assert.Equal(40, ArenaDifficultyPolicy.GearLevelAt(Red, 1, 66, 1));
        // Out-of-range progress cannot leave the band.
        Assert.Equal(1, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, -1));
        Assert.Equal(66, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, 7));
        Assert.Equal(1, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, double.NaN));
    }

    [Theory]
    [InlineData(ArenaDifficultyStep.Maximum)]
    [InlineData(ArenaDifficultyStep.Reduced)]
    public void GunGameCurveIsMonotone(ArenaDifficultyStep step)
    {
        int gear = 0, pmcBonus = 0;
        ArenaBotLevel pmc = ArenaBotLevel.Easy, scav = ArenaBotLevel.Easy;
        for (int i = 0; i <= 200; i++)
        {
            double p = i / 200d;
            int g = ArenaDifficultyPolicy.GearLevelAt(step, 1, 66, p);
            int b = ArenaDifficultyPolicy.PmcBonusAt(step, p);
            var pl = ArenaDifficultyPolicy.PmcLevelAt(step, p);
            var sl = ArenaDifficultyPolicy.ScavLevelAt(step, p);
            Assert.True(g >= gear, $"gear fell at {p}");
            Assert.True(b >= pmcBonus, $"bonus fell at {p}");
            Assert.True(pl >= pmc, $"pmc level fell at {p}");
            Assert.True(sl >= scav, $"scav level fell at {p}");
            (gear, pmcBonus, pmc, scav) = (g, b, pl, sl);
        }
    }

    [Fact]
    public void GunGameOpensWithHardPmcsAndNormalScavsAndStepsAtTheContractThresholds()
    {
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevelAt(Max, 0));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevelAt(Max, 0.79));
        Assert.Equal(ArenaBotLevel.Impossible, ArenaDifficultyPolicy.PmcLevelAt(Max, 0.8));
        Assert.Equal(ArenaBotLevel.Impossible, ArenaDifficultyPolicy.PmcLevelAt(Max, 1));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevelAt(Red, 0.8));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.PmcLevelAt(Red, 1));

        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevelAt(Max, 0));
        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevelAt(Max, 0.49));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.ScavLevelAt(Max, 0.5));
        Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.ScavLevelAt(Max, 1));
        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevelAt(Red, 0.5));
        Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.ScavLevelAt(Red, 1));
    }

    [Fact]
    public void GunGamePmcBonusIsRoundedProgressTimesTheStepBonus()
    {
        Assert.Equal(0, ArenaDifficultyPolicy.PmcBonusAt(Max, 0));
        Assert.Equal(1, ArenaDifficultyPolicy.PmcBonusAt(Max, 0.25));
        Assert.Equal(2, ArenaDifficultyPolicy.PmcBonusAt(Max, 0.5));
        Assert.Equal(3, ArenaDifficultyPolicy.PmcBonusAt(Max, 0.75));
        Assert.Equal(4, ArenaDifficultyPolicy.PmcBonusAt(Max, 1));
        Assert.Equal(0, ArenaDifficultyPolicy.PmcBonusAt(Red, 0.2));
        Assert.Equal(1, ArenaDifficultyPolicy.PmcBonusAt(Red, 0.5));
        Assert.Equal(2, ArenaDifficultyPolicy.PmcBonusAt(Red, 1));
        Assert.Equal(4, ArenaDifficultyPolicy.PmcBonusAt(Max, 9));
        Assert.Equal(0, ArenaDifficultyPolicy.PmcBonusAt(Max, -9));
    }

    [Fact]
    public void ProgressOneGivesTheStepValuesFromTheFirstRequestOutsideGunGame()
    {
        foreach (string mode in new[] { "deathmatch", "teamdeathmatch", "domination", "tactical" })
        {
            double p = ArenaDifficultyPolicy.Progress(mode, 0, 15);
            Assert.Equal(ArenaBotLevel.Impossible, ArenaDifficultyPolicy.BotLevel(Max, pmc: true, p));
            Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.BotLevel(Max, pmc: false, p));
            Assert.Equal(ArenaBotLevel.Hard, ArenaDifficultyPolicy.BotLevel(Red, pmc: true, p));
            Assert.Equal(ArenaBotLevel.Normal, ArenaDifficultyPolicy.BotLevel(Red, pmc: false, p));
            Assert.Equal(66, ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, p));
            Assert.Equal(40, ArenaDifficultyPolicy.GearLevelAt(Red, 1, 66, p));
            Assert.Equal(4, ArenaDifficultyPolicy.PmcBonusAt(Max, p));
            Assert.Equal(2, ArenaDifficultyPolicy.PmcBonusAt(Red, p));
        }
    }

    [Fact]
    public void RepeatedModeIdsMatchTheirOwners()
    {
        // The policy repeats these literals so harnesses linking only it still compile.
        Assert.Equal(ArenaPopulationPolicy.Zombie, ArenaDifficultyPolicy.ZombieModeId);
        Assert.Equal(ArenaPopulationPolicy.Tactical, ArenaDifficultyPolicy.TacticalModeId);
        Assert.Equal(KitPreloadPlan.GunGameModeId, ArenaDifficultyPolicy.GunGameModeId);
    }

    [Fact]
    public void PrewarmPoolsOnlyWhatIsSafeAtSecondZero()
    {
        foreach (var step in new[] { Max, Red })
        {
            Assert.Empty(ArenaDifficultyPolicy.PrewarmLevels(false, "deathmatch", true, step));
            Assert.Empty(ArenaDifficultyPolicy.PrewarmLevels(true, ArenaPopulationPolicy.Zombie, true, step));
            Assert.Empty(ArenaDifficultyPolicy.PrewarmLevels(true, "tactical", false, step));
            Assert.Equal(new[] { ArenaDifficultyPolicy.PmcLevel(step) }, ArenaDifficultyPolicy.PrewarmLevels(true, "tactical", true, step));
            Assert.Equal(new[] { ArenaDifficultyPolicy.PmcLevel(step) }, ArenaDifficultyPolicy.PrewarmLevels(true, "teamdeathmatch", false, step));
            // Gun Game's gear band ramps: only the opening level is pooled, the step's level is fetched fresh.
            Assert.Equal(new[] { ArenaBotLevel.Hard }, ArenaDifficultyPolicy.PrewarmLevels(true, "gungame", true, step));
        }
    }

    [Fact]
    public void ZombieAliveLimitIsOnlyLoweredAtReduced()
    {
        foreach (int configured in new[] { 4, 24, 25, 32, 96 })
        {
            Assert.Equal(configured, ArenaDifficultyPolicy.ZombieMaxAlive(Max, configured));
            Assert.Equal(Math.Min(configured, 24), ArenaDifficultyPolicy.ZombieMaxAlive(Red, configured));
        }
    }

    [Theory]
    [InlineData(1, 75, 75)]
    [InlineData(3, 25, 25)]
    [InlineData(4, 40, 35)]
    [InlineData(5, 20, 10)]
    [InlineData(6, 30, 15)]
    [InlineData(7, 40, 20)]
    [InlineData(8, 20, 0)]
    [InlineData(9, 30, 0)]
    [InlineData(10, 40, 5)]
    [InlineData(11, 20, 0)]
    [InlineData(int.MaxValue, 40, 0)]
    public void ZombieSlowShareFadesFromWaveFourAtMaximumOnly(int wave, int rhythm, int expected)
    {
        Assert.Equal(expected, ArenaDifficultyPolicy.ZombieSlowPercent(Max, rhythm, wave));
        Assert.Equal(rhythm, ArenaDifficultyPolicy.ZombieSlowPercent(Red, rhythm, wave));
    }

    [Theory]
    [InlineData(20, 1, 20)]
    [InlineData(20, 2, 22)]
    [InlineData(20, 6, 30)]
    [InlineData(20, 11, 40)]
    [InlineData(20, 50, 40)]
    [InlineData(37, 3, 44)]
    [InlineData(70, 11, 100)]
    [InlineData(70, int.MaxValue, 100)]
    [InlineData(0, 30, 0)]
    [InlineData(100, 30, 100)]
    public void ZombieShooterShareGrowsToTwiceTheSettingAtMaximumOnly(int configured, int wave, int expected)
    {
        Assert.Equal(expected, ArenaDifficultyPolicy.ZombieShooterPercent(Max, configured, wave));
        Assert.Equal(configured, ArenaDifficultyPolicy.ZombieShooterPercent(Red, configured, wave));
    }

    [Fact]
    public void ZombiePositionReportsAreNeverSlowedByTheStep()
    {
        Assert.Equal(Max, ArenaDifficultyPolicy.ReportStep(ArenaPopulationPolicy.Zombie, Red));
        Assert.Equal(Red, ArenaDifficultyPolicy.ReportStep("deathmatch", Red));
        Assert.Equal(Red, ArenaDifficultyPolicy.ReportStep(null, Red));
    }

    [Theory]
    [InlineData(40, 30, false)]             // exactly ten below the current level is kept
    [InlineData(40, 29, true)]              // eleven below is stale
    [InlineData(40, 40, false)]
    [InlineData(40, 79, false)]             // a profile above the ramp is never stale
    [InlineData(11, 1, false)]
    [InlineData(12, 1, true)]
    public void GunGameGearIsStaleOnlyBeyondTheSlack(int current, int profile, bool stale)
    {
        Assert.Equal(10, ArenaDifficultyPolicy.GunGameStaleGearLevels);
        Assert.Equal(stale, ArenaDifficultyPolicy.IsGunGameGearStale(profile, current));
    }

    [Fact]
    public void TheOpeningOfTheRampNeverPurgesTheOpeningProfiles()
    {
        // While the centre is at most 11, no profile of level 1..6 (the opening batch) is stale.
        for (int current = 1; current <= 11; current++)
            for (int profile = 1; profile <= 6; profile++)
                Assert.False(ArenaDifficultyPolicy.IsGunGameGearStale(profile, current));

        // The default ramp (Maximum, 1 to 66 in 15 minutes) reaches 11 after about 140 seconds.
        for (int second = 0; second <= 900; second++)
        {
            int centre = ArenaDifficultyPolicy.GearLevelAt(Max, 1, 66, second / 900d);
            if (centre <= 11)
                Assert.False(ArenaDifficultyPolicy.IsGunGameGearStale(1, centre));
        }
    }
}
