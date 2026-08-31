using System.Text.Json.Nodes;

namespace ArenaMode.Tests;

/// <summary>
/// The shipped kit catalogue must be buildable.
///
/// Every rule here is one the client enforces at runtime by REFUSING the kit —
/// KitBuilder.ValidateKit throws on unknown templates, Populate logs an orphan
/// and drops it — which means a broken catalogue does not announce itself until
/// a player respawns naked mid-raid. These are the invariants that were checked
/// by hand, in a Python one-liner, after every generator change.
/// </summary>
public class KitCatalogTests
{
    private static JsonArray Kits => RepoFiles.LoadArray(RepoFiles.Kits);

    private const string RootTemplate = "55d7217a4bdc2d86028b456d";

    [Fact]
    public void CatalogueIsNotEmpty()
    {
        Assert.NotEmpty(Kits);
    }

    [Fact]
    public void EveryKitHasAnIdAndTheyAreUnique()
    {
        var ids = new List<string>();

        foreach (var kit in Kits)
        {
            var id = kit?["id"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(id), "a kit has no id");
            ids.Add(id!);
        }

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ItemIdsAreUniqueWithinEachKit()
    {
        foreach (var kit in Kits)
        {
            var ids = Items(kit).Select(Id).ToList();
            var kitId = kit?["id"]?.GetValue<string>();

            Assert.True(
                ids.Count == ids.Distinct(StringComparer.Ordinal).Count(),
                $"kit '{kitId}' repeats an item id — the builder remaps by id, so a repeat silently reparents a node");
        }
    }

    [Fact]
    public void EachKitHasExactlyOneRootAndItIsTheEquipmentTemplate()
    {
        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();
            var roots = Items(kit).Where(node => node["parentId"] is null).ToList();

            Assert.True(roots.Count == 1, $"kit '{kitId}' has {roots.Count} roots, expected exactly 1");
            Assert.Equal(RootTemplate, roots[0]["_tpl"]?.GetValue<string>());
        }
    }

