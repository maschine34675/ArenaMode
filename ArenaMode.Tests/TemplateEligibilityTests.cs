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
        var seeded = ArenaTemplateEligibility.SeededExclusions.ToArray();

        Assert.Equal(seeded.OrderBy(x => x, StringComparer.Ordinal),
            ArenaTemplateEligibility.ExclusionSet(null).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(seeded.OrderBy(x => x, StringComparer.Ordinal),
            ArenaTemplateEligibility.ExclusionSet(new string?[] { null, "", "   " })
                .OrderBy(x => x, StringComparer.Ordinal));

        var set = ArenaTemplateEligibility.ExclusionSet(new[] { " abc ", "abc", "def", null, "" });
        Assert.Equal(seeded.Concat(new[] { "abc", "def" }).OrderBy(x => x, StringComparer.Ordinal),
            set.OrderBy(x => x, StringComparer.Ordinal));
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

    /// <summary>
    /// The Nightforce NXS 2.5-10x24 is unfinished without saying so: the marker
    /// rule cannot see it and it has a handbook entry, so only its id keeps it
    /// out. Fifty-eight hosts accept it in mod_scope — including the SR-25
    /// upper receiver, which is how a kit got one and then threw once per
    /// aiming frame in ScopeZoomHandler.
    /// </summary>
    [Fact]
    public void TheUnmarkedBrokenTemplatesAreWellFormedAndNamedOrdinarily()
    {
        Assert.Contains("544a3d0a4bdc2d1b388b4567", ArenaTemplateEligibility.KnownBrokenTemplates);

        foreach (var id in ArenaTemplateEligibility.KnownBrokenTemplates)
        {
            Assert.Equal(24, id.Length);
            Assert.True(id.All(Uri.IsHexDigit), $"'{id}' is not a template id");
            Assert.DoesNotContain(id, ArenaTemplateEligibility.KnownPlaceholders);
        }

        Assert.Equal(ArenaTemplateEligibility.KnownPlaceholders.Count
                     + ArenaTemplateEligibility.KnownBrokenTemplates.Count,
            ArenaTemplateEligibility.SeededExclusions.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The reason the seed is not merely a config default: the server's
    /// start-up migration adds missing config KEYS, so an installation whose
    /// excludedTemplates was written before an id existed keeps that list
    /// verbatim. Such a config must still not reach the draw.
    /// </summary>
    [Fact]
    public void AConfigWrittenBeforeTheIdWasKnownStillCannotHandItOut()
    {
        var oldConfig = ArenaTemplateEligibility.KnownPlaceholders.ToArray();
        Assert.DoesNotContain("544a3d0a4bdc2d1b388b4567", oldConfig);

        var set = ArenaTemplateEligibility.ExclusionSet(oldConfig);

        Assert.Contains("544a3d0a4bdc2d1b388b4567", set);
        Assert.True(ArenaTemplateEligibility.IsExcluded(
            "544a3d0a4bdc2d1b388b4567", "Nightforce NXS 2.5-10x24 scope", set));
        // Even handed an empty list, which is what a hand-cleared config gives.
        Assert.True(ArenaTemplateEligibility.IsExcluded(
            "544a3d0a4bdc2d1b388b4567", "Nightforce NXS 2.5-10x24 scope", Array.Empty<string>()));
    }
}
