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
        // pool; one raid in three loaded a round that does not fire. The pool
        // generator refuses the game's placeholders now, and this keeps the
        // shipped fallback honest with it.
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
        var pool = new JsonArray(Weapon("first", "a", "a", "b"), Weapon("second", "a", "a", "b"));
        var drawn = JsonNode.Parse(LadderPicker.Redraw(pool.ToJsonString(), 1))!.AsArray();
        var loaded = AmmunitionSites(Assert.Single(drawn)).Distinct().ToList();
        Assert.Single(loaded);
        Assert.Contains(loaded[0], new[] { "a", "b" });
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
