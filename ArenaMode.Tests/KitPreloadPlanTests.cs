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
        var bought = Kit("bought");
        var rung = Kit("rung");

        Assert.Same(baseKit, KitPreloadPlan.OpeningKit("endless", false, () => baseKit, Never("gun game")));
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit("rounds", false, () => baseKit, Never("gun game")));
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.TeamDeathmatch, false, () => baseKit, Never("gun game")));
        Assert.Same(rung, KitPreloadPlan.OpeningKit(KitPreloadPlan.GunGameModeId, false, Never("base"), () => rung));
        Assert.Same(rung, KitPreloadPlan.OpeningKit("GUNGAME", false, Never("base"), () => rung));

        // Tactical too: its buy phase clears the pre-selected purchase before
        // the swap asks, so the first life is the base kit, never the dearest
        // affordable one. The plan once said otherwise and warmed the wrong kit.
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.Tactical, false, () => baseKit, Never("gun game")));
        Assert.NotSame(bought, KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.Tactical, false, () => baseKit, Never("gun game")));

        // A mode nobody recognises gets the base kit — the raid-start swap does
        // the same, because KitCatalog.GetSelectedKit falls through to it.
        Assert.Same(baseKit, KitPreloadPlan.OpeningKit(null, false, () => baseKit, Never("gun game")));
    }

    [Fact]
    public void AProtectedTeamRaidWarmsNothingAndDrawsNothing()
    {
        // The base-kit supplier DRAWS the raid's random kit. A raid that keeps
        // the PMC's entry loadout must not burn that draw, so no supplier may
        // be asked at all.
        Assert.Null(KitPreloadPlan.OpeningKit(ArenaPopulationPolicy.Tactical, true, Never("base"), Never("gun game")));
        Assert.Null(KitPreloadPlan.OpeningKit(KitPreloadPlan.GunGameModeId, true, Never("base"), Never("gun game")));
        Assert.Null(KitPreloadPlan.OpeningKit("endless", true, Never("base"), Never("gun game")));
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
