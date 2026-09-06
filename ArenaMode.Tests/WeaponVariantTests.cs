using System.Text.Json.Nodes;
using ArenaMode.Server.Services;
using ArenaMode.Server;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public class WeaponVariantTests
{
    [Fact]
    public void UnreadableAuthoredCatalogIsNeverRewritten()
    {
        var generator = new ArenaWeaponVariants(new JsonObject());
        const string input = "{an interrupted user edit";
        Assert.Equal(input, generator.CreateCatalog(input, 1, out var report));
        Assert.Contains("unchanged", Assert.Single(report).Detail);
    }

    [SkippableFact]
    public void RealJpScopeRingDoesNotGenerateItsSecondaryDeltaPointWithoutPrimaryScope()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        var db = JsonNode.Parse(File.ReadAllText(RepoFiles.ItemDatabase!))!.AsObject();
        const string ring = "5a37ca54c4a282000d72296a", delta = "58d268fc86f774111273f8c2";
        const string anchor = "eeeeeeeeeeeeeeeeeeeeee01";
        // A controlled anchor isolates the exact live JP+optic templates and
        // filters. The full real kit/ladder checks run in the companion harness.
        db[anchor] = new JsonObject
        {
            ["_parent"] = "5422acb9af1c889c16000029", ["_type"] = "Item", ["_props"] = new JsonObject
            {
                ["Slots"] = new JsonArray(new JsonObject
                {
                    ["_name"] = "mod_scope", ["_required"] = false, ["_props"] = new JsonObject
                    { ["filters"] = new JsonArray(new JsonObject { ["Filter"] = new JsonArray(ring) }) }
                })
            }
        };
        var nodes = new JsonArray(
            new JsonObject { ["_id"] = "eq", ["_tpl"] = "55d7217a4bdc2d86028b456d" },
            new JsonObject { ["_id"] = "gun", ["_tpl"] = anchor, ["parentId"] = "eq", ["slotId"] = "FirstPrimaryWeapon" });
        string Catalog() => new JsonArray(new JsonObject { ["id"] = "JP regression", ["items"] = nodes.DeepClone() }).ToJsonString();
        var engine = new ArenaWeaponVariants(db);
        string input = Catalog();
        for (int seed = 0; seed < 16; seed++)
        {
            string output = engine.CreateCatalog(input, seed, out _);
            var result = JsonNode.Parse(output)![0]!["items"]!.AsArray();
            Assert.True(engine.TryValidate(result, out _));
            Assert.DoesNotContain(result, item => item!["_tpl"]!.GetValue<string>() == delta);
            Assert.Equal(output, engine.CreateCatalog(input, seed, out _));
        }
        // The same native-valid arrangement can be intentionally authored.
        // Do not turn this generation-quality fix into a destructive validator.
        nodes.Add(new JsonObject { ["_id"] = "ring", ["_tpl"] = ring, ["parentId"] = "gun", ["slotId"] = "mod_scope" });
        nodes.Add(new JsonObject { ["_id"] = "dot", ["_tpl"] = delta, ["parentId"] = "ring", ["slotId"] = "mod_scope_001" });
        Assert.True(engine.TryValidate(nodes, out _));
        var preserved = JsonNode.Parse(engine.CreateCatalog(Catalog(), 2, out _))![0]!["items"]!;
        Assert.True(JsonNode.DeepEquals(nodes, preserved));
    }

    [SkippableFact]
    public void RealKitDrawIsStableAndPreservesEveryAuthoredNodeAndMetadata()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        var db = JsonNode.Parse(File.ReadAllText(RepoFiles.ItemDatabase!))!.AsObject();
        string databaseBefore = db.ToJsonString();
        var generator = new ArenaWeaponVariants(db);
        string input = File.ReadAllText(RepoFiles.Kits);
        string first = generator.CreateCatalog(input, 481, out var report);
        Assert.Equal(first, generator.CreateCatalog(input, 481, out _));
        var source = JsonNode.Parse(input)!.AsArray();
        var result = JsonNode.Parse(first)!.AsArray();
        Assert.Equal(source.Count, result.Count);
        Assert.Contains(report, x => x.Added > 0 && x.HasOptic);
        for (int i = 0; i < source.Count; i++)
        {
            foreach (var property in source[i]!.AsObject().Where(x => x.Key != "items"))
                Assert.True(JsonNode.DeepEquals(property.Value, result[i]![property.Key]));
            foreach (var item in source[i]!["items"]!.AsArray())
            {
                var copy = result[i]!["items"]!.AsArray().Single(x => x!["_id"]!.GetValue<string>() == item!["_id"]!.GetValue<string>())!.DeepClone();
                if (!JsonNode.DeepEquals(item, copy))
                {
                    Assert.Contains(item!["_tpl"]!.GetValue<string>(), new[] { "59d6507c86f7741b846413a2", "5649af094bdc2df8348b4586", "59e6449086f7746c9f75e822", "59985a8086f77414ec448d1a" });
                    Assert.Null(item["upd"]);
                    copy["_tpl"] = item["_tpl"]!.DeepClone();
                }
                Assert.True(JsonNode.DeepEquals(item, copy));
            }
        }
        Assert.Equal(databaseBefore, db.ToJsonString());
    }

    [SkippableFact]
    public void DuplicateAndOrphanNodesNeverQualifyAsValidWeaponTrees()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        var generator = new ArenaWeaponVariants(JsonNode.Parse(File.ReadAllText(RepoFiles.ItemDatabase!))!.AsObject());
        var items = RepoFiles.LoadArray(RepoFiles.Kits)[0]!["items"]!.AsArray();
        var duplicate = (JsonArray)items.DeepClone();
        duplicate.Add(items[1]!.DeepClone());
        Assert.False(generator.TryValidate(duplicate, out var reason));
        Assert.Equal("duplicate-or-missing-id", reason);
        var orphan = (JsonArray)items.DeepClone();
        orphan[1]!["parentId"] = "missing-parent";
        Assert.False(generator.TryValidate(orphan, out reason));
        Assert.Equal("orphan-or-cycle", reason);
    }

    [SkippableFact]
    public void ArmedPreloadNativeStartAndReloadKeepTheExactSessionVariant()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        var oldIndex = ArenaContext.WeaponVariants;
        string oldKits = ArenaContext.KitsJson, oldPool = ArenaContext.LadderJson, oldDraw = LadderPicker.Current;
        var oldConfig = ArenaContext.Config;
        try
        {
            ArenaContext.Config = new ArenaModeConfig();
            ArenaContext.WeaponVariants = new ArenaWeaponVariants(JsonNode.Parse(File.ReadAllText(RepoFiles.ItemDatabase!))!.AsObject());
            ArenaContext.KitsJson = File.ReadAllText(RepoFiles.Kits);
            ArenaContext.LadderJson = File.ReadAllText(RepoFiles.Ladder);
            var sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
            var session = new MongoId("0123456789abcdef01234567");
            var other = new MongoId("89abcdef0123456701234567");
            string authored = ArenaContext.KitsJson;
            Assert.Equal(authored, sessions.GetKits(session)); // ordinary observation does not generate
            string token = sessions.Arm(session, "factory4_day", "gungame");
            string preloaded = sessions.GetKits(session); // actual client order: Armed, before native start
            Assert.NotEqual(authored, preloaded);
            Assert.Equal(preloaded, sessions.GetKits(session));
            string armedLadder = sessions.GetLadder(session);
            Assert.Equal(armedLadder, sessions.GetLadder(session, redraw: true)); // cannot redraw before live
            ArenaContext.KitsJson = "[]"; // simulate ConfigStore.Reload while loading
            ArenaContext.LadderJson = "[]";
            Assert.Equal("[]", sessions.GetKits(other)); // different session never sees this player's kit
            string otherToken = sessions.Arm(other, "factory4_day", "gungame");
            Assert.Equal("[]", sessions.GetKits(other)); // second active arm owns its own source
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
                sessions.ProcessLocalRaidStart(session, "factory4_day", Protocol.BuildNativeStartIdentity(token)));
            Assert.Equal(preloaded, sessions.GetKits(session));
            Assert.Equal(armedLadder, sessions.GetLadder(session));
            Assert.NotEqual("[]", sessions.GetLadder(session, redraw: true)); // frozen source pool, not reloaded []
            Assert.Equal(preloaded, sessions.GetKits(session));
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
                sessions.ProcessLocalRaidStart(other, "factory4_day", Protocol.BuildNativeStartIdentity(otherToken)));
            Assert.Equal("[]", sessions.GetLadder(other, redraw: true));
            Assert.NotEqual("[]", sessions.GetLadder(session));
            Assert.True(sessions.BindNativeEndIdentity(session, Protocol.BuildNativeStartIdentity(token), "factory-native-end", out var endIdentity));
            Assert.True(sessions.BeginNativeEndFinalization(session, endIdentity));
            Assert.Equal("[]", sessions.GetKits(session));
            Assert.True(sessions.CompleteNativeEndFinalization(session, endIdentity));
            sessions.MarkIdle(session);
            Assert.Equal("[]", sessions.GetKits(session));
            Assert.Equal("[]", sessions.GetLadder(session));
            token = sessions.Arm(session, "factory4_day", "gungame");
            Assert.Equal("[]", sessions.GetKits(session)); // fresh arm does not inherit old variants
            Assert.True(sessions.TryBeginArmedCancellation(session, token));
            sessions.MarkIdle(session);
            string preparing = Guid.NewGuid().ToString("N");
            Assert.True(sessions.BeginArmPreparation(session, "factory4_day", "gungame", false, preparing));
            Assert.Equal("[]", sessions.GetKits(session));
            sessions.CancelArmPreparation(session);
            ArenaContext.Config.ArmedFlagTtlSeconds = 0;
            sessions.Arm(session, "factory4_day", "gungame");
            Assert.Equal("[]", sessions.GetKits(session)); // expired arm never manufactures a fresh catalogue
        }
        finally
        {
            ArenaContext.WeaponVariants = oldIndex;
            ArenaContext.KitsJson = oldKits;
            ArenaContext.LadderJson = oldPool;
            ArenaContext.Config = oldConfig;
            LadderPicker.Redraw(oldDraw, 0);
        }
    }

    [SkippableFact]
    public void ActualTypedServerTemplateAdapterMatchesTheRawDatabaseRules()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");
        string json = File.ReadAllText(RepoFiles.ItemDatabase!);
        // The same converter registrator as live JsonUtil, but local options:
        // tests must not reset the server's static serialization configuration.
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters()) options.Converters.Add(converter);
        var typed = JsonSerializer.Deserialize<Dictionary<string, TemplateItem>>(json, options)!;
        // The production adapter consumes Items only. Explicitly leave the
        // unrelated required tables unavailable so accidental reads fail here.
        var table = new TemplateTable
        {
            Items = typed.ToDictionary(x => new MongoId(x.Key), x => x.Value),
            Character = null!, CustomisationStorage = null!, Prestige = null!, Quests = null!,
            RepeatableQuests = null!, Handbook = null!, Customization = null!, Dialogue = null!,
            Profiles = null!, Prices = null!, DefaultEquipmentPresets = null!, Achievements = null!,
            CustomAchievements = null!, LocationServices = null!,
        };
        string before = JsonSerializer.Serialize(table.Items, options);
        var actualAdapter = LadderGenerator.CreateVariantIndex(table, options);
        var rawAdapter = new ArenaWeaponVariants(JsonNode.Parse(json)!.AsObject());
        string kits = File.ReadAllText(RepoFiles.Kits);
        string actual = actualAdapter.CreateCatalog(kits, 37, out var report);
        Assert.Equal(rawAdapter.CreateCatalog(kits, 37, out _), actual);
        Assert.True(report.Count(x => x.HasOptic) >= 16);
        Assert.Equal(before, JsonSerializer.Serialize(table.Items, options));
    }
}
