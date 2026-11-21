using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Block (arena_yard, ArenaMapPort's seventh map) as an optional ArenaMapPort map: optional-map
/// revision 7, no mode refusal solo or with a PitFireTeam squad, the default of eight
/// participants and the live-location discovery. Block is the player-facing name; it has nothing
/// to do with the Customs yard area. The team boxes live in BlockTeamSpawnTests.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class BlockMapTests
{
    private static JsonObject Saved(string ids, int revision)
    {
        var maps = new JsonObject();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            maps[id] = new JsonObject { ["enabled"] = false, ["botMax"] = 3, ["spawnZones"] = "user-zone" };
        return new JsonObject { ["maps"] = maps, ["optionalMapsRevision"] = revision };
    }

    // Revision 7 extends only the exact Chop Shop default with Block. An Iceberg default at
    // revision 6 is a Chop Shop removal and gains neither Chop Shop nor Block; a Chop Shop
    // default at revision 7 without Block is a Block removal and stays; every earlier exact
    // default keeps its own cutoff and gains the complete current default.
    [Theory]
    [InlineData(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 6, true)]
    [InlineData(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 7, false)]   // Block removed at revision 7
    [InlineData(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 8, false)]   // future marker
    [InlineData(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 99, false)]
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 5, true)]        // gains Chop Shop and Block
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 4, true)]
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 6, false)]       // Chop Shop removed at revision 6
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 7, false)]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 4, true)]           // gains everything
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 5, false)]          // Iceberg removed at revision 5
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 6, false)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 3, true)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 4, false)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2, true)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 3, false)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1, true)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2, false)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.DefaultClientAllowlist, 6, false)]              // already complete
    [InlineData(ArenaMapCatalog.DefaultClientAllowlist, 7, false)]
    [InlineData("factory4_day,arena_autoservice", 6, false)]                    // custom list
    [InlineData("factory4_day,arena_bowl,arena_iceberg,arena_autoservice", 6, false)]
    [InlineData("factory4_day,custom-map", 0, false)]
    public void MigrationOnlyExtendsRecognizedHistoricalDefaults(string ids, int revision, bool gain)
    {
        Assert.Equal(gain ? ArenaMapCatalog.DefaultClientAllowlist : ids,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, revision > 0));
        var saved = Saved(ids, revision);
        var maps = saved["maps"]!.AsObject();
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        var defaultsBefore = defaults.ToJsonString();
        var had = ids.Split(',');
        bool hadBlock = had.Contains(ArenaMapCatalog.YardId);
        bool hadChopShop = had.Contains(ArenaMapCatalog.AutoServiceId);
        bool hadIceberg = had.Contains(ArenaMapCatalog.IcebergId);
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, defaults);
            Assert.Equal(gain || hadBlock, maps.ContainsKey(ArenaMapCatalog.YardId));
            // A gain brings the complete current default; a refusal adds no optional map.
            Assert.Equal(gain || hadChopShop, maps.ContainsKey(ArenaMapCatalog.AutoServiceId));
            Assert.Equal(gain || hadIceberg, maps.ContainsKey(ArenaMapCatalog.IcebergId));
            foreach (var id in had)
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
        var block = saved.Deserialize<ArenaModeConfig>()!.Maps[ArenaMapCatalog.YardId];
        Assert.True(block.Enabled);
        Assert.Equal("BotZone_Main", block.SpawnZones);
        Assert.Equal(8, block.BotMax);
        Assert.Equal(8, block.MaxBotPerZone);
        Assert.Equal(4, block.BurstMinAliveBots);
        // Once upgraded, deleting Block must survive the next repair, even though
        // the remaining map set again looks exactly like the Chop Shop default.
        maps.Remove(ArenaMapCatalog.YardId);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.YardId));
        Assert.Equal(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, ArenaMapCatalog.UpgradeOptionalClientAllowlist(
            ArenaMapCatalog.AutoServiceDefaultClientAllowlist, ArenaMapCatalog.OptionalMapsRevision, true));
    }

    [Fact]
    public void ExistingDisabledBlockAndMalformedRevisionStayUserOwned()
    {
        var saved = JsonNode.Parse("""
            {"optionalMapsRevision":"invalid","maps":{
              "arena_yard":{"enabled":false,"botMax":3,"spawnZones":"my-zone"}}}
            """)!;
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            var entry = saved["maps"]![ArenaMapCatalog.YardId]!;
            Assert.False(entry["enabled"]!.GetValue<bool>());
            Assert.Equal(3, entry["botMax"]!.GetValue<int>());
            Assert.Equal("my-zone", entry["spawnZones"]!.GetValue<string>());
            Assert.Equal("invalid", saved["optionalMapsRevision"]!.GetValue<string>());
            Assert.Null(saved["maps"]![ArenaMapCatalog.AutoServiceId]);
            Assert.Null(saved["maps"]![ArenaMapCatalog.IcebergId]);
        }
        // A malformed marker over the exact former default never re-enables a map: the server
        // reads it as int.MaxValue, so even the Chop Shop default stays without Block.
        var chopShopDefault = Saved(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 0);
        chopShopDefault["optionalMapsRevision"] = "invalid";
        ConfigStore.FillMissingKeys(chopShopDefault, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        Assert.Null(chopShopDefault["maps"]![ArenaMapCatalog.YardId]);
        Assert.Equal("invalid", chopShopDefault["optionalMapsRevision"]!.GetValue<string>());
    }

    [Fact]
    public void RealReloadPersistsBlockAndHonorsItsRemovalOnNextReload()
    {
        string previousDir = ConfigStore.ConfigDir;
        var previousConfig = ArenaContext.Config;
        string directory = Path.Combine(Path.GetTempPath(), "arena-block-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigStore.Initialize(directory);
            File.WriteAllText(ConfigStore.ConfigFile, Saved(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, 6).ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.True(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.YardId));
            // The existing Chop Shop entry keeps the user's disabled state.
            Assert.False(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.AutoServiceId));
            var saved = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, saved["optionalMapsRevision"]!.GetValue<int>());
            Assert.Equal(7, saved["optionalMapsRevision"]!.GetValue<int>());
            saved["maps"]!.AsObject().Remove(ArenaMapCatalog.YardId);
            File.WriteAllText(ConfigStore.ConfigFile, saved.ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.False(ArenaContext.Config.Maps.ContainsKey(ArenaMapCatalog.YardId));
            // The removal is also persisted: the file still lacks Block afterwards.
            var after = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Null(after["maps"]![ArenaMapCatalog.YardId]);
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
    [InlineData("arena_yard")]
    [InlineData("ARENA_YARD")]
    public void BlockIsOptionalAndAdmitsEveryModeSoloAndWithAPitFireTeamSquad(string map)
    {
        Assert.Equal(ArenaMapCatalog.YardId, ArenaMapCatalog.CanonicalOptionalMapId(map));
        Assert.True(ArenaMapCatalog.IsYard(map));
        Assert.True(ArenaMapCatalog.IsSupportedMap(map));
        Assert.True(ArenaMapCatalog.IsOptionalMap(map));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsYard));
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation(map, ArenaMapCatalog.IsYard));
        Assert.True(ArenaMapCatalog.CanSelectNativeLocation(map, ArenaMapCatalog.IsYard));
        Assert.True(ArenaMapCatalog.RequiresModeFilteredView(map));
        Assert.True(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.DefaultClientAllowlist, map));
        Assert.False(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.AutoServiceDefaultClientAllowlist, map));
        // No refusal anywhere: Tactical and Zombies are admitted solo and with a PitFireTeam
        // squad (18 Bot/BotPmc rows over a main walkable component the size of Iceberg's), and
        // the shared resolver answers with neither an area nor a reason.
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "zombie", "tactical" })
        foreach (bool pitFireTeam in mode is "zombie" or "tactical" ? new[] { false, true } : new[] { false })
        {
            Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, pitFireTeam,
                out var area, out var reason), $"{mode} pft={pitFireTeam}");
            Assert.Null(area);
            Assert.Null(reason);
        }
        foreach (string unknown in new[] { "arena_yard_extra", "arena_yard2", "yard", "block", " arena_yard" })
        {
            Assert.False(ArenaMapCatalog.IsSupportedMap(unknown));
            Assert.Equal(string.Empty, ArenaMapCatalog.CanonicalOptionalMapId(unknown));
        }
    }

    [Theory]
    [InlineData("arena_yard")]
    [InlineData("ARENA_YARD")]
    public void BlockAdmitsWholeMapDominationWithoutPitFireTeamOrAreas(string map)
    {
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "domination", false, out var area, out var reason));
        Assert.Null(area); Assert.Null(reason);
        Assert.True(ArenaAreaCatalog.TryResolve(null, map, "DOMINATION", false, out _, out _));
        // Domination keeps its generic PitFireTeam refusal; Block adds none of its own.
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "domination", true, out _, out reason));
        Assert.Equal("Domination is not available for this map or area, or with a PitFireTeam squad.", reason);
        foreach (string bounded in new[] { ArenaAreaCatalog.CustomsYardId, "unknown-area" })
            Assert.False(ArenaAreaCatalog.TryResolve(bounded, map, "domination", false, out _, out _));
        Assert.True(ArenaTeamBoxCatalog.Applies(map, "domination"));
        Assert.Equal(20, ArenaParticipantPolicy.MaximumFor(map, "domination"));
        Assert.True(ArenaParticipantPolicy.TryResolve(map, "domination", false, false, 20, out var count, out _));
        Assert.Equal(20, count);
        foreach (int invalid in new[] { 3, 22, 24 })
            Assert.False(ArenaParticipantPolicy.TryResolve(map, "domination", false, false, invalid, out _, out _));
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, "arena_yard_extra", "domination", false, out _, out _));
    }

    // Eighteen installed Bot rows (seven bots leave eleven spare) on a main walkable component the
    // size of Iceberg's and Bowl's put Block in the 8-group. Ranges stay generic.
    [Theory]
    [InlineData("arena_yard")]
    [InlineData("ARENA_YARD")]
    public void BlockDefaultsToEightParticipants(string map)
    {
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch" })
        {
            Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(map, mode));
            Assert.True(ArenaParticipantPolicy.TryResolve(map, mode, false, false, 8, out var count, out _));
            Assert.Equal(8, count);
            Assert.True(ArenaParticipantPolicy.TryResolve(map, mode, false, false, 6, out count, out _));
            Assert.Equal(6, count);
        }
        Assert.Equal(24, ArenaParticipantPolicy.MaximumFor(map, "deathmatch"));
        Assert.Equal(20, ArenaParticipantPolicy.MaximumFor(map, "teamdeathmatch"));
        // The neighbours keep their own defaults.
        Assert.Equal(4, ArenaParticipantPolicy.DefaultTotal(ArenaMapCatalog.EquatorId, "deathmatch"));
        Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(ArenaMapCatalog.AutoServiceId, "deathmatch"));
    }

    [Fact]
    public void ServerDefaultMapSetEqualsTheClientDefault()
    {
        // ConfigStore recognizes historical defaults by exact map set; the
        // current server default must therefore be the current client default.
        var keys = new ArenaModeConfig().Maps.Keys.OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist.Split(',').OrderBy(x => x, StringComparer.Ordinal), keys);
        Assert.Equal(ArenaMapCatalog.AutoServiceDefaultClientAllowlist + "," + ArenaMapCatalog.YardId,
            ArenaMapCatalog.DefaultClientAllowlist);
        Assert.Equal(7, ArenaMapCatalog.OptionalMapsRevision);
        Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, new ArenaModeConfig().OptionalMapsRevision);
        var map = new ArenaModeConfig().Maps[ArenaMapCatalog.YardId];
        Assert.True(map.Enabled);
        Assert.Equal("BotZone_Main", map.SpawnZones);
        Assert.Equal(8, map.BotMax);
        Assert.Equal(8, map.MaxBotPerZone);
        Assert.Equal(4, map.BurstMinAliveBots);
    }

    // Detection is the live location by its exact id, asked afresh on every request;
    // no other map's presence and no version string stands in for it.
    [Theory]
    [InlineData("present", true)]
    [InlineData("disabled", false)]
    [InlineData("missing", false)]
    [InlineData("wrong-id", false)]
    [InlineData("other-imported-map", false)]
    [InlineData("throws", false)]
    public void DiscoveryFollowsTheLiveLocationOnly(string scenario, bool advertised)
    {
        var oldLookup = ArenaOptionalMapPolicy.FindLocation;
        try
        {
            var calls = new List<string>();
            ArenaOptionalMapPolicy.FindLocation = id =>
            {
                calls.Add(id);
                return scenario switch
                {
                    "present" => id == ArenaMapCatalog.YardId
                        ? new SPTarkov.Server.Core.Models.Eft.Common.LocationBase { Id = id, Enabled = true } : null,
                    "disabled" => new() { Id = id, Enabled = false },
                    "wrong-id" => new() { Id = "arena_yard_extra", Enabled = true },
                    "other-imported-map" => new() { Id = ArenaMapCatalog.AutoServiceId, Enabled = true },
                    "throws" => throw new InvalidOperationException("lookup failed"),
                    _ => null
                };
            };
            Assert.Equal(advertised, ArenaOptionalMapPolicy.IsAvailable(ArenaMapCatalog.YardId));
            Assert.Equal(advertised, ArenaOptionalMapPolicy.IsAvailable("ARENA_YARD"));
            Assert.Equal(ArenaMapCatalog.YardId, calls[^1]);
            var config = new ArenaModeConfig();
            string before = JsonSerializer.Serialize(config);
            calls.Clear();
            using var view = JsonDocument.Parse(ArenaOptionalMapPolicy.ClientConfigJson(config));
            Assert.Equal(advertised, view.RootElement.GetProperty("maps").TryGetProperty(ArenaMapCatalog.YardId, out _));
            // The lookup is repeated for each map in turn, Block last (after Chop Shop).
            Assert.Equal(ArenaMapCatalog.AutoServiceId, calls[^2]);
            Assert.Equal(ArenaMapCatalog.YardId, calls[^1]);
            Assert.Equal(before, JsonSerializer.Serialize(config));
        }
        finally { ArenaOptionalMapPolicy.FindLocation = oldLookup; }
    }
}
