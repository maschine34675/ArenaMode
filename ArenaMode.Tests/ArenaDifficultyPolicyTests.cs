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
    [InlineData("zombies", false)]
    [InlineData(null, false)]
    public void OnlyGunGameScalesWithTime(string? mode, bool scales)
        => Assert.Equal(scales, ArenaDifficultyPolicy.ScalesWithTime(mode));

    [Theory]
    [InlineData("deathmatch")]
    [InlineData("teamdeathmatch")]
    [InlineData("domination")]
    [InlineData("tactical")]
    [InlineData("zombies")]
    public void EveryModeOutsideGunGameIsAtProgressOneAtAnyTime(string mode)
    {
        foreach (double elapsed in new[] { 0d, 1d, 600d, 5400d, double.NaN })
            Assert.Equal(1d, ArenaDifficultyPolicy.Progress(mode, elapsed, 15));
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
}
