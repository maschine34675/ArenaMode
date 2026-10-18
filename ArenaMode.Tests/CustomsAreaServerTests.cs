using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace ArenaMode.Tests;

public sealed partial class ArenaAreaServerTests
{
    private const string Customs = ArenaMapCatalog.CustomsId;
    private const string CustomsArea = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
    private static readonly JsonSerializerOptions CustomsJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(), new StringToMongoIdConverter(), new ListOrTConverterFactory() },
    };

    public static IEnumerable<object?[]> InvalidCustomsRequests()
    {
        // This fixture always supplies a bounded area team-size override. That
        // stale size must not be silently accepted for an omitted/full area.
        foreach (string? area in new[] { null, "", "  ", ArenaAreaCatalog.FullMapId })
            foreach (string? mode in new[] { null, Mode, "endless", "rounds", "gungame", "tactical" })
                yield return [area, Customs, mode, false];
        yield return [ArenaAreaCatalog.FullMapId, Customs, "tactical", true];
        yield return [CustomsArea, Customs, Mode, true];
        yield return [CustomsArea, Customs, "tactical", true];
        foreach (string? mode in new[] { null, "", "endless", "rounds", "gungame", "tactical", "unknown" })
            yield return [CustomsArea, Customs, mode, false];
        foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId, ArenaMapCatalog.LaboratoryId, "customs" })
            yield return [CustomsArea, map, Mode, false];
        yield return [ArenaAreaCatalog.FactorySouthId, Customs, Mode, false];
        yield return [ArenaAreaCatalog.CustomsFactoryCenterId, Customs, Mode, false];
        yield return [ArenaAreaCatalog.CustomsFactoryCenterPolygonId, Customs, Mode, false];
        yield return ["customs-factory-center-teams-v1", Customs, Mode, false];
        yield return ["customs-factory-center-v2", Customs, Mode, false];
        yield return [CustomsArea.ToUpperInvariant(), Customs, Mode, false];
    }

    [Theory]
    [MemberData(nameof(InvalidCustomsRequests))]
    public async Task CustomsAdmissionRejectsBeforeSnapshotPreparationRecoveryOrArmReplacement(
        string? area, string map, string? mode, bool pft)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = map, Mode = mode, AreaId = area, PitFireTeamTeam = pft, RequestId = Token, AreaTeamSize = AreaSize };

        using var fresh = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(fresh.RootElement.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(fresh.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Same(original, saves.GetProfile(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.False(Sessions.IsExternalProfileWriteProtected(Session));
        Assert.False(Sessions.BeginArmPreparation(Session, map, mode, pft, Token, area, AreaSize));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, Token, area, AreaSize));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);

        // A real stored clone makes order observable: an invalid request must
        // not consume it or replace the currently active profile during recovery.
        Assert.True(ArenaContext.Snapshots.TakeSnapshot(Session));
        using var stale = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(stale.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(1, cloner.Calls);
        Assert.True(ArenaContext.Snapshots.HasSnapshot(Session));
        Assert.Same(original, saves.GetProfile(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);

        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: Area, areaTeamSize: AreaSize);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, areaId: area, areaTeamSize: AreaSize));
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(Area, Sessions.GetAreaId(Session));
        using var armed = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(armed.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(Area, Sessions.GetAreaId(Session));
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Fact]
    public async Task CustomsRouteTakesOneSnapshotReplaysExactAreaAndRejectsTampering()
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = Customs, Mode = Mode, RequestId = Token, AreaId = CustomsArea, AreaTeamSize = AreaSize };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(first.RootElement.GetProperty("snapshotTaken").GetBoolean());
        Assert.Equal(Token, first.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(CustomsArea, first.RootElement.GetProperty("areaId").GetString());
        Assert.Equal(ArenaSessionState.Armed, Sessions.GetStatus(Session).state);
        Assert.Equal(CustomsArea, Sessions.GetAreaId(Session));
        Assert.True(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
        Assert.False(Sessions.IsExternalProfileWriteProtected(Session));

        using var replay = JsonDocument.Parse(await InvokeStart(router,
            request with { Map = Customs.ToUpperInvariant(), Mode = Mode.ToUpperInvariant() }));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(Token, replay.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(CustomsArea, replay.RootElement.GetProperty("areaId").GetString());

        foreach (var tampered in new[]
        {
            request with { AreaId = null },
            request with { AreaId = ArenaAreaCatalog.CustomsFactoryCenterId },
            request with { AreaId = "customs-factory-center-v2" },
            request with { Mode = "tactical" },
            request with { PitFireTeamTeam = true },
            request with { Map = ArenaMapCatalog.FactoryDayId },
            request with { Map = ArenaMapCatalog.FactoryDayId, AreaId = ArenaAreaCatalog.FullMapId },
            request with { RequestId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
        })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, tampered));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(Token, Sessions.GetArmToken(Session));
            Assert.Equal(CustomsArea, Sessions.GetAreaId(Session));
        }
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(CustomsArea, status.RootElement.GetProperty("areaId").GetString());

        string nativeEnd = Start(Customs);
        var location = CustomsLocation();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.Equal(CustomsArea, Sessions.GetAreaId(Session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDisabledCustomsConfigStillRejectsTheCorrectAreaBeforeSnapshot(bool missing)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        if (missing) ArenaContext.Config.Maps.Remove(Customs);
        else ArenaContext.Config.Maps[Customs].Enabled = false;
        using var response = JsonDocument.Parse(await InvokeStart(router, new ArenaRaidStartRequest
            { Map = Customs, Mode = Mode, RequestId = Token, AreaId = CustomsArea, AreaTeamSize = AreaSize }));
        Assert.False(response.RootElement.GetProperty("armed").GetBoolean());
        Assert.Contains("not enabled", response.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, cloner.Calls);
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("replacement")]
    [InlineData("wrong-map")]
    public void CustomsReadinessRejectsTamperedNativeResultsWithoutLosingFailedStartRecovery(string tamper)
    {
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: CustomsArea, areaTeamSize: AreaSize);
        string nativeEnd = Start(Customs);
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        var location = CustomsLocation();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        var response = location;
        if (tamper == "outside")
            location.SpawnPointParams = location.SpawnPointParams!.Append(Point("late", 0, 1, 0, "Coop")).ToList();
        else if (tamper == "replacement")
            response = JsonSerializer.Deserialize<LocationBase>(JsonSerializer.Serialize(location))!;
        else location.Id = ArenaMapCatalog.FactoryDayId;
        Assert.False(Sessions.ValidateAreaResponse(Session, response, out _));
        if (tamper != "replacement")
            Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.False(Sessions.IsClientRaidStarted(Session));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [Fact]
    public void CustomsWholeMapCloneIsRejectedAndOrdinaryRaidPatchLeavesItsSourceUntouched()
    {
        var original = CustomsLocation();
        string before = JsonSerializer.Serialize(original);
        foreach (string? area in new[] { null, "", ArenaAreaCatalog.FullMapId })
        {
            var clone = new SPTarkov.Server.Core.Utils.Cloners.FastCloner().Clone(original)!;
            Assert.Throws<InvalidOperationException>(() =>
                ArenaLocationMutator.Mutate(clone, ArenaContext.Config.Maps[Customs], ArenaContext.Config, Mode, area));
            Assert.Equal(JsonSerializer.Serialize(original.SpawnPointParams), JsonSerializer.Serialize(clone.SpawnPointParams));
            Assert.Equal(before, JsonSerializer.Serialize(original));
        }
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, original);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: CustomsArea, areaTeamSize: AreaSize);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, original);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Start(Customs);
        ArenaGenerateLocationAndLootPatch.Postfix(Other, Customs, original);
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("usec-only")]
    [InlineData("fewer-bots")]
    public void CustomsCapacityFailureRetainsUnmodifiedCloneAndDoesNotReleaseRaid(string failure)
    {
        var location = CustomsLocation();
        if (failure == "empty") location.SpawnPointParams = [];
        else if (failure == "usec-only")
        {
            foreach (var point in location.SpawnPointParams!.Where(point => point.Categories!.Contains("Player")))
                point.Sides = ["Usec"];
        }
        else location.SpawnPointParams = location.SpawnPointParams!.Where(point => point.Id != TestBearBot2).ToList();
        string before = JsonSerializer.Serialize(location);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(
            location, ArenaContext.Config.Maps[Customs], ArenaContext.Config, Mode, CustomsArea, AreaSize));
        Assert.Equal(before, JsonSerializer.Serialize(location));
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: CustomsArea, areaTeamSize: AreaSize);
        string nativeEnd = Start(Customs);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.Equal(before, JsonSerializer.Serialize(location));
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [SkippableFact]
    public void InstalledCustomsCloneRetainsBothFactionsAndSeparatedTwoVersusTwoCapacity()
    {
        var database = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        string path = Path.Combine(database, "locations", Customs, "base.json");
        Skip.IfNot(File.Exists(path), "Installed Customs database fixture is unavailable.");
        var original = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), CustomsJsonOptions)!;
        string before = JsonSerializer.Serialize(original, CustomsJsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(before, CustomsJsonOptions)!;
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: CustomsArea, areaTeamSize: AreaSize);
        string nativeEnd = Start(Customs);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Customs, clone);
        Assert.True(Sessions.ValidateAreaResponse(Session, clone, out var reason), reason);
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));

        var points = clone.SpawnPointParams!.ToArray();
        Assert.Equal(29, points.Length);
        Assert.True(original.SpawnPointParams!.Count() > points.Length);
        Assert.All(points, point => Assert.True(ArenaAreaCatalog.CustomsFactoryCenterTeams.AllowsSpawn(
            point.Position!.Value.X, point.Position.Value.Y, point.Position.Value.Z)));
        var bots = points.Where(point => point.Categories!.Any(category => category is "Bot" or "BotPmc")
                && !string.IsNullOrWhiteSpace(point.BotZoneName))
            .Select(point => point.Position!.Value).Distinct().ToArray();
        Assert.Equal(11, bots.Length);
        Assert.All(points.Where(point => point.Categories!.Any(category => category is "Bot" or "BotPmc")),
            point => Assert.Equal(ArenaContext.Config.Maps[Customs].SpawnZones, point.BotZoneName));
        Assert.Equal("ZoneFactoryCenter", ArenaContext.Config.Maps[Customs].SpawnZones);

        foreach (string faction in new[] { "Usec", "Bear" })
        {
            var players = points.Where(point => point.Categories!.Contains("Player")
                    && point.Sides!.Any(side => side is "All" or "Pmc" || side == faction))
                .Select(point => point.Position!.Value).Distinct().ToArray();
            Assert.Equal(18, players.Length);
            foreach (var player in players)
            {
                Assert.True(players.Any(other => CustomsDistance(player, other) >= 40),
                    $"{faction} start {player} has no 40m respawn alternative.");
                Assert.True(bots.Count(bot => CustomsDistance(player, bot) >= 25) >= 3,
                    $"{faction} start {player} cannot place all three AI participants at least 25m away.");
            }
        }
        Assert.Empty(clone.Waves!);
        // Empty, not "all silenced": the mutator now REMOVES an entry it zeroed,
        // because BotProfileClient orders profiles from every entry in this list
        // without asking ShallSpawn. Assert.All over an empty collection passes
        // without running its lambda, so the old assertion would have gone quiet
        // rather than red.
        Assert.Empty(clone.BossLocationSpawn!);
        Assert.Equal(before, JsonSerializer.Serialize(original, CustomsJsonOptions));
        // Coordinate capacity is not a claim about live NavMesh connectivity.
    }

    private (ArenaModeRouter router, SaveServer saves, SptProfile profile, CountingAreaCloner cloner) CustomsRouter()
    {
        // The real SaveServer constructor initializes its in-memory dictionary.
        // File/persistence dependencies are deliberately absent: this route must
        // only take a snapshot, never save or otherwise write the real profile.
        var saves = new SaveServer(null!, [], null!, null!, null!, null!,
            BotTierScalePatchTests.TestLogger.Create<SaveServer>(), null!);
        var profile = new SptProfile { ProfileInfo = new() { ProfileId = Session } };
        saves.AddProfile(profile);
        var cloner = new CountingAreaCloner();
        ArenaContext.Snapshots = new ProfileSnapshotService(cloner, saves,
            BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
        var router = new ArenaModeRouter(null!, saves, new InRaidConfig
            {
                RaidMenuSettings = new RaidMenuSettings { AiAmount = "AsOnline", AiDifficulty = "AsOnline" },
                CarExtracts = [], CoopExtracts = [],
            }, BotTierScalePatchTests.TestLogger.Create<ArenaModeRouter>());
        return (router, saves, profile, cloner);
    }

    private sealed class CountingAreaCloner : ICloner
    {
        public int Calls { get; private set; }
        public T? Clone<T>(T? value)
        {
            Calls++;
            return new SPTarkov.Server.Core.Utils.Cloners.FastCloner().Clone(value);
        }
    }

    private static double CustomsDistance(Vector3 a, Vector3 b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    // Reuse the fixed, independent native team fixture rather than obsolete
    // arbitrary fixed-2v2 IDs; these tests do not require an installed database.
    private static LocationBase CustomsLocation() => Location();

    private static SpawnPointParam CustomsPoint(string id, float x, float y, float z, string category)
        => new()
        {
            Id = id, Position = new Vector3(x, y, z), Categories = [category],
            Sides = category == "Player" ? ["Pmc"] : ["Savage"],
            BotZoneName = category is "Bot" or "BotPmc" ? "ZoneFactoryCenter" : string.Empty,
        };
}

public sealed class CustomsMapConfigTests
{
    [Theory]
    [InlineData(ArenaMapCatalog.LegacyDefaultClientAllowlist)]
    [InlineData(ArenaMapCatalog.PreviousDefaultClientAllowlist)]
    public void BothExactOlderClientDefaultsGainCustomsAndRetainFactoryNight(string previous)
    {
        string upgraded = ArenaMapCatalog.UpgradeLegacyClientAllowlist(previous);
        Assert.Equal(ArenaMapCatalog.DefaultClientAllowlist, upgraded);
        Assert.True(ArenaMapCatalog.AllowlistContains(upgraded, ArenaMapCatalog.CustomsId));
        Assert.True(ArenaMapCatalog.AllowlistContains(upgraded, ArenaMapCatalog.FactoryNightId));
        Assert.Equal(upgraded, ArenaMapCatalog.UpgradeLegacyClientAllowlist(upgraded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("factory4_day,laboratory")]
    [InlineData("factory4_day,factory4_night,labyrinth,laboratory,")]
    [InlineData("FACTORY4_DAY,FACTORY4_NIGHT,LABYRINTH,LABORATORY")]
    public void UserEditedClientAllowlistsDoNotSilentlyGainCustoms(string customized)
    {
        string upgraded = ArenaMapCatalog.UpgradeLegacyClientAllowlist(customized);
        Assert.Equal(customized, upgraded);
        Assert.False(ArenaMapCatalog.AllowlistContains(upgraded, ArenaMapCatalog.CustomsId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderServerDefaultsGainCustomsWithoutChangingExistingMapSettings(bool includeNight)
    {
        var saved = JsonSerializer.SerializeToNode(new ArenaModeConfig())!;
        var maps = saved["maps"]!.AsObject();
        maps.Remove(ArenaMapCatalog.CustomsId);
        if (!includeNight) maps.Remove(ArenaMapCatalog.FactoryNightId);
        maps[ArenaMapCatalog.FactoryDayId]!["enabled"] = false;
        maps[ArenaMapCatalog.LaboratoryId]!["spawnZones"] = "UserLabZone";
        maps["Woods"] = new JsonObject { ["enabled"] = false, ["spawnZones"] = "UserZone" };
        var before = maps.ToDictionary(entry => entry.Key, entry => entry.Value!.ToJsonString());
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig());
        Assert.True(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.Equal("ZoneFactoryCenter", maps[ArenaMapCatalog.CustomsId]!["spawnZones"]!.GetValue<string>());
        Assert.True(maps[ArenaMapCatalog.CustomsId]!["enabled"]!.GetValue<bool>());
        foreach (var entry in before)
            Assert.Equal(entry.Value, maps[entry.Key]!.ToJsonString());
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
    }

    [Fact]
    public void ExplicitCustomsDisableAndZoneChoiceSurviveDefaultMerge()
    {
        var saved = JsonNode.Parse("""
            { "maps": { "bigmap": { "enabled": false, "spawnZones": "UserCustomsZone", "botMax": 9 } } }
            """)!;
        Assert.True(ConfigStore.FillMissingKeys(saved, JsonSerializer.SerializeToNode(new ArenaModeConfig())));
        var config = saved.Deserialize<ArenaModeConfig>()!;
        Assert.False(config.IsMapEnabled(ArenaMapCatalog.CustomsId));
        Assert.Equal("UserCustomsZone", config.Maps[ArenaMapCatalog.CustomsId].SpawnZones);
        Assert.Equal(9, config.Maps[ArenaMapCatalog.CustomsId].BotMax);
    }
}
