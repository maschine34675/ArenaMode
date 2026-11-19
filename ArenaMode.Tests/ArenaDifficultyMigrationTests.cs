using ArenaMode.Config;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// The one-time move of the retired [Difficulty] keys to the Step setting: only a switched-off
/// scaling or a bot cap below impossible selects Reduced; everything else keeps the default.
/// </summary>
public sealed class ArenaDifficultyMigrationTests
{
    [Theory]
    [InlineData("false", null, true)]
    [InlineData("False", "impossible", true)]
    [InlineData("true", "hard", true)]
    [InlineData(null, "normal", true)]
    [InlineData(null, "EASY", true)]
    [InlineData("true", "impossible", false)]
    [InlineData("true", null, false)]
    [InlineData(null, null, false)]
    [InlineData("maybe", "unknown", false)]
    public void LegacyValuesSelectReducedOnlyWhenTheyMadeTheArenaEasier(string? scale, string? cap, bool reduced)
        => Assert.Equal(reduced ? ArenaDifficultyStep.Reduced : (ArenaDifficultyStep?)null,
            ArenaDifficultyMigration.StepFromLegacy(scale, cap));

    [Fact]
    public void TheConfigTextIsReadFromTheDifficultySectionOnly()
    {
        const string cfg = "## Settings file\r\n[General]\r\nScaleDifficulty = false\r\n\r\n[Difficulty]\r\n"
            + "# ScaleDifficulty = false\r\nScaleDifficulty = true\r\nMaxBotDifficulty = hard\r\n\r\n[Bots]\r\nPmcFloor = 8\r\n";
        Assert.Equal(ArenaDifficultyStep.Reduced, ArenaDifficultyMigration.StepFromConfigText(cfg));
        Assert.Null(ArenaDifficultyMigration.StepFromConfigText(
            "[General]\r\nScaleDifficulty = false\r\n[Difficulty]\r\nScaleDifficulty = true\r\nMaxBotDifficulty = impossible\r\n"));
        Assert.Null(ArenaDifficultyMigration.StepFromConfigText(null));
        Assert.Null(ArenaDifficultyMigration.StepFromConfigText(""));
    }

    [Fact]
    public void AnOldDefaultConfigurationKeepsMaximum()
        => Assert.Null(ArenaDifficultyMigration.StepFromConfigText(
            "[Difficulty]\n\nScaleDifficulty = true\n\nPressureRampMinutes = 12\n\nMaxBotDifficulty = impossible\n"));
}