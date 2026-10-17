using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class AirPitMapTests
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
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist, 0)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist, 0)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 0)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 0)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 0)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 1)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2)]
    public void RecognizedDefaultsAcquireAirPitOnceWithoutResettingExistingValues(string ids, int revision)
    {
        Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, revision > 0));
        var saved = Saved(ids, revision);
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        string original = defaults.ToJsonString();
        Assert.True(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.Equal(3, saved["optionalMapsRevision"]!.GetValue<int>());
        var maps = saved["maps"]!.AsObject();
        Assert.True(maps[ArenaMapCatalog.AirPitId]!["enabled"]!.GetValue<bool>());
        Assert.Equal("BotZone_Main", maps[ArenaMapCatalog.AirPitId]!["spawnZones"]!.GetValue<string>());
        foreach (string id in ids.Split(','))
        {
            Assert.False(maps[id]!["enabled"]!.GetValue<bool>());
            Assert.Equal(3, maps[id]!["botMax"]!.GetValue<int>());
            Assert.Equal("user-zone", maps[id]!["spawnZones"]!.GetValue<string>());
        }
        Assert.Equal(original, defaults.ToJsonString());
        maps.Remove(ArenaMapCatalog.AirPitId);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.AirPitId));
        Assert.Equal(ids, ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, 3, true));
    }

    [Theory]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 1)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 2)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 3)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 4)]
    [InlineData("factory4_day,arena_equator,arena_bay5", 0)]
    [InlineData("factory4_day,arena_bay5", 2)]
    [InlineData("factory4_day,custom-map", 0)]
    [InlineData("", 0)]
    public void EditedOrPreviouslyRemovedListsDoNotAcquireAirPit(string ids, int revision)
    {
        Assert.Equal(ids, ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, true));
        var saved = Saved(ids, revision);
        for (int pass = 0; pass < 3; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            var maps = saved["maps"]!.AsObject();
            foreach (string optional in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.AirPitId })
                Assert.Equal(ids.Split(',').Contains(optional), maps.ContainsKey(optional));
        }
    }

    [Fact]
    public void LegacyBooleanAndMalformedRevisionCannotResetRemovedMaps()
    {
        Assert.Equal(ArenaMapCatalog.NativeDefaultClientAllowlist,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ArenaMapCatalog.NativeDefaultClientAllowlist, 0, true));
        var saved = Saved(ArenaMapCatalog.Bay5DefaultClientAllowlist, null);
        saved["optionalMapsRevision"] = "invalid";
        ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        Assert.False(saved["maps"]!.AsObject().ContainsKey(ArenaMapCatalog.AirPitId));
        Assert.Equal("invalid", saved["optionalMapsRevision"]!.GetValue<string>());
    }

    [Fact]
    public void DisabledAirPitKeepsValuesWithoutRecreatingOtherOptionalMaps()
    {
        var saved = Saved("factory4_day,arena_airpit", 2);
        for (int pass = 0; pass < 3; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            var config = saved.Deserialize<ArenaModeConfig>()!;
            Assert.False(config.IsMapEnabled(ArenaMapCatalog.AirPitId));
            Assert.Equal(3, config.Maps[ArenaMapCatalog.AirPitId].BotMax);
            Assert.Equal("user-zone", config.Maps[ArenaMapCatalog.AirPitId].SpawnZones);
            Assert.False(config.Maps.ContainsKey(ArenaMapCatalog.EquatorId));
            Assert.False(config.Maps.ContainsKey(ArenaMapCatalog.Bay5Id));
        }
    }

    [Fact]
    public void RealReloadPersistsAirPitAndHonorsItsRemovalOnNextReload()
    {
        string previousDir = ConfigStore.ConfigDir;
        var previousConfig = ArenaContext.Config;
        string directory = Path.Combine(Path.GetTempPath(), "arena-airpit-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigStore.Initialize(directory);
            File.WriteAllText(ConfigStore.ConfigFile, Saved(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2).ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.True(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.AirPitId));
            var saved = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Equal(3, saved["optionalMapsRevision"]!.GetValue<int>());
            saved["maps"]!.AsObject().Remove(ArenaMapCatalog.AirPitId);
            File.WriteAllText(ConfigStore.ConfigFile, saved.ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.False(ArenaContext.Config.Maps.ContainsKey(ArenaMapCatalog.AirPitId));
        }
        finally
        {
            ConfigStore.Initialize(previousDir);
            ArenaContext.Config = previousConfig;
            File.Delete(Path.Combine(directory, "config.json"));
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("arena_airpit")]
    [InlineData("ARENA_AIRPIT")]
    public void AirPitIsSelectableWithEightParticipantsAndItsOwnTeamLayout(string id)
    {
        Assert.True(ArenaMapCatalog.IsSupportedMap(id));
        Assert.Equal(ArenaMapCatalog.AirPitId, ArenaMapCatalog.CanonicalOptionalMapId(id));
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation(id, ArenaMapCatalog.IsAirPit));
        Assert.True(ArenaMapCatalog.CanSelectNativeLocation(id, ArenaMapCatalog.IsAirPit));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsAirPit));
        Assert.True(ArenaMapCatalog.RequiresModeFilteredView(id));
        Assert.False(ArenaEquatorTeamSpawns.Applies(id, "teamdeathmatch"));
        Assert.Same(ArenaTeamBoxCatalog.AirPit, ArenaTeamBoxCatalog.Find(id));
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch" })
        {
            Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(id, mode));
            Assert.True(ArenaParticipantPolicy.TryResolve(id, mode, false, false, 8, out var selected, out _));
            Assert.Equal(8, selected);
        }
    }

    [Theory]
    [InlineData("arena_airpit_extra")]
    [InlineData("arena_AirPit2")]
    [InlineData("")]
    public void SimilarNamesNeverAcquireOptionalMapAdmission(string id)
    {
        Assert.Equal(string.Empty, ArenaMapCatalog.CanonicalOptionalMapId(id));
        Assert.False(ArenaMapCatalog.IsOptionalMap(id));
        Assert.False(ArenaMapCatalog.IsSupportedMap(id));
        Assert.False(ArenaMapCatalog.CanSelectNativeLocation(id, _ => true));
        Assert.False(ArenaParticipantPolicy.TryResolve(id, "deathmatch", false, false, 8, out _, out _));
    }
}
