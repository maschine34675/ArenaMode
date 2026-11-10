using ArenaMode.Kits;
using Newtonsoft.Json;

namespace ArenaMode.Tests;

/// <summary>
/// The stock catalogue as the source of players' own kits (docs/reviews/CUSTOM-KITS-20261110.md):
/// a player copies a stock kit and edits it, so every shipped kit must copy into a custom kit the
/// structure check accepts, and the envelope the check reads from the stock kits must stay what the
/// rules assume. A kits.json edit that breaks either (a kit with a holster, a filled secure
/// container, a supply stack) fails here instead of as an unusable copy in the kit screen. The rules
/// themselves are pinned with mutants by tools/CustomKitChecks.
/// </summary>
public class CustomKitStructureTests
{
    private static List<KitDefinition> Stock()
        => JsonConvert.DeserializeObject<List<KitDefinition>>(File.ReadAllText(RepoFiles.Kits))!;

    [Fact]
    public void EveryStockKitCopiesIntoASoundCustomKit()
    {
        var stock = Stock();
        var envelope = CustomKitEnvelope.FromStock(stock);
        foreach (var kit in stock)
        {
            var copy = CustomKitEditing.CopyOf(kit, ArenaCustomKitId.NewId(_ => false), kit.Name);
            Assert.Empty(CustomKitStructure.Validate(copy, envelope));
            Assert.All(CustomKitEditing.PriceRows(copy), row => Assert.NotNull(row.Id));
        }
    }

    [Fact]
    public void TheStockEnvelopeIsWhatTheRulesAssume()
    {
        var envelope = CustomKitEnvelope.FromStock(Stock());
        Assert.InRange(envelope.MaxSpareMagazines, 1, 10);
        Assert.InRange(envelope.MaxRigEntries, 1, 8);
        Assert.Equal(1, envelope.MaxRigCount);
        Assert.Single(envelope.SecuredContainers);
        Assert.Single(envelope.Pockets);
        // The upd values an own kit may keep: the stock weapons' durability and fire mode.
        Assert.Equal(100d, envelope.MaxDurability);
        Assert.Contains("single", envelope.FireModes);
    }

    [Fact]
    public void EveryStockKitUsesOnlyTheSlotsAndUpdKeysACustomKitMay()
    {
        foreach (var kit in Stock())
        {
            string root = CustomKitStructure.RootId(kit)!;
            Assert.NotNull(root);
            foreach (var node in kit.Items)
            {
                if (node.ParentId == root)
                    Assert.Contains(node.SlotId, CustomKitStructure.RootSlots);
                foreach (var property in node.Upd?.Properties() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JProperty>())
                    Assert.Contains(property.Name, CustomKitStructure.UpdKeys);
            }
        }
    }
}
