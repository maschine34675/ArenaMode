using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public class KitSuppliesTests
{
    private const string Equipment = "55d7217a4bdc2d86028b456d";
    private const string Custom = "0123456789abcdef01234567";
    private const string Flash = "5a0c27731526d80618476ac4";
    private static TemplateItem Item(string id, ThrowWeapType? type = null) => new()
    {
        Id = new MongoId(id), Type = "Item",
        Properties = new TemplateItemProperties { ThrowType = type, MinTimeToContactExplode = -1,
            Width = 1, Height = 1, Prefab = new Prefab { Path = "test.bundle" } }
    };
    private static List<TemplateItem> Templates() => [Item(Equipment), Item(ArenaKitSupplies.Frag, ThrowWeapType.frag_grenade), Item(Flash, ThrowWeapType.flash_grenade)];
    private static string Kit(string? existing = null) => new JsonArray(new JsonObject
    {
        ["id"] = "custom", ["price"] = 9001,
        ["items"] = new JsonArray(new JsonObject { ["_id"] = "root", ["_tpl"] = Equipment }),
        ["fill"] = new JsonObject { ["spareMagazines"] = 5, ["rigContents"] = existing == null ? new JsonArray()
            : new JsonArray(new JsonObject { ["tpl"] = existing, ["count"] = 3 }) }
    }).ToJsonString();

    [Fact]
    public void OptionalRequestsAreModestAndIdempotentWithoutEditingAuthoredContents()
    {
        var service = new ArenaKitSupplies(Templates());
        string source = Kit();
        string result = service.CreateCatalog(source, out int added);
        Assert.Equal(2, added);
        var kit = JsonNode.Parse(result)![0]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source)![0]!["items"], kit["items"]));
        Assert.Equal(9001, kit["price"]!.GetValue<int>());
        Assert.Equal(5, kit["fill"]!["spareMagazines"]!.GetValue<int>());
        foreach (var entry in kit["fill"]!["rigContents"]!.AsArray())
        {
            Assert.Equal(ArenaKitSupplies.Frag, entry!["tpl"]!.GetValue<string>());
            Assert.Equal(1, entry!["count"]!.GetValue<int>());
            Assert.True(entry["optionalQuickUseOnly"]!.GetValue<bool>());
        }
        Assert.Equal(result, service.CreateCatalog(result, out added));
        Assert.Equal(0, added);
    }

    [Fact]
    public void ExistingModdedGrenadesAreKeptAndImpactReplacementIsNeverRequested()
    {
        var templates = Templates();
        templates.Add(Item(Custom, ThrowWeapType.frag_grenade));
        var service = new ArenaKitSupplies(templates);
        string source = Kit(Custom), result = service.CreateCatalog(source, out int added);
        Assert.Equal(0, added);
        Assert.Equal(source, result);
        templates[1].Properties!.MinTimeToContactExplode = 0.3;
        service = new ArenaKitSupplies(templates);
        result = service.CreateCatalog(Kit(), out added);
        Assert.Equal(0, added);
        Assert.Equal(Kit(), result);
        // A custom author's flash stays intact; the automatic supplies never add one.
        source = Kit(Flash);
        result = new ArenaKitSupplies(Templates()).CreateCatalog(source, out added);
        Assert.Equal(2, added);
        var contents = JsonNode.Parse(result)![0]!["fill"]!["rigContents"]!.AsArray();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source)![0]!["fill"]!["rigContents"]![0], contents[0]));
        Assert.All(contents.Skip(1), entry => Assert.Equal(ArenaKitSupplies.Frag, entry!["tpl"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("[]")]
    [InlineData("[{\"items\":[],\"fill\":null}]")]
    [InlineData("[{\"items\":[4],\"fill\":{\"rigContents\":[]}}]")]
    [InlineData("[{\"items\":[],\"fill\":{\"rigContents\":[false]}}]")]
    public void UnsupportedUserFormatsRemainByteIdentical(string source)
    {
        Assert.Equal(source, new ArenaKitSupplies(Templates()).CreateCatalog(source, out int count));
        Assert.Equal(0, count);
    }

    [SkippableFact]
    public void RealTwentyKitsReceiveExactlyTwoRequestsWithEveryExistingFieldPreserved()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters()) options.Converters.Add(converter);
        var templates = JsonSerializer.Deserialize<Dictionary<string, TemplateItem>>(File.ReadAllText(RepoFiles.ItemDatabase!), options)!;
        var supplies = new ArenaKitSupplies(templates.Values);
        string source = File.ReadAllText(RepoFiles.Kits), output = supplies.CreateCatalog(source, out int count);
        var before = JsonNode.Parse(source)!.AsArray();
        var after = JsonNode.Parse(output)!.AsArray();
        Assert.Equal(20, before.Count);
        Assert.Equal(40, count);
        for (int i = 0; i < before.Count; i++)
        {
            var oldKit = before[i]!.AsObject();
            var newKit = after[i]!.AsObject();
            foreach (var property in oldKit.Where(p => p.Key != "fill"))
                Assert.True(JsonNode.DeepEquals(property.Value, newKit[property.Key]));
            var oldContents = oldKit["fill"]!["rigContents"]!.AsArray();
            var newContents = newKit["fill"]!["rigContents"]!.AsArray();
            Assert.Equal(oldContents.Count + 2, newContents.Count);
            Assert.All(newContents.Skip(oldContents.Count), entry =>
            {
                Assert.Equal(ArenaKitSupplies.Frag, entry!["tpl"]!.GetValue<string>());
                Assert.Equal(1, entry["count"]!.GetValue<int>());
                Assert.True(entry["optionalQuickUseOnly"]!.GetValue<bool>());
            });
            for (int j = 0; j < oldContents.Count; j++) Assert.True(JsonNode.DeepEquals(oldContents[j], newContents[j]));
            Assert.Equal(oldKit["fill"]!["spareMagazines"]!.ToJsonString(), newKit["fill"]!["spareMagazines"]!.ToJsonString());
        }
        Assert.Equal(output, supplies.CreateCatalog(output, out count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void ArmedCatalogFreezesSuppliesButProtectedTeamAndOrdinarySessionsKeepAuthoredGear()
    {
        var oldSupplies = ArenaKitSupplies.Current;
        var oldVariants = ArenaContext.WeaponVariants;
        string oldKits = ArenaContext.KitsJson, oldPool = ArenaContext.LadderJson, oldDraw = LadderPicker.Current;
        var oldConfig = ArenaContext.Config;
        try
        {
            ArenaKitSupplies.Current = new ArenaKitSupplies(Templates());
            ArenaContext.WeaponVariants = null;
            ArenaContext.Config = new ArenaModeConfig();
            ArenaContext.KitsJson = Kit(); ArenaContext.LadderJson = "[]";
            var sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
            var player = new MongoId(Custom);
            var protectedPlayer = new MongoId("fedcba9876543210fedcba98");
            string source = ArenaContext.KitsJson;
            Assert.Equal(source, sessions.GetKits(player));
            sessions.Arm(protectedPlayer, "factory4_day", "tactical", protectPitFireTeam: true);
            Assert.Equal(source, sessions.GetKits(protectedPlayer));
            string token = sessions.Arm(player, "factory4_day", "gungame");
            string preload = sessions.GetKits(player);
            Assert.Contains("optionalQuickUseOnly", preload);
            ArenaContext.KitsJson = "[]";
            Assert.Equal(preload, sessions.GetKits(player));
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
                sessions.ProcessLocalRaidStart(player, "factory4_day", Protocol.BuildNativeStartIdentity(token)));
            Assert.Equal(preload, sessions.GetKits(player));
            Assert.Equal(source, sessions.GetKits(protectedPlayer));
            sessions.MarkIdle(player);
            Assert.Equal("[]", sessions.GetKits(player));
            sessions.Arm(player, "factory4_day", "tactical");
            Assert.Equal("[]", sessions.GetKits(player));
        }
        finally
        {
            ArenaKitSupplies.Current = oldSupplies;
            ArenaContext.WeaponVariants = oldVariants;
            ArenaContext.KitsJson = oldKits; ArenaContext.LadderJson = oldPool;
            ArenaContext.Config = oldConfig; LadderPicker.Redraw(oldDraw, 0);
        }
    }
}
