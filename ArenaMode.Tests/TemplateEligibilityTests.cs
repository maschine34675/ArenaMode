using ArenaMode.Server.Services;

namespace ArenaMode.Tests;

/// <summary>
/// The rule that keeps BSG's unfinished duplicates out of the Gun Game ladder
/// and the attachment draw. The Cheremukha-7M shipped in a rung's cartridge
/// choices and did not fire; nothing in the numbers a generator ranks by says
/// so, only the display name does.
/// </summary>
public sealed class TemplateEligibilityTests
{
    [Theory]
    [InlineData("!!!DO_NOT_USE!!!23x75mm \"Cheremukha-7M\"")]
    [InlineData("!!!DO NOT USE!!!40x46 mm M716(Smoke)")]
    [InlineData("!!!DO_NOT_USE!!!AI AXMC 7.62x51 20 inch barrel")]
    [InlineData("something DO_NOT_USE at the end")]
    public void TheGamesOwnMarkerIsRecognisedHoweverItIsPunctuated(string name)
        => Assert.True(ArenaTemplateEligibility.IsPlaceholderName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("23x75mm Barrikada slug")]
    [InlineData("Do not use in the rain")]
    [InlineData("do_not_use")]
    public void OrdinaryNamesAndLowerCaseWarningsAreNotPlaceholders(string? name)
        // Case sensitive on purpose: the marker is upper case, and a mod item
        // whose description warns in lower case is a real item.
        => Assert.False(ArenaTemplateEligibility.IsPlaceholderName(name));

    [Fact]
    public void TheKnownPlaceholdersAreWellFormedDistinctIds()
    {
        Assert.Equal(8, ArenaTemplateEligibility.KnownPlaceholders.Count);
        Assert.Equal(ArenaTemplateEligibility.KnownPlaceholders.Count,
            ArenaTemplateEligibility.KnownPlaceholders.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in ArenaTemplateEligibility.KnownPlaceholders)
        {
            Assert.Equal(24, id.Length);
            Assert.True(id.All(Uri.IsHexDigit), $"'{id}' is not a template id");
        }

        Assert.Contains("5e85aac65505fa48730d8af2", ArenaTemplateEligibility.KnownPlaceholders);
    }

    [Fact]
    public void TheConfiguredListIsTrimmedDedupedAndNullSafe()
    {
        Assert.Empty(ArenaTemplateEligibility.ExclusionSet(null));
        Assert.Empty(ArenaTemplateEligibility.ExclusionSet(new string?[] { null, "", "   " }));

        var set = ArenaTemplateEligibility.ExclusionSet(new[] { " abc ", "abc", "def", null, "" });
        Assert.Equal(new[] { "abc", "def" }, set.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void EitherTheListOrTheNameIsEnoughToExclude()
    {
        var list = ArenaTemplateEligibility.ExclusionSet(new[] { "aaaa" });

        Assert.True(ArenaTemplateEligibility.IsExcluded("aaaa", "A perfectly fine name", list));
        Assert.True(ArenaTemplateEligibility.IsExcluded("bbbb", "!!!DO_NOT_USE!!!thing", list));
        Assert.True(ArenaTemplateEligibility.IsExcluded("bbbb", "!!!DO_NOT_USE!!!thing", null));
        Assert.False(ArenaTemplateEligibility.IsExcluded("bbbb", "A perfectly fine name", list));
        Assert.False(ArenaTemplateEligibility.IsExcluded(null, null, list));
    }
}
