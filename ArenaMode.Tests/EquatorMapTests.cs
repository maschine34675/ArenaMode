using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class EquatorMapConfigTests
{
    [Theory]
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.EquatorDefaultClientAllowlist)]
    public void ExactClientDefaultsGainOptionalEquator(string old)
        => Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist, ArenaMapCatalog.UpgradeLegacyClientAllowlist(old));

    [Theory]
    [InlineData("")]
    [InlineData("factory4_day")]
    [InlineData("factory4_day,factory4_night,labyrinth,laboratory,bigmap,")]
    [InlineData("FACTORY4_DAY,FACTORY4_NIGHT,LABYRINTH,LABORATORY,BIGMAP")]
    public void EditedClientListsStayExact(string edited)
        => Assert.Equal(edited, ArenaMapCatalog.UpgradeLegacyClientAllowlist(edited));

    [Theory]
    [InlineData(ArenaMapCatalog.EquatorId)]
    [InlineData(ArenaMapCatalog.Bay5Id)]
    public void DefaultsUseRealZoneAndSmallMapPopulation(string mapId)
    {
        var map = new ArenaModeConfig().Maps[mapId];
        Assert.True(map.Enabled);
        Assert.Equal("BotZone_Main", map.SpawnZones);
        Assert.Equal(8, map.BotMax);
        Assert.Equal(8, map.MaxBotPerZone);
        Assert.Equal(4, map.BurstMinAliveBots);
        Assert.True(ArenaMapCatalog.ShouldDisplayNativeLocation("ARENA_EQUATOR", id => ArenaMapCatalog.IsEquator(id)));
        Assert.True(ArenaMapCatalog.AnyMapAvailable(ArenaMapCatalog.IsEquator));
    }

    [Theory]
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.NativeDefaultClientAllowlist)]
    public void ExactLegacyServerMapSetsGainEquatorOnceWithoutChangingValues(string old)
    {
        var maps = new JsonObject();
        foreach (var id in old.Split(','))
            maps[id] = new JsonObject { ["enabled"] = false, ["spawnZones"] = "user-zone", ["botMax"] = 7 };
        var saved = new JsonObject { ["maps"] = maps };
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig());
        Assert.True(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.Equal("BotZone_Main", maps[ArenaMapCatalog.EquatorId]!["spawnZones"]!.GetValue<string>());
        Assert.Equal("BotZone_Main", maps[ArenaMapCatalog.Bay5Id]!["spawnZones"]!.GetValue<string>());
        foreach (var id in old.Split(','))
        {
            Assert.False(maps[id]!["enabled"]!.GetValue<bool>());
            Assert.Equal("user-zone", maps[id]!["spawnZones"]!.GetValue<string>());
            Assert.Equal(7, maps[id]!["botMax"]!.GetValue<int>());
        }
        maps.Remove(ArenaMapCatalog.EquatorId);
        maps.Remove(ArenaMapCatalog.Bay5Id);
        ConfigStore.FillMissingKeys(saved, defaults);
        Assert.False(maps.ContainsKey(ArenaMapCatalog.EquatorId));
        Assert.False(maps.ContainsKey(ArenaMapCatalog.Bay5Id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"factory4_day\":{\"enabled\":true}}")]
    [InlineData("{\"custom-map\":{\"enabled\":false}}")]
    public void CustomizedMissingServerMapStaysAbsentAcrossRepeatedMerges(string mapJson)
    {
        var saved = new JsonObject { ["maps"] = JsonNode.Parse(mapJson) };
        for (int pass = 0; pass < 3; pass++)
        {
            ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
            Assert.False(saved["maps"]!.AsObject().ContainsKey(ArenaMapCatalog.EquatorId));
        }
    }

    [Fact]
    public void ExplicitDisableAndCustomEquatorSettingsRemainAuthoritative()
    {
        var saved = JsonNode.Parse("""{"maps":{"arena_equator":{"enabled":false,"spawnZones":"my-zone","botMax":3}}}""")!;
        ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig()));
        var config = saved.Deserialize<ArenaModeConfig>()!;
        Assert.False(config.IsMapEnabled(ArenaMapCatalog.EquatorId));
        Assert.Equal("my-zone", config.Maps[ArenaMapCatalog.EquatorId].SpawnZones);
        Assert.Equal(3, config.Maps[ArenaMapCatalog.EquatorId].BotMax);
    }
}