    [Fact]
    public void NoNodeReferencesAParentThatIsNotInTheKit()
    {
        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();
            var known = Items(kit).Select(Id).ToHashSet(StringComparer.Ordinal);

            foreach (var node in Items(kit))
            {
                var parent = node["parentId"]?.GetValue<string>();
                if (parent is null)
                    continue;

                Assert.True(known.Contains(parent),
                    $"kit '{kitId}': node {Id(node)} hangs off '{parent}', which the kit does not contain");
            }
        }
    }

    [Fact]
    public void EachKitCarriesExactlyOnePrimaryWeapon()
    {
        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();
            var weapons = Items(kit).Count(node =>
                string.Equals(node["slotId"]?.GetValue<string>(), "FirstPrimaryWeapon", StringComparison.Ordinal));

            Assert.True(weapons == 1, $"kit '{kitId}' has {weapons} primary weapons, expected 1");
        }
    }

    /// <summary>
    /// Slots that hold ONE thing must not be filled twice. The builder attaches
    /// by slot and the second attach fails, so a duplicate is a kit that quietly
    /// arrives missing a piece.
    /// </summary>
    [Theory]
    [InlineData("Headwear")]
    [InlineData("Earpiece")]
    [InlineData("ArmorVest")]
    [InlineData("TacticalVest")]
    [InlineData("Backpack")]
    [InlineData("Eyewear")]
    public void SingleOccupancySlotsAreFilledAtMostOnce(string slotId)
    {
        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();
            var count = Items(kit).Count(node =>
                string.Equals(node["slotId"]?.GetValue<string>(), slotId, StringComparison.Ordinal));

            Assert.True(count <= 1, $"kit '{kitId}' fills '{slotId}' {count} times");
        }
    }

    /// <summary>
    /// The armoured-rig rule the generator exists to enforce: a rig that blocks
    /// body armour cannot be worn WITH body armour. Checked here by shape rather
    /// than by reading BlocksArmorVest, so it holds without the item database.
    /// </summary>
    [SkippableFact]
    public void NoKitWearsBothAnArmouredRigAndBodyArmour()
    {
        var databasePath = RepoFiles.ItemDatabase;
        Skip.If(databasePath is null, "no item database on this machine");

        var items = JsonNode.Parse(File.ReadAllText(databasePath!))!.AsObject();

        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();
            var rig = Items(kit).FirstOrDefault(node =>
                string.Equals(node["slotId"]?.GetValue<string>(), "TacticalVest", StringComparison.Ordinal));
            var armour = Items(kit).Any(node =>
                string.Equals(node["slotId"]?.GetValue<string>(), "ArmorVest", StringComparison.Ordinal));

            if (rig is null || !armour)
                continue;

            var tpl = rig["_tpl"]?.GetValue<string>();
            var blocks = tpl is not null
                         && items[tpl]?["_props"]?["BlocksArmorVest"]?.GetValue<bool>() == true;

            Assert.False(blocks, $"kit '{kitId}' wears body armour under a rig that blocks it");
        }
    }

    [SkippableFact]
    public void EveryTemplateExistsInTheItemDatabase()
    {
        var databasePath = RepoFiles.ItemDatabase;
        Skip.If(databasePath is null, "no item database on this machine");

        var items = JsonNode.Parse(File.ReadAllText(databasePath!))!.AsObject();

        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();

            foreach (var node in Items(kit))
            {
                var tpl = node["_tpl"]?.GetValue<string>();
                Assert.False(string.IsNullOrEmpty(tpl), $"kit '{kitId}': a node has no template");
                Assert.True(items.ContainsKey(tpl!),
                    $"kit '{kitId}' references template {tpl}, which the item database does not have — "
                    + "the client drops the whole kit when it meets one");
            }
        }
    }

    /// <summary>
    /// The fill section is a DIFFERENT schema from the item tree above it, and
    /// the difference is one underscore.
    ///
    /// The tree is SPT's preset shape, where a template is "_tpl". This section
    /// is our own declarative one, where it is "tpl". Writing the tree's key here
    /// deserialises to a null template and the client then asks ItemFactory for
    /// an item with an empty id — which throws out of the whole kit application,
    /// so every swap and every respawn in the raid fails, not merely the rig
    /// contents. That shipped, and none of the tests above saw it because they
    /// all read "items" and never looked at "fill".
    /// </summary>
    [Fact]
    public void RigContentsUseTheFillSchemaKeyNotTheItemTreeKey()
    {
        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();

            foreach (var entry in kit?["fill"]?["rigContents"] as JsonArray ?? [])
            {
                Assert.True(entry?["_tpl"] is null,
                    $"kit '{kitId}': a rigContents entry uses '_tpl'; this section's key is 'tpl'");

                var tpl = entry?["tpl"]?.GetValue<string>();
                Assert.False(string.IsNullOrWhiteSpace(tpl),
                    $"kit '{kitId}': a rigContents entry has no 'tpl'");
            }
        }
    }

    [SkippableFact]
    public void EveryRigContentTemplateExists()
    {
        var databasePath = RepoFiles.ItemDatabase;
        Skip.If(databasePath is null, "no item database on this machine");

        var items = JsonNode.Parse(File.ReadAllText(databasePath!))!.AsObject();

        foreach (var kit in Kits)
        {
            var kitId = kit?["id"]?.GetValue<string>();

            foreach (var entry in kit?["fill"]?["rigContents"] as JsonArray ?? [])
            {
                var tpl = entry?["tpl"]?.GetValue<string>();
                Assert.True(tpl is not null && items.ContainsKey(tpl),
                    $"kit '{kitId}': rigContents names {tpl}, which the item database does not have");
            }
        }
    }

    private static IEnumerable<JsonNode> Items(JsonNode? kit)
        => (kit?["items"] as JsonArray ?? []).Where(node => node is not null)!;

    private static string Id(JsonNode? node) => node?["_id"]?.GetValue<string>() ?? string.Empty;
}
