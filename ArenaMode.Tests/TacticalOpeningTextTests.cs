using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// The one line the Tactical buy screens say about the enemy squad's gear
/// (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md): three decided texts, at most 45 characters, and
/// only what the round's gear level really is (it is defined through TacticalGearLevel, so it cannot
/// claim a stage the server band and the pool purge are not in).
/// </summary>
public sealed class TacticalOpeningTextTests
{
    private const ArenaDifficultyStep Max = ArenaDifficultyStep.Maximum;
    private const ArenaDifficultyStep Red = ArenaDifficultyStep.Reduced;

    private static string Say(ArenaDifficultyStep step, int start, int end, int round, bool pmc = true)
        => TacticalOpeningText.Note(step, start, end, pmc, round);

    [Fact]
    public void TheThreeTextsAreTheDecidedOnesAndFitTheLabel()
    {
        Assert.Equal("Enemy squad this round: light gear", TacticalOpeningText.Light);
        Assert.Equal("Enemy squad this round: medium gear", TacticalOpeningText.Medium);
        Assert.Equal("Enemy squad from now on: full gear", TacticalOpeningText.FullFromNowOn);
        Assert.Equal(45, TacticalOpeningText.MaxLength);
        foreach (var text in new[] { TacticalOpeningText.Light, TacticalOpeningText.Medium, TacticalOpeningText.FullFromNowOn })
            Assert.InRange(text.Length, 1, TacticalOpeningText.MaxLength);
    }

    [Theory]
    [InlineData(ArenaDifficultyStep.Maximum)]
    [InlineData(ArenaDifficultyStep.Reduced)]
    public void TheDefaultMapSaysLightMediumThenFullFromNowOnThenNothing(ArenaDifficultyStep step)
    {
        Assert.Equal(TacticalOpeningText.Light, Say(step, 1, 66, 1));
        Assert.Equal(TacticalOpeningText.Medium, Say(step, 1, 66, 2));
        Assert.Equal(TacticalOpeningText.FullFromNowOn, Say(step, 1, 66, 3));
        foreach (int round in new[] { 4, 5, 20, 999 })
            Assert.Equal(string.Empty, Say(step, 1, 66, round));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ARoundBelowOneSaysNothing(int round)
        => Assert.Equal(string.Empty, Say(Max, 1, 66, round));

    [Fact]
    public void AScavSquadIsToldNothingInAnyRound()
    {
        foreach (var step in new[] { Max, Red })
            for (int round = 0; round <= 6; round++)
                Assert.Equal(string.Empty, Say(step, 1, 66, round, pmc: false));
    }

    [Theory]
    [InlineData(1, 45, 1, TacticalOpeningText.Light)]
    [InlineData(1, 45, 2, TacticalOpeningText.FullFromNowOn)]
    [InlineData(1, 45, 3, "")]
    [InlineData(40, 66, 1, TacticalOpeningText.Light)]
    [InlineData(40, 66, 2, TacticalOpeningText.Medium)]
    [InlineData(40, 66, 3, TacticalOpeningText.FullFromNowOn)]
    [InlineData(1, 30, 1, "")]
    [InlineData(1, 30, 2, "")]
    [InlineData(1, 30, 3, "")]
    [InlineData(1, 20, 1, "")]
    [InlineData(1, 20, 3, "")]
    public void ACustomMapSaysWhatItsOwnLevelsAre(int start, int end, int round, string expected)
        => Assert.Equal(expected, Say(Max, start, end, round));

    [Fact]
    public void AStepWhoseFullLevelIsTheStartLevelSaysNothing()
    {
        for (int round = 1; round <= 4; round++)
            Assert.Equal(string.Empty, Say(Red, 40, 66, round));
    }

    /// <summary>
    /// The text is defined through the policy: over a sweep of maps and steps, light is round 1 only,
    /// medium round 2 only, "full from now on" exactly once and only after a lighter round, and every map
    /// the policy says has no lighter round says nothing at all.
    /// </summary>
    [Fact]
    public void TheTextAgreesWithThePolicyOnEveryMap()
    {
        foreach (var step in new[] { Max, Red })
            foreach (int start in new[] { 1, 10, 40, 79 })
                foreach (int end in new[] { 10, 20, 30, 34, 45, 50, 66, 79, 100 })
                {
                    int lighter = ArenaDifficultyPolicy.TacticalOpeningRoundsThatDiffer(step, start, end);
                    int full = 0, light = 0, medium = 0;
                    for (int round = 1; round <= 8; round++)
                    {
                        string line = Say(step, start, end, round);
                        if (line == TacticalOpeningText.Light) { light++; Assert.Equal(1, round); }
                        else if (line == TacticalOpeningText.Medium) { medium++; Assert.Equal(2, round); }
                        else if (line == TacticalOpeningText.FullFromNowOn) { full++; Assert.Equal(lighter + 1, round); }
                        else Assert.Equal(string.Empty, line);
                    }

                    Assert.Equal(lighter >= 1 ? 1 : 0, light);
                    Assert.Equal(lighter >= 2 ? 1 : 0, medium);
                    Assert.Equal(lighter >= 1 ? 1 : 0, full);
                }
    }
}
