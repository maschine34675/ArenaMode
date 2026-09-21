using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class Bay5MapMigrationTests
{
    private static JsonObject Saved(string ids, int? revision)
    {
        var maps = new JsonObject();
        foreach (string id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            maps[id] = new JsonObject { ["enabled"] = false, ["botMax"] = 3, ["spawnZones"] = "user-zone" };
        var root = new JsonObject { ["maps"] = maps };
        if (revision.HasValue) root["optionalMapsRevision"] = revision.Value;
        return root;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void ExactEquatorMapSetGainsBay5WithoutChangingAnyExistingValues(int? revision)
    {
        var saved = Saved(ArenaMapCatalog.EquatorDefaultClientAllowlist, revision);
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig());
        string originalDefaults = defaults!.ToJsonString();
        Assert.True(ConfigStore.FillMissingKeys(saved, defaults));
        var maps = saved["maps"]!.AsObject();
        Assert.Equal(2, saved["optionalMapsRevision"]!.GetValue<int>());
        Assert.True(maps[ArenaMapCatalog.Bay5Id]!["enabled"]!.GetValue<bool>());
        Assert.Equal("BotZone_Main", maps[ArenaMapCatalog.Bay5Id]!["spawnZones"]!.GetValue<string>());
        Assert.Equal(8, maps[ArenaMapCatalog.Bay5Id]!["botMax"]!.GetValue<int>());
        Assert.Equal(4, maps[ArenaMapCatalog.Bay5Id]!["burstMinAliveBots"]!.GetValue<int>());
        foreach (string id in ArenaMapCatalog.EquatorDefaultClientAllowlist.Split(','))
        {
            Assert.False(maps[id]!["enabled"]!.GetValue<bool>());
            Assert.Equal(3, maps[id]!["botMax"]!.GetValue<int>());
            Assert.Equal("user-zone", maps[id]!["spawnZones"]!.GetValue<string>());
        }
        Assert.Equal(originalDefaults, defaults.ToJsonString());
        maps.Remove(ArenaMapCatalog.Bay5Id);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.Bay5Id));
    }

    [Theory]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 1)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 3)]
    [InlineData("factory4_day,arena_equator", 1)]
    [InlineData("factory4_day,arena_equator,custom-map", 0)]
    [InlineData("", 1)]
    public void RemovedOrCustomizedMapSetsNeverAcquireBay5AcrossReloads(string ids, int revision)
    {
        var saved = Saved(ids, revision);
        for (int pass = 0; pass < 3; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            Assert.False(saved["maps"]!.AsObject().ContainsKey(ArenaMapCatalog.Bay5Id));
            if (!ids.Split(',').Contains(ArenaMapCatalog.EquatorId))
                Assert.False(saved["maps"]!.AsObject().ContainsKey(ArenaMapCatalog.EquatorId));
            Assert.Equal(Math.Max(2, revision), saved["optionalMapsRevision"]!.GetValue<int>());
        }
    }

    [Fact]
    public void ExistingDisabledBay5KeepsItsSettingsAndEquatorRemoval()
    {
        var saved = Saved(ArenaMapCatalog.NativeDefaultClientAllowlist + "," + ArenaMapCatalog.Bay5Id, 1);
        ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        var config = saved.Deserialize<ArenaModeConfig>()!;
        Assert.False(config.IsMapEnabled(ArenaMapCatalog.Bay5Id));
        Assert.Equal("user-zone", config.Maps[ArenaMapCatalog.Bay5Id].SpawnZones);
        Assert.Equal(3, config.Maps[ArenaMapCatalog.Bay5Id].BotMax);
        Assert.False(config.Maps.ContainsKey(ArenaMapCatalog.EquatorId));
    }

    [Fact]
    public void RealServerReloadPersistsRevisionTwoAndRetainsSubsequentBay5Removal()
    {
        string oldDir = ConfigStore.ConfigDir;
        var oldConfig = ArenaContext.Config;
        string directory = Path.Combine(Path.GetTempPath(), "arena-bay5-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigStore.Initialize(directory);
            File.WriteAllText(ConfigStore.ConfigFile, Saved(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1).ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.True(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.Bay5Id));
            var saved = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Equal(2, saved["optionalMapsRevision"]!.GetValue<int>());
            saved["maps"]!.AsObject().Remove(ArenaMapCatalog.Bay5Id);
            File.WriteAllText(ConfigStore.ConfigFile, saved.ToJsonString());
            for (int pass = 0; pass < 2; pass++)
            {
                Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
                Assert.False(ArenaContext.Config.Maps.ContainsKey(ArenaMapCatalog.Bay5Id));
                Assert.False(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.EquatorId));
                Assert.Equal(3, ArenaContext.Config.Maps[ArenaMapCatalog.EquatorId].BotMax);
            }
        }
        finally
        {
            ConfigStore.Initialize(oldDir);
            ArenaContext.Config = oldConfig;
            File.Delete(Path.Combine(directory, "config.json"));
            Directory.Delete(directory);
        }
    }
}

public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData(ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id)]
    [InlineData(ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId)]
    public void OptionalTransportViewChecksEachLocationFreshAndNeverBorrowsAvailability(string present, string absent)
    {
        var oldLookup = ArenaOptionalMapPolicy.FindLocation;
        try
        {
            var location = new SPTarkov.Server.Core.Models.Eft.Common.LocationBase { Id = present, Enabled = true };
            var calls = new List<string>();
            ArenaOptionalMapPolicy.FindLocation = id => { calls.Add(id); return id == present ? location : null; };
            var config = new ArenaModeConfig();
            string before = JsonSerializer.Serialize(config);
            string locationBefore = JsonSerializer.Serialize(location);
            using var first = JsonDocument.Parse(ArenaOptionalMapPolicy.ClientConfigJson(config));
            Assert.True(first.RootElement.GetProperty("maps").TryGetProperty(present, out _));
            Assert.False(first.RootElement.GetProperty("maps").TryGetProperty(absent, out _));
            Assert.Equal(new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id }, calls);
            Assert.True(ArenaOptionalMapPolicy.IsAvailable(present.ToUpperInvariant()));
            Assert.Equal(before, JsonSerializer.Serialize(config));
            Assert.Equal(locationBefore, JsonSerializer.Serialize(location));
            location.Enabled = false;
            using var second = JsonDocument.Parse(ArenaOptionalMapPolicy.ClientConfigJson(config));
            Assert.False(second.RootElement.GetProperty("maps").TryGetProperty(present, out _));
            Assert.False(second.RootElement.GetProperty("maps").TryGetProperty(absent, out _));
            Assert.Equal(before, JsonSerializer.Serialize(config));
        }
        finally { ArenaOptionalMapPolicy.FindLocation = oldLookup; }
    }
}