public sealed partial class ArenaAreaServerTests
{
    public static IEnumerable<object?[]> OptionalRouteCases()
    {
        foreach (string map in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.CustomsId })
            foreach (string? mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie", "endless", "rounds", null })
                yield return new object?[] { map, mode,
                    (mode is "deathmatch" or "gungame" or "tactical" or "teamdeathmatch" or "zombie")
                    && map != ArenaMapCatalog.CustomsId
                    && !(ArenaMapCatalog.IsEquator(map) && mode == "tactical") };
    }

    [Theory]
    [MemberData(nameof(OptionalRouteCases))]
    public async Task ExpandedFullMapRouteKeepsConcreteBlockersAndSnapshotContract(string mapId, string? mode, bool expected)
    {
        var oldLookup = ArenaOptionalMapPolicy.FindLocation;
        try
        {
            ArenaOptionalMapPolicy.FindLocation = _ => new() { Id = mapId, Enabled = true };
            ArenaContext.Config.Maps[mapId] = new ArenaModeConfig().Maps[mapId];
            var (router, saves, profile, cloner) = CustomsRouter();
            var request = new ArenaRaidStartRequest { Map = mapId, Mode = mode, RequestId = Token,
                Participants = ArenaEquatorTeamSpawns.Applies(mapId, mode!) ? 4 : null,
                DeathmatchHostility = mode == "deathmatch" ? DeathmatchSelection.FreeForAll : null };
            using var result = JsonDocument.Parse(await InvokeStart(router, request));
            Assert.Equal(expected, result.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(expected ? 1 : 0, cloner.Calls);
            Assert.Same(profile, saves.GetProfile(Session));
            Assert.Equal(expected, ArenaContext.Snapshots!.HasSnapshot(Session));
            if (expected)
            {
                using var replay = JsonDocument.Parse(await InvokeStart(router, request));
                Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
                Assert.Equal(1, cloner.Calls);
                Assert.Equal(Token, Sessions.GetArmToken(Session));
            }
        }
        finally { ArenaOptionalMapPolicy.FindLocation = oldLookup; }
    }

    public static IEnumerable<object[]> OptionalUnavailableCases()
    {
        foreach (string map in new[] { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id })
            foreach (string scenario in new[] { "missing", "disabled", "wrong-id", "other-imported-map", "throws" })
                yield return new object[] { map, scenario };
    }

    [Theory]
    [MemberData(nameof(OptionalUnavailableCases))]
    public async Task UnavailableOptionalMapNeverRecoversOrArmsOrAdvertisesAndKeepsConfig(string mapId, string scenario)
    {
        var oldLookup = ArenaOptionalMapPolicy.FindLocation;
        try
        {
            ArenaOptionalMapPolicy.FindLocation = scenario switch
            {
                "missing" => null,
                "disabled" => _ => new() { Id = mapId, Enabled = false },
                "wrong-id" => _ => new() { Id = Map, Enabled = true },
                "other-imported-map" => _ => new() { Id = ArenaMapCatalog.IsEquator(mapId) ? ArenaMapCatalog.Bay5Id : ArenaMapCatalog.EquatorId, Enabled = true },
                _ => _ => throw new InvalidOperationException("database not ready"),
            };
            ArenaContext.Config = new ArenaModeConfig();
            string before = JsonSerializer.Serialize(ArenaContext.Config);
            using var config = JsonDocument.Parse(ArenaOptionalMapPolicy.ClientConfigJson(ArenaContext.Config));
            Assert.False(config.RootElement.GetProperty("maps").TryGetProperty(mapId, out _));
            Assert.True(config.RootElement.GetProperty("maps").TryGetProperty(Map, out _));
            Assert.Equal(before, JsonSerializer.Serialize(ArenaContext.Config));
            var (router, saves, profile, cloner) = CustomsRouter();
            Assert.True(ArenaContext.Snapshots!.TakeSnapshot(Session));
            using var result = JsonDocument.Parse(await InvokeStart(router,
                new() { Map = mapId, Mode = "gungame", RequestId = Token }));
            Assert.False(result.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(1, cloner.Calls);
            Assert.True(ArenaContext.Snapshots.HasSnapshot(Session));
            Assert.Same(profile, saves.GetProfile(Session));
            Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
            Assert.True(ArenaOptionalMapPolicy.IsAvailable(Map));
        }
        finally { ArenaOptionalMapPolicy.FindLocation = oldLookup; }
    }
}
