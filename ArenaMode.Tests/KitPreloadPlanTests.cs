using ArenaMode.Kits;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class KitPreloadPlanTests
{
    private static KitDefinition Kit(string id, params string[] templates)
    {
        var kit = new KitDefinition { Id = id, Name = id };
        kit.Items.Add(new KitItemNode { Id = "root", Tpl = "55d7217a4bdc2d86028b456d" });
        var index = 0;
        foreach (var tpl in templates)
            kit.Items.Add(new KitItemNode { Id = $"n{index++}", Tpl = tpl, ParentId = "root", SlotId = "x" });
        return kit;
    }

    private static Func<KitDefinition?> Never(string what)
        => () => throw new InvalidOperationException($"the {what} supplier must not be asked");

    [Fact]
    public void TemplatesSkipTheRootAndRepeatsButKeepTheRigContents()
    {
        var kit = Kit("k", "gun", "mag", "mag", "MAG");
        kit.Fill.RigContents.Add(new KitStackedItem { Tpl = "ammo", Count = 60 });
        kit.Fill.RigContents.Add(new KitStackedItem { Tpl = "gun" });
        kit.Fill.RigContents.Add(new KitStackedItem { Tpl = "" });

        Assert.Equal(new[] { "gun", "mag", "ammo" }, KitPreloadPlan.TemplatesOf(kit));
        Assert.Empty(KitPreloadPlan.TemplatesOf(null));
        Assert.Empty(KitPreloadPlan.TemplatesOf(new KitDefinition { Id = "bare" }));
    }

    [Fact]
    public void TheOpeningKitIsAskedTheWayTheSwapAsks()
    {
        var baseKit = Kit("base");
        var rung = Kit("rung");
        var tactical = Kit("tactical-opening");

        Assert.Same(baseKit, KitPreloadPlan.OpeningKit("endless", false, () => baseKit, Never("gun game"), Never("tactical")));
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit("rounds", false, () => baseKit, Never("gun game"), Never("tactical")));
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.TeamDeathmatch, false, () => baseKit, Never("gun game"), Never("tactical")));
        // Zombie's equipment is "Arena kits" (ArenaModeCatalog's definition),
        // the same base-kit/random-draw path Deathmatch and Team DM use — it
        // falls through the switch rather than needing its own case.
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.Zombie, false, () => baseKit, Never("gun game"), Never("tactical")));
        Assert.Same(rung, KitPreloadPlan.OpeningKit(KitPreloadPlan.GunGameModeId, false, Never("base"), () => rung, Never("tactical")));
        Assert.Same(rung, KitPreloadPlan.OpeningKit("GUNGAME", false, Never("base"), () => rung, Never("tactical")));

        // Tactical has a dedicated free starter supplier. Never draw a random
        // base kit while warming it: that draw would otherwise become free gear.
        int tacticalCalls = 0;
        foreach (var mode in new[] { ArenaPopulationPolicy.Tactical, "TACTICAL" })
            Assert.Same(tactical, KitPreloadPlan.OpeningKit(mode, false, Never("base"), Never("gun game"),
                () => { tacticalCalls++; return tactical; }));
        Assert.Equal(2, tacticalCalls);
        Assert.Null(KitPreloadPlan.OpeningKit("tactical", false, Never("base"), Never("gun game"), () => null));

        // A mode nobody recognises gets the base kit — the raid-start swap does
        // the same, because KitCatalog.GetSelectedKit falls through to it.
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(null, false, () => baseKit, Never("gun game"), Never("tactical")));
    }

    [Fact]
    public void AProtectedTeamRaidWarmsNothingAndDrawsNothing()
    {
        // The base-kit supplier DRAWS the raid's random kit. A raid that keeps
        // the PMC's entry loadout must not burn that draw, so no supplier may
        // be asked at all.
        foreach (var mode in new[] { ArenaPopulationPolicy.Tactical, "TACTICAL", KitPreloadPlan.GunGameModeId, "endless", "teamdeathmatch", ArenaPopulationPolicy.Zombie })
            Assert.Null(KitPreloadPlan.OpeningKit(mode, true, Never("base"), Never("gun game"), Never("tactical")));
    }

    [Fact]
    public void OffLoadsNothingWhateverIsAvailable()
    {
        var batches = KitPreloadPlan.Batches(KitPreloadScope.Off, Kit("a", "gun"), new[] { Kit("b", "other") }, new[] { Kit("r", "rung") });
        Assert.Empty(batches);
    }

    [Fact]
    public void TheInitialKitIsOneBatchAndOnlyThat()
    {
        var opening = Kit("a", "gun", "mag");
        var batches = KitPreloadPlan.Batches(KitPreloadScope.InitialKit, opening, new[] { opening, Kit("b", "other") }, new[] { Kit("r", "rung") });

        var batch = Assert.Single(batches);
        Assert.Equal("kit 'a'", batch.Label);
        Assert.Equal(new[] { "gun", "mag" }, batch.Templates);
    }

    [Fact]
    public void NothingToWarmMeansNoBatchRatherThanAnEmptyOne()
    {
        Assert.Empty(KitPreloadPlan.Batches(KitPreloadScope.InitialKit, null, null, null));
        Assert.Empty(KitPreloadPlan.Batches(KitPreloadScope.InitialKit, new KitDefinition { Id = "bare" }, null, null));
        Assert.Empty(KitPreloadPlan.Batches(KitPreloadScope.WholeCatalogue, null, Array.Empty<KitDefinition>(), null));
    }

    [Theory]
    [InlineData(KitPreloadScope.InitialKit)]
    [InlineData(KitPreloadScope.WholeCatalogue)]
    public void GunGameFirstThreeSelectedStagesShareMandatoryOpeningBatch(KitPreloadScope scope)
    {
        var selected = new[] { Kit("custom-z", "first"), Kit("custom-b", "second", "mag"),
            Kit("custom-x", "third", "MAG"), Kit("later", "fourth") };
        var batches = KitPreloadPlan.Batches(scope, Kit("opening", "armour", "first"), null,
            scope == KitPreloadScope.WholeCatalogue ? selected : null, selected);
        Assert.Equal(new[] { "armour", "first", "second", "mag", "third" }, batches[0].Templates);
        Assert.Contains("first 3 Gun Game rungs", batches[0].Label);
        if (scope == KitPreloadScope.WholeCatalogue)
            Assert.Equal(new[] { "fourth" }, batches[1].Templates);
        else
            Assert.Single(batches);
    }

    [Fact]
    public void GunGameShortRepeatedOrDisabledLaddersDoNotInventExtraWork()
    {
        var rung = Kit("short", "gun");
        var shortPlan = KitPreloadPlan.Batches(KitPreloadScope.InitialKit, rung, null, null, new[] { rung, rung });
        Assert.Equal(new[] { "gun" }, Assert.Single(shortPlan).Templates);
        Assert.Empty(KitPreloadPlan.Batches(KitPreloadScope.Off, rung, null, null, new[] { rung }));
        Assert.Single(KitPreloadPlan.Batches(KitPreloadScope.InitialKit, rung, null, null, Array.Empty<KitDefinition>()));
    }

    [Fact]
    public void TheWholeCatalogueFollowsOneKitAtATimeMinusWhatEarlierBatchesCover()
    {
        var opening = Kit("a", "gun", "mag");
        var catalogue = new[] { opening, Kit("b", "mag", "armour"), Kit("c", "armour", "helmet") };
        var ladder = new[] { Kit("r1", "gun"), Kit("r2", "rifle") };

        var batches = KitPreloadPlan.Batches(KitPreloadScope.WholeCatalogue, opening, catalogue, ladder);

        // One batch per kit, never one for the lot: a raid that begins while a
        // batch is in flight waits for that kit alone. A kit the earlier
        // batches already cover (the opening kit itself, rung r1) is left out.
        Assert.Equal(
            new[] { "kit 'a'", "kit 'b'", "kit 'c'", "rung 'r2'" },
            batches.Select(batch => batch.Label));
        Assert.Equal(new[] { "gun", "mag" }, batches[0].Templates);
        Assert.Equal(new[] { "armour" }, batches[1].Templates);
        Assert.Equal(new[] { "helmet" }, batches[2].Templates);
        Assert.Equal(new[] { "rifle" }, batches[3].Templates);
    }

    [Fact]
    public void TheWholeCatalogueStillLoadsWhenThereIsNoOpeningKit()
    {
        var batches = KitPreloadPlan.Batches(KitPreloadScope.WholeCatalogue, null, new[] { Kit("b", "armour"), null! }, null);

        var batch = Assert.Single(batches);
        Assert.Equal("kit 'b'", batch.Label);
        Assert.Equal(new[] { "armour" }, batch.Templates);
    }

    [Fact]
    public void ACatalogueTheOpeningKitAlreadyCoversAddsNoSecondBatch()
    {
        var opening = Kit("a", "gun", "mag");
        var batches = KitPreloadPlan.Batches(KitPreloadScope.WholeCatalogue, opening, new[] { Kit("b", "GUN", "mag") }, null);

        Assert.Single(batches);
    }
}
