using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class IcebergMapTests
{
    private static JsonObject Saved(string ids, int revision)
    {
        var maps = new JsonObject();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            maps[id] = new JsonObject { ["enabled"] = false, ["botMax"] = 3, ["spawnZones"] = "user-zone" };
        return new JsonObject { ["maps"] = maps, ["optionalMapsRevision"] = revision };
    }

    // Revision 5 extended the exact Bowl default with Iceberg; revision 6 extends the
    // exact Iceberg default with Chop Shop (ChopShopMapTests). Every earlier default
    // keeps its own cutoff, and the AirPit default at revision 4 is a Bowl removal.
    [Theory]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 4, true)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 3, true)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2, true)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1, true)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 5, false)]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 6, false)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 4, false)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 3, false)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2, false)]
    [InlineData("factory4_day,arena_bowl", 4, false)]
    [InlineData("factory4_day,arena_bowl,arena_iceberg", 4, false)]
    [InlineData("factory4_day,custom-map", 0, false)]
    public void MigrationOnlyExtendsRecognizedHistoricalDefaults(string ids, int revision, bool gain)
    {
        Assert.Equal(gain ? ArenaMapCatalog.DefaultClientAllowlist : ids,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, revision > 0));
        var saved = Saved(ids, revision);
        var maps = saved["maps"]!.AsObject();
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        var defaultsBefore = defaults.ToJsonString();
        bool hadBowl = ids.Split(',').Contains(ArenaMapCatalog.BowlId);
        bool hadIceberg = ids.Split(',').Contains(ArenaMapCatalog.IcebergId);
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, defaults);
            Assert.Equal(gain || hadIceberg, maps.ContainsKey(ArenaMapCatalog.IcebergId));
            // A gain brings the complete current default; a refusal adds no optional map.
            Assert.Equal(gain || hadBowl, maps.ContainsKey(ArenaMapCatalog.BowlId));
            foreach (var id in ids.Split(','))
            {
                Assert.False(maps[id]!["enabled"]!.GetValue<bool>());
                Assert.Equal(3, maps[id]!["botMax"]!.GetValue<int>());
                Assert.Equal("user-zone", maps[id]!["spawnZones"]!.GetValue<string>());
            }
            Assert.Equal(Math.Max(ArenaMapCatalog.OptionalMapsRevision, revision),
                saved["optionalMapsRevision"]!.GetValue<int>());
            Assert.Equal(defaultsBefore, defaults.ToJsonString());
        }
        if (!gain) return;
        var iceberg = saved.Deserialize<ArenaModeConfig>()!.Maps[ArenaMapCatalog.IcebergId];
        Assert.True(iceberg.Enabled);
        Assert.Equal("BotZone_Main", iceberg.SpawnZones);
        Assert.Equal(8, iceberg.BotMax);
        Assert.Equal(8, iceberg.MaxBotPerZone);
        Assert.Equal(4, iceberg.BurstMinAliveBots);
        // Once upgraded, deleting Iceberg must survive the next repair, even though
        // the remaining map set again looks exactly like the Bowl default.
        maps.Remove(ArenaMapCatalog.IcebergId);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.IcebergId));
        Assert.Equal(ArenaMapCatalog.BowlDefaultClientAllowlist, ArenaMapCatalog.UpgradeOptionalClientAllowlist(
            ArenaMapCatalog.BowlDefaultClientAllowlist, ArenaMapCatalog.OptionalMapsRevision, true));
    }

    [Fact]
    public void ExistingDisabledIcebergAndMalformedRevisionStayUserOwned()
    {
        var saved = JsonNode.Parse("""
            {"optionalMapsRevision":"invalid","maps":{
              "arena_iceberg":{"enabled":false,"botMax":3,"spawnZones":"my-zone"}}}
            """)!;
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            Assert.False(saved["maps"]![ArenaMapCatalog.IcebergId]!["enabled"]!.GetValue<bool>());
            Assert.Equal(3, saved["maps"]![ArenaMapCatalog.IcebergId]!["botMax"]!.GetValue<int>());
            Assert.Equal("my-zone", saved["maps"]![ArenaMapCatalog.IcebergId]!["spawnZones"]!.GetValue<string>());
            Assert.Equal("invalid", saved["optionalMapsRevision"]!.GetValue<string>());
            Assert.Null(saved["maps"]![ArenaMapCatalog.BowlId]);
            Assert.Null(saved["maps"]![ArenaMapCatalog.AirPitId]);
        }
        // A malformed marker over the exact former default never re-enables a map.
        var bowlDefault = Saved(ArenaMapCatalog.BowlDefaultClientAllowlist, 0);
        bowlDefault["optionalMapsRevision"] = "invalid";
        ConfigStore.FillMissingKeys(bowlDefault, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        Assert.Null(bowlDefault["maps"]![ArenaMapCatalog.IcebergId]);
    }

    [Fact]
    public void RealReloadPersistsIcebergAndHonorsItsRemovalOnNextReload()
    {
        string previousDir = ConfigStore.ConfigDir;
        var previousConfig = ArenaContext.Config;
        string directory = Path.Combine(Path.GetTempPath(), "arena-iceberg-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigStore.Initialize(directory);
            File.WriteAllText(ConfigStore.ConfigFile, Saved(ArenaMapCatalog.BowlDefaultClientAllowlist, 4).ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.True(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.IcebergId));
            Assert.False(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.BowlId));
            var saved = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, saved["optionalMapsRevision"]!.GetValue<int>());
            saved["maps"]!.AsObject().Remove(ArenaMapCatalog.IcebergId);
            File.WriteAllText(ConfigStore.ConfigFile, saved.ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.False(ArenaContext.Config.Maps.ContainsKey(ArenaMapCatalog.IcebergId));
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
    [InlineData("arena_iceberg")]
    [InlineData("ARENA_ICEBERG")]
    public void IcebergIsOptionalAndAdmitsEveryModeExceptTactical(string map)
    {
        Assert.Equal(ArenaMapCatalog.IcebergId, ArenaMapCatalog.CanonicalOptionalMapId(map));
        Assert.True(ArenaMapCatalog.IsSupportedMap(map));
        Assert.True(ArenaMapCatalog.IsOptionalMap(map));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsIceberg));
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation(map, ArenaMapCatalog.IsIceberg));
        Assert.True(ArenaMapCatalog.CanSelectNativeLocation(map, ArenaMapCatalog.IsIceberg));
        Assert.True(ArenaMapCatalog.RequiresModeFilteredView(map));
        Assert.True(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.DefaultClientAllowlist, map));
        Assert.False(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.BowlDefaultClientAllowlist, map));
        Assert.Same(ArenaTeamBoxCatalog.Iceberg, ArenaTeamBoxCatalog.Find(map));
        // The team boxes cap both team modes at 10v10; solo modes keep 24.
        Assert.Equal(20, ArenaParticipantPolicy.MaximumFor(map, "domination"));
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "zombie" })
        {
            Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out _, out _));
            if (mode == "zombie")
                Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, true, out _, out _));
            else
            {
                Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(map, mode));
                Assert.Equal(mode == "teamdeathmatch" ? 20 : 24, ArenaParticipantPolicy.MaximumFor(map, mode));
                Assert.True(ArenaParticipantPolicy.TryResolve(map, mode, false, false, 8, out var count, out _));
                Assert.Equal(8, count);
            }
        }
        // Like Equator: the centre leaves only four distant bot markers, so the
        // shared resolver refuses both Tactical formats at menu, arm and response.
        foreach (bool pitFireTeam in new[] { false, true })
        {
            Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", pitFireTeam,
                out var area, out var reason));
            Assert.Null(area);
            Assert.Equal("Iceberg has too few distant bot spawn points for Tactical's five-opponent squad.", reason);
        }
        foreach (string unknown in new[] { "arena_iceberg_extra", "arena_iceberg2", "iceberg", " arena_iceberg" })
        {
            Assert.False(ArenaMapCatalog.IsSupportedMap(unknown));
            Assert.Equal(string.Empty, ArenaMapCatalog.CanonicalOptionalMapId(unknown));
        }
    }

    [Theory]
    [InlineData("arena_iceberg")]
    [InlineData("ARENA_ICEBERG")]
    public void IcebergAdmitsWholeMapDominationWithoutPitFireTeamOrAreas(string map)
    {
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "domination", false, out var area, out var reason));
        Assert.Null(area); Assert.Null(reason);
        Assert.True(ArenaAreaCatalog.TryResolve(null, map, "DOMINATION", false, out _, out _));
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "domination", true, out _, out reason));
        Assert.Equal("Domination requires Factory or a supported Arena port, whole map, without PitFireTeam.", reason);
        foreach (string bounded in new[] { ArenaAreaCatalog.CustomsYardId, "unknown-area" })
            Assert.False(ArenaAreaCatalog.TryResolve(bounded, map, "domination", false, out _, out _));
        Assert.True(ArenaTeamBoxCatalog.Applies(map, "domination"));
        Assert.Equal(20, ArenaParticipantPolicy.MaximumFor(map, "domination"));
        Assert.True(ArenaParticipantPolicy.TryResolve(map, "domination", false, false, 20, out var count, out _));
        Assert.Equal(20, count);
        foreach (int invalid in new[] { 3, 22, 24 })
            Assert.False(ArenaParticipantPolicy.TryResolve(map, "domination", false, false, invalid, out _, out _));
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, "arena_iceberg_extra", "domination", false, out _, out _));
    }

    [Fact]
    public void ServerDefaultMapSetEqualsTheClientDefault()
    {
        // ConfigStore recognizes historical defaults by exact map set; the
        // current server default must therefore be the current client default.
        var keys = new ArenaModeConfig().Maps.Keys.OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist.Split(',').OrderBy(x => x, StringComparer.Ordinal), keys);
        Assert.Equal(ArenaMapCatalog.BowlDefaultClientAllowlist + "," + ArenaMapCatalog.IcebergId,
            ArenaMapCatalog.IcebergDefaultClientAllowlist);
        Assert.Equal(ArenaMapCatalog.IcebergDefaultClientAllowlist + "," + ArenaMapCatalog.AutoServiceId,
            ArenaMapCatalog.DefaultClientAllowlist);
        Assert.Equal(6, ArenaMapCatalog.OptionalMapsRevision);
        Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, new ArenaModeConfig().OptionalMapsRevision);
    }
}
