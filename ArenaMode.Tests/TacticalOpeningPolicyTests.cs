using System.Reflection;
using ArenaMode.Server.Patches;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// The pure policy of the Tactical opening rounds: ONE function decides every round, and the
/// disclosure and purge predicates are defined through it (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md).
/// </summary>
public sealed class TacticalOpeningPolicyTests
{
    private const ArenaDifficultyStep Max = ArenaDifficultyStep.Maximum;
    private const ArenaDifficultyStep Red = ArenaDifficultyStep.Reduced;

    private static int[] Levels(ArenaDifficultyStep step, int start, int end)
        => new[] { 1, 2, 3, 4, 9 }.Select(r => ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, r)).ToArray();

    [Fact]
    public void DefaultMapGivesTheDecidedLevelsForBothSteps()
    {
        Assert.Equal(new[] { 25, 45, 66, 66, 66 }, Levels(Max, 1, 66));
        Assert.Equal(new[] { 15, 27, 40, 40, 40 }, Levels(Red, 1, 66));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ARoundBelowOneCountsAsRoundOne(int round)
    {
        foreach (var step in new[] { Max, Red })
            Assert.Equal(
                ArenaDifficultyPolicy.TacticalGearLevel(step, 1, 66, 1),
                ArenaDifficultyPolicy.TacticalGearLevel(step, 1, 66, round));
    }

    [Fact]
    public void FromRoundThreeOnTheLevelIsTheStepsFullLevelAtAnyRound()
    {
        foreach (var step in new[] { Max, Red })
            foreach (int round in new[] { 3, 4, 50, 999, int.MaxValue })
                Assert.Equal(ArenaDifficultyPolicy.GearLevel(step, 1, 66),
                    ArenaDifficultyPolicy.TacticalGearLevel(step, 1, 66, round));
    }

    /// <summary>
    /// The derivation pin: the Reduced centres are not a second table. They are the Maximum centres
    /// scaled by ReducedGearShare and rounded away from zero, the way GearLevel scales the full level.
    /// </summary>
    [Fact]
    public void ReducedOpeningCentresAreDerivedFromTheGearShareOfTheMaximumCentres()
    {
        foreach (int round in new[] { 1, 2 })
        {
            int maximum = ArenaDifficultyPolicy.TacticalOpeningCentre(Max, round);
            Assert.Equal(
                (int)Math.Round(maximum * ArenaDifficultyPolicy.ReducedGearShare, MidpointRounding.AwayFromZero),
                ArenaDifficultyPolicy.TacticalOpeningCentre(Red, round));
        }
        Assert.Equal(25, ArenaDifficultyPolicy.TacticalOpeningCentre(Max, 1));
        Assert.Equal(45, ArenaDifficultyPolicy.TacticalOpeningCentre(Max, 2));
        Assert.Equal(15, ArenaDifficultyPolicy.TacticalOpeningCentre(Red, 1));
        Assert.Equal(27, ArenaDifficultyPolicy.TacticalOpeningCentre(Red, 2));
        // No opening centre after the opening rounds, whatever the step: "full".
        Assert.Equal(int.MaxValue, ArenaDifficultyPolicy.TacticalOpeningCentre(Max, 3));
        Assert.Equal(int.MaxValue, ArenaDifficultyPolicy.TacticalOpeningCentre(Red, 3));
    }

    [Theory]
    // Maximum: the full level is the end level. 34 - 25 = 9 is below the gap, 35 - 25 = 10 is not.
    [InlineData(Max, 1, 34, 34, 34, 34)]
    [InlineData(Max, 1, 35, 25, 35, 35)]
    [InlineData(Max, 1, 30, 30, 30, 30)]
    [InlineData(Max, 1, 20, 20, 20, 20)]
    // Round 2 has its own gap: 55 - 45 = 10 counts, 54 - 45 = 9 does not.
    [InlineData(Max, 1, 55, 25, 45, 55)]
    [InlineData(Max, 1, 54, 25, 54, 54)]
    [InlineData(Max, 1, 45, 25, 45, 45)]
    public void AnOpeningStageCountsOnlyWhenItIsAtLeastOneTierBelowTheFullLevel(
        ArenaDifficultyStep step, int start, int end, int r1, int r2, int r3)
        => Assert.Equal(new[] { r1, r2, r3 }, new[] { 1, 2, 3 }
            .Select(r => ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, r)).ToArray());

    [Fact]
    public void TheMinimumGapIsOneTierWide()
        => Assert.Equal(10, ArenaDifficultyPolicy.TacticalOpeningMinGap);

    [Fact]
    public void CustomMapsClampIntoTheirStartAndFullLevels()
    {
        // 1/45: Maximum round 2 is the full level already; Reduced full is round(0.6 x 45) = 27 = its round-2 centre.
        Assert.Equal(new[] { 25, 45, 45 }, Levels(Max, 1, 45).Take(3).ToArray());
        Assert.Equal(new[] { 15, 27, 27 }, Levels(Red, 1, 45).Take(3).ToArray());
        // 1/30: round 1 (25) is 5 below full, not a tier: every round is full. Reduced full 18.
        Assert.Equal(new[] { 30, 30, 30 }, Levels(Max, 1, 30).Take(3).ToArray());
        Assert.Equal(new[] { 18, 18, 18 }, Levels(Red, 1, 30).Take(3).ToArray());
        // 1/20: no opening at all.
        Assert.Equal(new[] { 20, 20, 20 }, Levels(Max, 1, 20).Take(3).ToArray());
        Assert.Equal(new[] { 12, 12, 12 }, Levels(Red, 1, 20).Take(3).ToArray());
        // Start 40: the start level is a floor for the opening centres (25 -> 40); Reduced full is 40 too.
        Assert.Equal(new[] { 40, 45, 66 }, Levels(Max, 40, 66).Take(3).ToArray());
        Assert.Equal(new[] { 40, 40, 40 }, Levels(Red, 40, 66).Take(3).ToArray());
        // End 100 clamps to 79 first; Reduced is 0.6 of 79.
        Assert.Equal(new[] { 25, 45, 79 }, Levels(Max, 1, 100).Take(3).ToArray());
        Assert.Equal(new[] { 15, 27, 47 }, Levels(Red, 1, 100).Take(3).ToArray());
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-5, 0, 3)]
    [InlineData(1000, 1, 1000)]
    public void NonsenseMapNumbersNeverThrowAndNeverExceedTheFullLevel(int start, int end, int round)
    {
        foreach (var step in new[] { Max, Red })
        {
            int level = ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, round);
            Assert.InRange(level, 1, ArenaDifficultyPolicy.GearLevel(step, start, end));
        }
    }

    [Fact]
    public void TheLevelNeverRisesAboveFullAndNeverFallsBetweenRounds()
    {
        int checkedCases = 0;
        foreach (var step in new[] { Max, Red })
            for (int start = 1; start <= 79; start += 6)
                for (int end = 1; end <= 100; end += 3)
                {
                    int full = ArenaDifficultyPolicy.GearLevel(step, start, end);
                    int low = ArenaDifficultyPolicy.GearLevelAt(step, start, end, 0d);
                    int previous = 0;
                    for (int round = 0; round <= 6; round++)
                    {
                        int level = ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, round);
                        Assert.True(level <= full, $"{step} {start}/{end} round {round}: {level} above {full}");
                        Assert.True(level >= low, $"{step} {start}/{end} round {round}: {level} below start {low}");
                        Assert.True(level >= previous, $"{step} {start}/{end} round {round}: {level} fell below {previous}");
                        // An opening level is either the full level or at least one tier below it.
                        Assert.True(level == full || full - level >= ArenaDifficultyPolicy.TacticalOpeningMinGap,
                            $"{step} {start}/{end} round {round}: {level} is not a full-or-tier-below level of {full}");
                        previous = level;
                        checkedCases++;
                    }
                    Assert.Equal(full, ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, 3));
                }
        Assert.True(checkedCases > 1000);
    }

    [Theory]
    [InlineData(Max, 1, 66, 2, true)]
    [InlineData(Red, 1, 66, 2, true)]
    [InlineData(Max, 1, 45, 1, true)]
    [InlineData(Red, 1, 45, 1, true)]
    [InlineData(Max, 1, 30, 0, false)]
    [InlineData(Red, 1, 30, 0, false)]
    [InlineData(Max, 1, 20, 0, false)]
    [InlineData(Max, 40, 66, 2, true)]
    [InlineData(Red, 40, 66, 0, false)]
    [InlineData(Max, 1, 100, 2, true)]
    public void DifferingRoundsAreDefinedByTheLevelFunction(ArenaDifficultyStep step, int start, int end, int count, bool differs)
    {
        Assert.Equal(count, ArenaDifficultyPolicy.TacticalOpeningRoundsThatDiffer(step, start, end));
        Assert.Equal(differs, ArenaDifficultyPolicy.TacticalOpeningDiffers(step, start, end));
        // The definition itself: a round differs exactly when its level is not the full level.
        int full = ArenaDifficultyPolicy.GearLevel(step, start, end);
        for (int round = 1; round <= ArenaDifficultyPolicy.TacticalOpeningRounds; round++)
            Assert.Equal(round <= count, ArenaDifficultyPolicy.TacticalGearLevel(step, start, end, round) != full);
    }

    [Theory]
    [InlineData(25, 20, false)]
    [InlineData(25, 19, true)]
    [InlineData(25, 30, false)]
    [InlineData(25, 31, true)]
    [InlineData(3, 1, false)]   // max(1, c - 5): the floor of the band is level 1
    [InlineData(3, 0, true)]
    [InlineData(3, 8, false)]
    [InlineData(3, 9, true)]
    public void OutOfBandIsTheBandTheServerWrites(int centre, int profileLevel, bool outOfBand)
        => Assert.Equal(outOfBand, ArenaDifficultyPolicy.IsTacticalGearOutOfBand(profileLevel, centre));

    [Fact]
    public void TheSpreadIsTheOneTheServerPatchWrites()
    {
        var spread = typeof(ArenaBotTierScalePatch).GetField("Spread", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(ArenaDifficultyPolicy.GearBandSpread, (int)spread.GetRawConstantValue()!);
    }

    [Theory]
    [InlineData("tactical", true)]
    [InlineData("TACTICAL", true)]
    [InlineData("deathmatch", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TacticalModeIsTheExactModeId(string? mode, bool tactical)
    {
        Assert.Equal(tactical, ArenaDifficultyPolicy.IsTacticalMode(mode));
        Assert.Equal("tactical", ArenaDifficultyPolicy.TacticalModeId);
    }
}
