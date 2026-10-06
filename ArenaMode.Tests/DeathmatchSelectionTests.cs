using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class DeathmatchSelectionTests
{
    [Theory]
    [InlineData("deathmatch", "ffa")]
    [InlineData("DEATHMATCH", "all_vs_player")]
    public void CanonicalChoiceSurvivesUnchanged(string mode, string choice)
    {
        Assert.True(DeathmatchSelection.TryResolve(mode, choice, out var frozen, out var reason));
        Assert.Equal(choice, frozen);
        Assert.Empty(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("FFA")]
    [InlineData("all-vs-player")]
    [InlineData(" all_vs_player ")]
    [InlineData("teamdeathmatch")]
    public void DeathmatchNeverDefaultsMissingOrUnknownChoice(string? choice)
    {
        Assert.False(DeathmatchSelection.TryResolve(DeathmatchSelection.ModeId, choice, out var frozen, out var reason));
        Assert.Null(frozen);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("endless")]
    [InlineData("rounds")]
    [InlineData("tactical")]
    [InlineData("gungame")]
    [InlineData("teamdeathmatch")]
    [InlineData("zombie")]
    [InlineData(null)]
    public void OtherModesRequireOmissionAndRetainLegacyPolicy(string? mode)
    {
        Assert.True(DeathmatchSelection.TryResolve(mode, null, out var frozen, out var reason));
        Assert.Null(frozen);
        Assert.Empty(reason);
        foreach (var choice in new[] { "", "ffa", "all_vs_player", "unknown" })
        {
            Assert.False(DeathmatchSelection.TryResolve(mode, choice, out frozen, out reason));
            Assert.Null(frozen);
            Assert.NotEmpty(reason);
        }
    }
}
