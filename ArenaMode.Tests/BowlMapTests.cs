using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class BowlMapTests
{
    [Theory]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 3, true)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 2, true)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 1, true)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist, 0, true)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 4, false)]
    [InlineData(ArenaMapCatalog.AirPitDefaultClientAllowlist, 5, false)]
    [InlineData(ArenaMapCatalog.Bay5DefaultClientAllowlist, 3, false)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist, 2, false)]
    [InlineData("factory4_day,arena_airpit", 3, false)]
    [InlineData("factory4_day,custom-map", 0, false)]
    public void MigrationOnlyExtendsRecognizedHistoricalDefaults(string ids, int revision, bool gain)
    {
        Assert.Equal(gain ? ArenaMapCatalog.DefaultClientAllowlist : ids,
            ArenaMapCatalog.UpgradeOptionalClientAllowlist(ids, revision, revision > 0));
        var maps = new JsonObject();
        foreach (var id in ids.Split(','))
            maps[id] = new JsonObject { ["enabled"] = false, ["botMax"] = 3, ["spawnZones"] = "user-zone" };
        var saved = new JsonObject { ["maps"] = maps, ["optionalMapsRevision"] = revision };
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        var defaultsBefore = defaults.ToJsonString();
        for (int pass = 0; pass < 2; pass++)
        {
            ConfigStore.FillMissingKeys(saved, defaults);
            Assert.Equal(gain, maps.ContainsKey(ArenaMapCatalog.BowlId));
            foreach (var id in ids.Split(','))
            {
                Assert.False(maps[id]!["enabled"]!.GetValue<bool>());
                Assert.Equal(3, maps[id]!["botMax"]!.GetValue<int>());
                Assert.Equal("user-zone", maps[id]!["spawnZones"]!.GetValue<string>());
            }
            Assert.Equal(defaultsBefore, defaults.ToJsonString());
        }
        if (!gain) return;
        Assert.True(maps[ArenaMapCatalog.BowlId]!["enabled"]!.GetValue<bool>());
        Assert.Equal("BotZone_Main", maps[ArenaMapCatalog.BowlId]!["spawnZones"]!.GetValue<string>());
        // Once upgraded, deleting Bowl must survive the next repair, even though
        // the remaining map set again looks exactly like the prior default.
        maps.Remove(ArenaMapCatalog.BowlId);
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.BowlId));
    }

    [Fact]
    public void ExistingDisabledBowlAndMalformedRevisionStayUserOwned()
    {
        var saved = JsonNode.Parse("""
            {"optionalMapsRevision":"invalid","maps":{
              "arena_bowl":{"enabled":false,"botMax":3,"spawnZones":"my-zone"}}}
            """)!;
        ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        Assert.False(saved["maps"]![ArenaMapCatalog.BowlId]!["enabled"]!.GetValue<bool>());
        Assert.Equal(3, saved["maps"]![ArenaMapCatalog.BowlId]!["botMax"]!.GetValue<int>());
        Assert.Equal("my-zone", saved["maps"]![ArenaMapCatalog.BowlId]!["spawnZones"]!.GetValue<string>());
        Assert.Equal("invalid", saved["optionalMapsRevision"]!.GetValue<string>());
        Assert.Null(saved["maps"]![ArenaMapCatalog.AirPitId]);
    }

    [Theory]
    [InlineData("arena_bowl")]
    [InlineData("ARENA_BOWL")]
    public void BowlUsesTeamBoxesOnlyForIndependentTdmAndAllowsAllModes(string map)
    {
        Assert.Equal(ArenaMapCatalog.BowlId, ArenaMapCatalog.CanonicalOptionalMapId(map));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsBowl));
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation(map, ArenaMapCatalog.IsBowl));
        Assert.True(ArenaMapCatalog.CanSelectNativeLocation(map, ArenaMapCatalog.IsBowl));
        Assert.True(ArenaMapCatalog.RequiresModeFilteredView(map));
        Assert.Same(ArenaTeamBoxCatalog.Bowl, ArenaTeamBoxCatalog.Find(map));
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical", "zombie" })
        {
            Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, false, out _, out _));
            if (mode is "tactical" or "zombie")
                Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, mode, true, out _, out _));
            else
            {
                Assert.Equal(8, ArenaParticipantPolicy.DefaultTotal(map, mode));
                Assert.Equal(mode == "teamdeathmatch" ? 20 : 24, ArenaParticipantPolicy.MaximumFor(map, mode));
                Assert.True(ArenaParticipantPolicy.TryResolve(map, mode, false, false, 8, out var count, out _));
                Assert.Equal(8, count);
            }
        }
        foreach (string unknown in new[] { "arena_bowl_extra", "arena_bowl2" })
            Assert.False(ArenaMapCatalog.IsSupportedMap(unknown));
    }
}
