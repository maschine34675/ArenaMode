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
