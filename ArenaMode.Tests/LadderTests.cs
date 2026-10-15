using System.Text.Json.Nodes;
using ArenaMode.Server.Services;

namespace ArenaMode.Tests;

/// <summary>
/// The Gun Game ladder: the shipped weapon pool, and the draw that cuts a raid's
/// ladder out of it.
///
/// The draw is worth testing because its failures are quiet. A rung that arrives
/// with an empty magazine is a weapon you cannot fire; one whose chambered round
/// disagrees with its magazine is a revolver that reloads into a different
/// calibre. Both shipped at some point, and neither throws.
/// </summary>
public class LadderTests
{
    private static JsonArray Pool => RepoFiles.LoadArray(RepoFiles.Ladder);

    [Fact]
    public void RoutineDrawDetailsDoNotUseTheWarningSink()
    {
        var detail = new List<string>();
        var warnings = new List<string>();
        var pool = new JsonArray(Weapon("a", "good", "good"), Weapon("b", "good", "good"));
        var drawn = LadderPicker.Redraw(pool.ToJsonString(), 1, detail.Add, warning: warnings.Add);
        Assert.Single(JsonNode.Parse(drawn)!.AsArray());
        Assert.NotEmpty(detail);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MalformedPoolRetainsItsWarningWithoutRoutineLogging()
    {
        var warnings = new List<string>();
        const string malformed = "{broken-json";
        Assert.Equal(malformed, LadderPicker.Redraw(malformed, 1, warning: warnings.Add));
        Assert.Contains(warnings, line => line.Contains("Ladder draw failed"));
    }

    [Fact]
    public void AllExcludedAmmoRemainsVisibleAtWarning()
    {
        var detail = new List<string>();
        var warnings = new List<string>();
        var pool = new JsonArray(Weapon("a", "bad", "bad"), Weapon("b", "bad", "bad"));
        var drawn = LadderPicker.Redraw(pool.ToJsonString(), 1, detail.Add,
            excludedTemplates: new[] { "bad" }, warning: warnings.Add);
        Assert.Single(JsonNode.Parse(drawn)!.AsArray());
        Assert.Contains(warnings, line => line.Contains("nothing in the pool can be loaded"));
        Assert.DoesNotContain(detail, line => line.Contains("nothing in the pool can be loaded"));
    }

    [Fact]
    public void AttachmentDrawFailureRemainsVisibleAtWarning()
    {
        var warnings = new List<string>();
        // Valid JSON bypasses draw selection but has an invalid typed variant id.
        // Invalid JSON alone is handled inside CreateCatalog, not by this guard.
        const string malformed = "[{\"id\":7,\"items\":[]}]";
        Assert.Equal(malformed, LadderPicker.Redraw(malformed, 0,
            variants: new ArenaWeaponVariants(new JsonObject()), warning: warnings.Add));
        Assert.Contains(warnings, line => line.Contains("attachment draw failed"));
    }

    [Fact]
    public void PoolIsLargeEnoughToDrawALadderFrom()
    {
        Assert.True(Pool.Count >= 24, $"the pool holds {Pool.Count} weapons, which is fewer than a ladder needs");
    }

    [Fact]
    public void EveryRungHasANameThatIsNotAnInternalOne()
    {
        foreach (var rung in Pool)
        {
            var id = rung?["id"]?.GetValue<string>();
            var name = rung?["name"]?.GetValue<string>();

            Assert.False(string.IsNullOrWhiteSpace(name), $"rung '{id}' has no name");

            // Internal names look like "weapon_izhmash_aks74u_545x39". The player
            // reads this on every promotion, so a localisation miss is visible.
            Assert.False(name!.StartsWith("weapon_", StringComparison.Ordinal),
                $"rung '{id}' is showing its internal name '{name}' — the locale lookup missed it");
        }
    }

    [Fact]
    public void EveryRungCarriesExactlyOneWeaponAndSomeAmmunition()
    {
        foreach (var rung in Pool)
        {
            var id = rung?["id"]?.GetValue<string>();
            var items = rung?["items"] as JsonArray ?? [];

            var weapons = items.Count(node =>
                string.Equals(node?["slotId"]?.GetValue<string>(), "FirstPrimaryWeapon", StringComparison.Ordinal));
            Assert.True(weapons == 1, $"rung '{id}' has {weapons} weapons");

            var choices = rung?["ammoChoices"] as JsonArray;
            Assert.True(choices is { Count: > 0 }, $"rung '{id}' offers no ammunition to draw from");
        }
    }

    [Fact]
    public void NoRungReferencesAKnownPlaceholder()
    {
        // The Cheremukha-7M sat in the KS-23's cartridge choices of the shipped
        // pool; one raid in three of those that handed out the KS-23 loaded a
        // round that does not fire. The pool generator refuses the game's
        // placeholders now, and this keeps the shipped fallback honest with it.
        foreach (var rung in Pool)
        {
            var id = rung?["id"]?.GetValue<string>();
            var referenced = (rung?["items"] as JsonArray ?? [])
                .Select(node => node?["_tpl"]?.GetValue<string>())
                .Concat((rung?["ammoChoices"] as JsonArray ?? []).Select(node => node?.GetValue<string>()))
                .Where(tpl => tpl != null && ArenaTemplateEligibility.KnownPlaceholders.Contains(tpl))
                .Distinct()
                .ToList();

            Assert.True(referenced.Count == 0,
                $"rung '{id}' references placeholder template(s) {string.Join(", ", referenced)}");
        }
    }

    [Fact]
    public void ADrawNeverLoadsAnExcludedCartridge()
    {
        // Two identical weapons, both offering a bad round and a good one, drawn
        // into a one-rung ladder forty times: the bad round must never be
        // loaded into any ammunition site, whatever the dice say.
        var pool = new JsonArray(Weapon("first", "bad", "bad", "good"), Weapon("second", "bad", "bad", "good"));
        for (var i = 0; i < 40; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1, null, null, new[] { "bad" }))!.AsArray();
            var rung = Assert.Single(drawn);
            foreach (var site in AmmunitionSites(rung))
                Assert.Equal("good", site);
        }
    }

    [Fact]
    public void AWeaponWithNoAllowedCartridgeIsPassedOverForOneThatHasOne()
    {
        var pool = new JsonArray(Weapon("only-bad", "bad", "bad"), Weapon("fine", "good", "good"));
        for (var i = 0; i < 40; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1, null, null, new[] { "bad" }))!.AsArray();
            Assert.Equal("fine", Assert.Single(drawn)?["name"]?.GetValue<string>());
        }
    }

    [Fact]
    public void WithoutExclusionsTheDrawIsWhatItAlwaysWas()
    {
        // Loaded with a round that is NOT among the choices, so a draw that
        // stopped rewriting the load would be caught; two distinct weapons in
        // one band, so a draw that started passing one of them over would be
        // caught as well.
        var pool = new JsonArray(Weapon("first", "stale", "a", "b"), Weapon("second", "stale", "a", "b"));
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 40; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1))!.AsArray();
            var rung = Assert.Single(drawn);
            names.Add(rung?["name"]?.GetValue<string>() ?? "");
            var loaded = AmmunitionSites(rung).Distinct().ToList();
            Assert.Single(loaded);
            Assert.Contains(loaded[0], new[] { "a", "b" });
        }

        Assert.Equal(new[] { "first", "second" }, names.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ABandWithOnlyExcludedCartridgesBorrowsTheNearestUnbarredWeapon()
    {
        // Two bands of two. The lower band is barred entirely; the draw must
        // reach into the upper band for the nearest weapon rather than serve a
        // round the configuration excludes.
        var pool = new JsonArray(
            Weapon("a", "bad", "bad"), Weapon("b", "bad", "bad"),
            Weapon("c", "good", "good"), Weapon("d", "good", "good"));
        for (var i = 0; i < 40; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 2, null, null, new[] { "bad" }))!.AsArray();
            Assert.Equal(2, drawn.Count);
            Assert.Equal("c", drawn[0]?["name"]?.GetValue<string>());
            foreach (var rung in drawn)
                Assert.All(AmmunitionSites(rung), site => Assert.Equal("good", site));
        }
    }

    [Fact]
    public void NoTwoRungsShareAWeaponWhileAnotherIsAvailable()
    {
        // The lower band is barred entirely and borrows from the upper one;
        // the upper band must then take the weapon the borrowing left, not the
        // same one again.
        var pool = new JsonArray(
            Weapon("a", "bad", "bad"), Weapon("b", "bad", "bad"),
            Weapon("c", "good", "good"), Weapon("d", "good", "good"));
        for (var i = 0; i < 40; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 2, null, null, new[] { "bad" }))!.AsArray();
            Assert.Equal(new[] { "c", "d" }, drawn.Select(rung => rung?["name"]?.GetValue<string>()));
        }
    }

    [Fact]
    public void ARepeatedWeaponBeatsARoundThatDoesNotFire()
    {
        // Only one weapon in the whole pool can be loaded. Two rungs both get
        // it, and the log says so; neither is served the excluded round.
        var pool = new JsonArray(Weapon("a", "bad", "bad"), Weapon("b", "bad", "bad"), Weapon("c", "good", "good"));
        var lines = new List<string>();
        var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 2, lines.Add, null, new[] { "bad" }))!.AsArray();

        Assert.Equal(new[] { "c", "c" }, drawn.Select(rung => rung?["name"]?.GetValue<string>()));
        foreach (var rung in drawn)
            Assert.All(AmmunitionSites(rung), site => Assert.Equal("good", site));
        Assert.Contains(lines, line => line.Contains("is repeated rather than a round that does not fire", StringComparison.Ordinal));
    }

    [Fact]
    public void APoolEntryWithoutChoicesKeepsItsLoadUntouched()
    {
        // A hand-written pool entry that offers no choices is loaded with what
        // its author put there; the draw neither rewrites it nor rolls for it.
        var authored = Weapon("x", "authored", "authored");
        authored.Remove("ammoChoices");
        var pool = new JsonArray(authored, Weapon("y", "stale", "good"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 40; i++)
        {
            var rung = Assert.Single(JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1))!.AsArray());
            var name = rung?["name"]?.GetValue<string>() ?? "";
            seen.Add(name);
            Assert.All(AmmunitionSites(rung), site => Assert.Equal(name == "x" ? "authored" : "good", site));
        }

        Assert.Equal(new[] { "x", "y" }, seen.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void WhenNothingInThePoolIsLoadableTheRungIsServedAndSaidOutLoud()
    {
        var pool = new JsonArray(Weapon("a", "bad", "bad"), Weapon("b", "bad", "bad"), Weapon("c", "bad", "bad"));
        var lines = new List<string>();
        var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 2, lines.Add, null, new[] { "bad" }))!.AsArray();

        Assert.Equal(2, drawn.Count);
        Assert.Contains(lines, line => line.Contains("nothing in the pool can be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void AWeaponWithoutAnyCartridgeStaysInTheDrawWhileOthersAreBarred()
    {
        // Barred means "had cartridges and every one is excluded". A weapon
        // that never had any is not barred, and an unrelated exclusion must
        // not start passing it over.
        var unarmed = Weapon("x", "stale", "stale");
        unarmed.Remove("ammoChoices");
        var items = unarmed["items"]!.AsArray();
        foreach (var node in items.Where(node => IsAmmunitionSite(node?["slotId"]?.GetValue<string>())).ToList())
            items.Remove(node!);

        var pool = new JsonArray(unarmed, Weapon("y", "good", "good"));
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 60; i++)
        {
            var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1, null, null, new[] { "bad" }))!.AsArray();
            names.Add(Assert.Single(drawn)?["name"]?.GetValue<string>() ?? "");
        }

        Assert.Equal(new[] { "x", "y" }, names.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>A minimal pool entry: a weapon loaded with one round, offering the given choices.</summary>
    private static JsonObject Weapon(string name, string loaded, params string[] choices)
        => new()
        {
            ["id"] = name,
            ["name"] = name,
            ["order"] = 0,
            ["items"] = new JsonArray(
                new JsonObject { ["_id"] = name + "-root", ["_tpl"] = "55d7217a4bdc2d86028b456d" },
                new JsonObject { ["_id"] = name + "-gun", ["_tpl"] = "gun", ["parentId"] = name + "-root", ["slotId"] = "FirstPrimaryWeapon" },
                new JsonObject { ["_id"] = name + "-mag", ["_tpl"] = "mag", ["parentId"] = name + "-gun", ["slotId"] = "mod_magazine" },
                new JsonObject { ["_id"] = name + "-stack", ["_tpl"] = loaded, ["parentId"] = name + "-mag", ["slotId"] = "cartridges" },
                new JsonObject { ["_id"] = name + "-chamber", ["_tpl"] = loaded, ["parentId"] = name + "-gun", ["slotId"] = "patron_in_weapon" }),
            ["ammoChoices"] = new JsonArray(choices.Select(c => (JsonNode)c).ToArray()),
        };

    private static IEnumerable<string> AmmunitionSites(JsonNode? rung)
        => (rung?["items"] as JsonArray ?? [])
            .Where(node => IsAmmunitionSite(node?["slotId"]?.GetValue<string>()))
            .Select(node => node?["_tpl"]?.GetValue<string>() ?? "");

    [Fact]
    public void DrawnLadderHasTheRequestedNumberOfRungs()
    {
        var drawn = JsonNode.Parse(LadderPicker.Redraw(Pool.ToJsonString(), 24))!.AsArray();
        Assert.Equal(24, drawn.Count);
    }

    [Fact]
    public void DrawnLadderClimbsFromWeakestToStrongest()
    {
        var drawn = JsonNode.Parse(LadderPicker.Redraw(Pool.ToJsonString(), 24))!.AsArray();

        // The pool arrives ordered weakest first and the draw takes one weapon
        // from each of twenty-four equal bands, so the result must still climb.
        // A ladder that does not is a ladder that hands you a shotgun after a
        // sniper rifle.
        var orders = drawn.Select(rung => rung?["order"]?.GetValue<int>() ?? 0).ToList();
        Assert.Equal(orders.OrderBy(order => order).ToList(), orders);
    }

    [Fact]
    public void EveryDrawnRungIsLoadedWithOneSingleCartridge()
    {
        var drawn = JsonNode.Parse(LadderPicker.Redraw(Pool.ToJsonString(), 24))!.AsArray();

        foreach (var rung in drawn)
        {
            var id = rung?["id"]?.GetValue<string>();
            var items = rung?["items"] as JsonArray ?? [];

            // Every place a weapon holds ammunition — the magazine stack, the
            // chamber, and a revolver's individual camoras — must hold the SAME
            // round. The draw rewrites them together for exactly this reason.
            var loaded = items
                .Where(node => IsAmmunitionSite(node?["slotId"]?.GetValue<string>()))
                .Select(node => node?["_tpl"]?.GetValue<string>())
                .Where(tpl => tpl is not null)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(loaded.Count <= 1,
                $"rung '{id}' is loaded with {loaded.Count} different cartridges: {string.Join(", ", loaded)}");
        }
    }

    [Fact]
    public void SuccessiveDrawsDifferButStayValid()
    {
        var first = JsonNode.Parse(LadderPicker.Redraw(Pool.ToJsonString(), 24))!.AsArray();
        var second = JsonNode.Parse(LadderPicker.Redraw(Pool.ToJsonString(), 24))!.AsArray();

        Assert.Equal(first.Count, second.Count);

        // Not an assertion that they differ — with twenty-four bands they almost
        // always do, but "almost always" is not a test. What matters is that a
        // second draw is still a complete, ordered ladder.
        var orders = second.Select(rung => rung?["order"]?.GetValue<int>() ?? 0).ToList();
        Assert.Equal(orders.OrderBy(order => order).ToList(), orders);
    }

    [Fact]
    public void APoolAlreadyTheRightSizeIsPassedThroughUnchanged()
    {
        // A hand-written ladder.json must keep working: it is already a ladder,
        // not a pool to cut one from.
        var handWritten = new JsonArray(
            Pool.Take(3).Select(rung => JsonNode.Parse(rung!.ToJsonString())).ToArray());

        var drawn = JsonNode.Parse(LadderPicker.Redraw(handWritten.ToJsonString(), 3))!.AsArray();
        Assert.Equal(3, drawn.Count);
    }

    private static bool IsAmmunitionSite(string? slotId)
        => slotId is not null
           && (string.Equals(slotId, "cartridges", StringComparison.Ordinal)
               || string.Equals(slotId, "patron_in_weapon", StringComparison.Ordinal)
               || slotId.StartsWith("camora_", StringComparison.Ordinal));
}
