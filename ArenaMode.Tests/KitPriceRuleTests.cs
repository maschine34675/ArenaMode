using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// The kit price rule (docs/reviews/CUSTOM-KITS-20261110.md): one implementation in
/// ArenaMode.Shared that the server's KitPricer and the client's own kits both use, and the
/// config page's "Reload from disk", which used to leave every stock kit at price 0 (Tactical
/// sold them for nothing until the next server start).
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public class KitPriceRuleTests
{
    private static KitPriceNode N(string id, string tpl, string? parent = null, string? slot = null) => new(id, tpl, parent, slot);

    private static Func<string, double?> Prices(params (string Tpl, double Price)[] prices)
    {
        var table = prices.ToDictionary(p => p.Tpl, p => p.Price, StringComparer.OrdinalIgnoreCase);
        return tpl => table.TryGetValue(tpl, out var price) ? price : null;
    }

    [Fact]
    public void EveryNodeCountsOnceAndUnknownTemplatesCountNothing()
    {
        var nodes = new[]
        {
            N("root", "equipment"),
            N("gun", "rifle", "root", "FirstPrimaryWeapon"),
            N("mag", "magazine", "gun", "mod_magazine"),
            N("ammo", "round", "mag", "cartridges"),
            N("odd", "unknown", "root", "Earpiece"),
        };
        var price = KitPriceRule.Price(nodes, Prices(("equipment", 0), ("rifle", 40000), ("magazine", 2000), ("round", 3000)));
        Assert.Equal(45000d, price);
    }

    [Fact]
    public void TheSecureContainerAndEverythingUnderItIsLeftOutAtAnyDepth()
    {
        var nodes = new[]
        {
            N("root", "equipment"),
            N("alpha", "container", "root", "SecuredContainer"),
            N("pouch", "pouch", "alpha", "main"),
            N("deep", "gold", "pouch", "main"),
            N("vest", "vest", "root", "ArmorVest"),
        };
        var price = KitPriceRule.Price(nodes, Prices(("container", 360000), ("pouch", 50000), ("gold", 90000), ("vest", 21000)));
        Assert.Equal(21000d, price);
        Assert.Equal(new[] { "alpha", "deep", "pouch" }, KitPriceRule.SecureContainerSubtree(nodes).OrderBy(x => x));
    }

    [Theory]
    [InlineData(62500d, 62000d)] // banker's rounding: the even thousand
    [InlineData(63500d, 64000d)]
    [InlineData(318400d, 318000d)]
    [InlineData(318600d, 319000d)]
    [InlineData(0d, 0d)]
    public void TheSumIsRoundedToWholeThousandsTheWayThePublishedCatalogueWas(double sum, double expected)
    {
        Assert.Equal(expected, KitPriceRule.Price(new[] { N("a", "x") }, Prices(("x", sum))));
    }

    [Fact]
    public void KitPricerPricesTheStockCatalogueWithTheSharedRuleAndKeepsAnAuthoredPrice()
    {
        var kits = RepoFiles.LoadArray(RepoFiles.Kits);
        Assert.All(kits, kit => Assert.Null(kit!["price"]));
        kits[0]!["price"] = 123456;
        var prices = Templates(kits).ToDictionary(tpl => tpl, _ => 1500d, StringComparer.OrdinalIgnoreCase);

        var priced = KitPricer.Apply(kits.ToJsonString(), prices, out var count, out var cheapest, out var dearest);

        Assert.NotNull(priced);
        var result = JsonNode.Parse(priced!)!.AsArray();
        Assert.Equal(kits.Count - 1, count);
        Assert.Equal(123456d, result[0]!["price"]!.GetValue<double>());
        for (var index = 1; index < result.Count; index++)
            Assert.Equal(Oracle(result[index]!, tpl => prices.TryGetValue(tpl, out var p) ? p : null), result[index]!["price"]!.GetValue<double>());
        Assert.True(cheapest > 0 && dearest >= cheapest);
    }

    [SkippableFact]
    public void TheRealHandbookGivesTheCatalogueThePricesThePlayerSees()
    {
        var items = RepoFiles.ItemDatabase;
        Skip.If(items is null, "no item database on this machine");
        var handbookPath = Path.Combine(Path.GetDirectoryName(items!)!, "handbook.json");
        Skip.If(!File.Exists(handbookPath), "no handbook in the item database");

        var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in JsonNode.Parse(File.ReadAllText(handbookPath))!["Items"]!.AsArray())
            prices[entry!["Id"]!.GetValue<string>()] = entry["Price"]?.GetValue<double>() ?? 0d;

        var kits = RepoFiles.LoadArray(RepoFiles.Kits);
        var priced = JsonNode.Parse(KitPricer.Apply(kits.ToJsonString(), prices, out var count, out _, out _)!)!.AsArray();

        Assert.Equal(kits.Count, count);
        foreach (var kit in priced)
            Assert.Equal(Oracle(kit!, tpl => prices.TryGetValue(tpl, out var p) ? p : null), kit!["price"]!.GetValue<double>());
        // Read off the buy screen of the native run of 2026-11-10 (GPU-2, SPT 4.1 database).
        Assert.Equal(180000d, Price(priced, "drifter"));
        Assert.Equal(669000d, Price(priced, "titan"));
    }

    [Fact]
    public void ReloadFromDiskPricesTheCatalogueAgain()
    {
        string oldDir = ConfigStore.ConfigDir, oldKits = ArenaContext.KitsJson, oldLadder = ArenaContext.LadderJson;
        var oldConfig = ArenaContext.Config;
        var oldPrices = ArenaContext.HandbookPrices;
        var dir = Path.Combine(Path.GetTempPath(), "arena-reprice-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var authored = File.ReadAllText(RepoFiles.Kits);
            File.WriteAllText(Path.Combine(dir, "kits.json"), authored);
            ConfigStore.Initialize(dir);
            var kits = RepoFiles.LoadArray(RepoFiles.Kits);
            ArenaContext.HandbookPrices = Templates(kits).ToDictionary(tpl => tpl, _ => 2500d, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out var message));

            var reloaded = JsonNode.Parse(ArenaContext.KitsJson)!.AsArray();
            Assert.Equal(kits.Count, reloaded.Count);
            Assert.All(reloaded, kit => Assert.True(kit!["price"]?.GetValue<double>() > 0, $"{kit["id"]} unpriced after reload ({message})"));

            // Before the handbook is known (the pricer has not run yet) the text stays as read.
            ArenaContext.HandbookPrices = null;
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.Equal(authored, ArenaContext.KitsJson);
        }
        finally
        {
            ConfigStore.Initialize(oldDir);
            ArenaContext.KitsJson = oldKits;
            ArenaContext.LadderJson = oldLadder;
            ArenaContext.Config = oldConfig;
            ArenaContext.HandbookPrices = oldPrices;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void RepriceNeverThrowsAndKeepsTextItCannotPrice()
    {
        var oldPrices = ArenaContext.HandbookPrices;
        try
        {
            ArenaContext.HandbookPrices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            const string broken = "[{\"id\":\"x\",\"items\":[{\"_id\":1,\"_tpl\":2}]}]";
            Assert.Equal(broken, KitPricer.Reprice(broken));
            Assert.Equal("{}", KitPricer.Reprice("{}"));
        }
        finally
        {
            ArenaContext.HandbookPrices = oldPrices;
        }
    }

    private static IEnumerable<string> Templates(JsonArray kits)
        => kits.SelectMany(kit => kit!["items"]!.AsArray()).Select(item => item!["_tpl"]!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase);

    private static double Price(JsonArray kits, string id)
        => kits.First(kit => string.Equals(kit!["id"]!.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))!["price"]!.GetValue<double>();

    /// <summary>The rule written out again by hand, recursively, as the oracle the shared code is compared with.</summary>
    private static double Oracle(JsonNode kit, Func<string, double?> price)
    {
        var items = kit["items"]!.AsArray().Select(item => item!.AsObject()).ToList();
        var children = items.Where(item => item["parentId"] != null)
            .ToLookup(item => item["parentId"]!.GetValue<string>(), StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        void Exclude(string id)
        {
            if (!excluded.Add(id)) return;
            foreach (var child in children[id])
                Exclude(child["_id"]!.GetValue<string>());
        }
        foreach (var item in items.Where(item => string.Equals(item["slotId"]?.GetValue<string>(), "SecuredContainer", StringComparison.OrdinalIgnoreCase)))
            Exclude(item["_id"]!.GetValue<string>());
        var total = items.Where(item => !excluded.Contains(item["_id"]!.GetValue<string>()))
            .Sum(item => price(item["_tpl"]!.GetValue<string>()) ?? 0d);
        return Math.Round(total / 1000d) * 1000d;
    }
}
