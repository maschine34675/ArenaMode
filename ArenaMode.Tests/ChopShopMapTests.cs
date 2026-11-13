using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Chop Shop (arena_autoservice) as an optional ArenaMapPort map: optional-map revision 6,
/// the Tactical and PitFireTeam-Zombies refusals, the default of six participants and
/// the live-location discovery. The team boxes and the one-point Domination come with their own commits.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class ChopShopMapTests
{
    private static JsonObject Saved(string ids, int revision)
    {
        var maps = new JsonObject();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            maps[id] = new JsonObject { ["enabled"] = false, ["botMax"] = 3, ["spawnZones"] = "user-zone" };
        return new JsonObject { ["maps"] = maps, ["optionalMapsRevision"] = revision };
    }

    // Revision 6 extends only the exact Iceberg default. A Bowl-only list at revision 5
    // is an Iceberg removal and stays; an Iceberg default at revision 6 without Chop Shop
    // is a Chop Shop removal and stays; every earlier default keeps its own cutoff and
    // gains the complete current default.
    [Theory]
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 5, true)]
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 4, true)]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 4, true)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 3, true)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2, true)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1, true)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 5, false)]     // Iceberg removed at revision 5
    [InlineData(ArenaMapCatalog.BowlDefaultClientAllowlist, 6, false)]
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 6, false)]  // Chop Shop removed at revision 6
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 7, false)]  // future marker
    [InlineData(ArenaMapCatalog.IcebergDefaultClientAllowlist, 99, false)]
    [InlineData(ArenaMapCatalog.DefaultClientAllowlist, 5, false)]         // already complete
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 4, false)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 3, false)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2, false)]
    [InlineData("factory4_day,arena_bowl,arena_iceberg", 5, false)]
    [InlineData("factory4_day,custom-map", 0, false)]
    public void MigrationOnlyExtendsRecognizedHistoricalDefaults(string ids, int revision, bool gain)
    {
        Assert.Equal(gain ? ArenaMapCatalog.DefaultClientAllowlist : ids,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, revision > 0));
        var saved = Saved(ids, revision);
        var maps = saved["maps"]!.AsObject();
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        var defaultsBefore = defaults.ToJsonString();
        bool hadIceberg = ids.Split(',').Contains(ArenaMapCatalog.IcebergId);
        bool hadChopShop = ids.Split(',').Contains(ArenaMapCatalog.AutoServiceId);
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, defaults);
            Assert.Equal(gain || hadChopShop, maps.ContainsKey(ArenaMapCatalog.AutoServiceId));
            Assert.Equal(gain || hadIceberg, maps.ContainsKey(ArenaMapCatalog.IcebergId));
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
        var chopShop = saved.Deserialize<ArenaModeConfig>()!.Maps[ArenaMapCatalog.AutoServiceId];
        Assert.True(chopShop.Enabled);
        Assert.Equal("BotZone_Main", chopShop.SpawnZones);
        Assert.Equal(8, chopShop.BotMax);
        Assert.Equal(8, chopShop.MaxBotPerZone);
        Assert.Equal(4, chopShop.BurstMinAliveBots);
        // Once upgraded, deleting Chop Shop must survive the next repair, even though
        // the remaining map set again looks exactly like the Iceberg default.
        maps.Remove(ArenaMapCatalog.AutoServiceId);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.AutoServiceId));
        Assert.Equal(ArenaMapCatalog.IcebergDefaultClientAllowlist, ArenaMapCatalog.UpgradeOptionalClientAllowlist(
            ArenaMapCatalog.IcebergDefaultClientAllowlist, ArenaMapCatalog.OptionalMapsRevision, true));
    }

    [Fact]
    public void ExistingDisabledChopShopAndMalformedRevisionStayUserOwned()
    {
        var saved = JsonNode.Parse("""
            {"optionalMapsRevision":"invalid","maps":{
              "arena_autoservice":{"enabled":false,"botMax":3,"spawnZones":"my-zone"}}}
            """)!;
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            var entry = saved["maps"]![ArenaMapCatalog.AutoServiceId]!;
            Assert.False(entry["enabled"]!.GetValue<bool>());
            Assert.Equal(3, entry["botMax"]!.GetValue<int>());
            Assert.Equal("my-zone", entry["spawnZones"]!.GetValue<string>());
            Assert.Equal("invalid", saved["optionalMapsRevision"]!.GetValue<string>());
            Assert.Null(saved["maps"]![ArenaMapCatalog.IcebergId]);
            Assert.Null(saved["maps"]![ArenaMapCatalog.BowlId]);
        }
        // A malformed marker over the exact former default never re-enables a map.
        var icebergDefault = Saved(ArenaMapCatalog.IcebergDefaultClientAllowlist, 0);
        icebergDefault["optionalMapsRevision"] = "invalid";
        ConfigStore.FillMissingKeys(icebergDefault, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        Assert.Null(icebergDefault["maps"]![ArenaMapCatalog.AutoServiceId]);
        Assert.Equal("invalid", icebergDefault["optionalMapsRevision"]!.GetValue<string>());
    }

    [Fact]
    public void RealReloadPersistsChopShopAndHonorsItsRemovalOnNextReload()
    {
        string previousDir = ConfigStore.ConfigDir;
        var previousConfig = ArenaContext.Config;
        string directory = Path.Combine(Path.GetTempPath(), "arena-chopshop-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigStore.Initialize(directory);
            File.WriteAllText(ConfigStore.ConfigFile, Saved(ArenaMapCatalog.IcebergDefaultClientAllowlist, 5).ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.True(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.AutoServiceId));
            Assert.False(ArenaContext.Config.IsMapEnabled(ArenaMapCatalog.IcebergId));
            var saved = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, saved["optionalMapsRevision"]!.GetValue<int>());
            Assert.Equal(6, saved["optionalMapsRevision"]!.GetValue<int>());
            saved["maps"]!.AsObject().Remove(ArenaMapCatalog.AutoServiceId);
            File.WriteAllText(ConfigStore.ConfigFile, saved.ToJsonString());
            Assert.Equal(ConfigResult.Success, ConfigStore.Reload(out _));
            Assert.False(ArenaContext.Config.Maps.ContainsKey(ArenaMapCatalog.AutoServiceId));
            // The removal is also persisted: the file still lacks Chop Shop afterwards.
            var after = JsonNode.Parse(File.ReadAllText(ConfigStore.ConfigFile))!;
            Assert.Null(after["maps"]![ArenaMapCatalog.AutoServiceId]);
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
    [InlineData("arena_autoservice")]
    [InlineData("ARENA_AUTOSERVICE")]
    public void ChopShopIsOptionalAndAdmitsEveryModeExceptSquadTacticalAndSquadZombies(string map)
    {
        Assert.Equal(ArenaMapCatalog.AutoServiceId, ArenaMapCatalog.CanonicalOptionalMapId(map));
        Assert.True(ArenaMapCatalog.IsAutoService(map));
        Assert.True(ArenaMapCatalog.IsSupportedMap(map));
        Assert.True(ArenaMapCatalog.IsOptionalMap(map));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsAutoService));
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation(map, ArenaMapCatalog.IsAutoService));
        Assert.True(ArenaMapCatalog.CanSelectNativeLocation(map, ArenaMapCatalog.IsAutoService));
        Assert.True(ArenaMapCatalog.RequiresModeFilteredView(map));
        Assert.True(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.DefaultClientAllowlist, map));
        Assert.False(ArenaMapCatalog.AllowlistContains(ArenaMapCatalog.IcebergDefaultClientAllowlist, map));
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "zombie", "tactical" })
            Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out _, out _));
        // Solo Tactical is admitted (16 Bot rows since ArenaMapPort 2026-11-12: it never needs
        // exactly five, and a smaller squad after the 45 s wait is the worst case). Only the
        // PitFireTeam squad is refused: a surviving leader in the yard centre has three or four
        // usable rows there, and RequestTeamEnemies demands exactly five. The shared resolver
        // answers at menu, arm and response.
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", false,
            out var soloArea, out var soloReason));
        Assert.Null(soloArea);
        Assert.Null(soloReason);
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", true,
            out var area, out var reason));
        Assert.Null(area);
        Assert.Equal("Chop Shop's yard centre has too few distant bot spawn points for a PitFireTeam Tactical squad; select Solo or another map.", reason);
        // Solo Zombies stays admitted; the PitFireTeam squad does not fit the 1,915.6 m² main mesh.
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "zombie", false, out _, out _));
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "zombie", true, out var zombieArea, out var zombieReason));
        Assert.Null(zombieArea);
        Assert.Equal("Chop Shop is too small for Zombies with a PitFireTeam squad; select Solo or another map.", zombieReason);
        foreach (string unknown in new[] { "arena_autoservice_extra", "arena_autoservice2", "autoservice", "chopshop", " arena_autoservice" })
        {
            Assert.False(ArenaMapCatalog.IsSupportedMap(unknown));
            Assert.Equal(string.Empty, ArenaMapCatalog.CanonicalOptionalMapId(unknown));
        }
    }

    [Theory]
    [InlineData("arena_autoservice")]
    [InlineData("ARENA_AUTOSERVICE")]
    public void ChopShopAdmitsWholeMapDominationWithoutPitFireTeamOrAreas(string map)
    {
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "domination", false, out var area, out var reason));
        Assert.Null(area); Assert.Null(reason);
        Assert.True(ArenaAreaCatalog.TryResolve(null, map, "DOMINATION", false, out _, out _));
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
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, "arena_autoservice_extra", "domination", false, out _, out _));
    }

    // Sixteen installed Bot rows (ArenaMapPort 2026-11-12) put Chop Shop in the 8-group: it
    // was six while it had seven rows. A value a player saved stays valid (6 is still
    // accepted); only the recommendation moved.
    [Theory]
    [InlineData("arena_autoservice")]
    [InlineData("ARENA_AUTOSERVICE")]
    public void ChopShopDefaultsToEightParticipants(string map)
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
        // The neighbours keep their own defaults.
        Assert.Equal(4, ArenaParticipantPolicy.DefaultTotal(ArenaMapCatalog.EquatorId, "deathmatch"));
        Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(ArenaMapCatalog.IcebergId, "deathmatch"));
    }

    [Fact]
    public void ServerDefaultMapSetEqualsTheClientDefault()
    {
        // ConfigStore recognizes historical defaults by exact map set; the
        // current server default must therefore be the current client default.
        var keys = new ArenaModeConfig().Maps.Keys.OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist.Split(',').OrderBy(x => x, StringComparer.Ordinal), keys);
        Assert.Equal(ArenaMapCatalog.IcebergDefaultClientAllowlist + "," + ArenaMapCatalog.AutoServiceId,
            ArenaMapCatalog.DefaultClientAllowlist);
        Assert.Equal(6, ArenaMapCatalog.OptionalMapsRevision);
        Assert.Equal(ArenaMapCatalog.OptionalMapsRevision, new ArenaModeConfig().OptionalMapsRevision);
        var map = new ArenaModeConfig().Maps[ArenaMapCatalog.AutoServiceId];
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
                    "present" => id == ArenaMapCatalog.AutoServiceId
                        ? new SPTarkov.Server.Core.Models.Eft.Common.LocationBase { Id = id, Enabled = true } : null,
                    "disabled" => new() { Id = id, Enabled = false },
                    "wrong-id" => new() { Id = "arena_autoservice_extra", Enabled = true },
                    "other-imported-map" => new() { Id = ArenaMapCatalog.IcebergId, Enabled = true },
                    "throws" => throw new InvalidOperationException("lookup failed"),
                    _ => null
                };
            };
            Assert.Equal(advertised, ArenaOptionalMapPolicy.IsAvailable(ArenaMapCatalog.AutoServiceId));
            Assert.Equal(advertised, ArenaOptionalMapPolicy.IsAvailable("ARENA_AUTOSERVICE"));
            Assert.Equal(ArenaMapCatalog.AutoServiceId, calls[^1]);
            var config = new ArenaModeConfig();
            string before = JsonSerializer.Serialize(config);
            calls.Clear();
            using var view = JsonDocument.Parse(ArenaOptionalMapPolicy.ClientConfigJson(config));
            Assert.Equal(advertised, view.RootElement.GetProperty("maps").TryGetProperty(ArenaMapCatalog.AutoServiceId, out _));
            // The lookup is repeated for each map in turn, Chop Shop last.
            Assert.Equal(ArenaMapCatalog.AutoServiceId, calls[^1]);
            Assert.Equal(before, JsonSerializer.Serialize(config));
        }
        finally { ArenaOptionalMapPolicy.FindLocation = oldLookup; }
    }
}
