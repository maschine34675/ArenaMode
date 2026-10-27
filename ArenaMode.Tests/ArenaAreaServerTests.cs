using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed partial class ArenaAreaServerTests : IDisposable
{
    private const string Map = ArenaMapCatalog.FactoryDayId;
    private const string BoundedMap = ArenaMapCatalog.CustomsId;
    private const string Mode = "teamdeathmatch";
    private const string Area = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
    private const int AreaSize = 2;
    // Fixed native IDs/coordinates, deliberately not derived from the production
    // team-start sets: admission changes must not silently rewrite this fixture.
    private const string TestUsecPlayer = "2051eb7b-3ae8-49b8-adcb-c13f0ddc7972";
    private const string TestBearPlayer = "2cb9950d-20ec-484e-919c-5489bb7abd5c";
    private const string TestUsecBot1 = "046309a0-3d13-48c4-a3e6-9bb509ac339d";
    private const string TestUsecBot2 = "570f30b0-d2da-4f91-9270-45783c7c6a88";
    private const string TestBearBot1 = "13adbe1b-21b0-4422-90a9-eab3ee8313fb";
    private const string TestBearBot2 = "295048b1-140d-47c3-85f8-ab5bdaaafa67";
    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId Other = new("89abcdef0123456701234567");
    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ProfileSnapshotService? _oldSnapshots = ArenaContext.Snapshots;
    private readonly ArenaModeConfig _oldConfig = ArenaContext.Config;
    private readonly bool _oldUpdateGate = ArenaMode.Server.Rewards.RewardReturnQuiescence.Enabled;
    private static readonly PropertyInfo UpdateGateEnabled = typeof(ArenaMode.Server.Rewards.RewardReturnQuiescence)
        .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _oldRouter = RouterInstance.GetValue(null);
    private static readonly FieldInfo RouterInstance = typeof(ArenaModeRouter).GetField(
        "Instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private ArenaSessionService Sessions => ArenaContext.Sessions!;

    public ArenaAreaServerTests()
    {
        UpdateGateEnabled.SetValue(null, true);
        ArenaContext.Config = new ArenaModeConfig
        {
            Maps = new(StringComparer.OrdinalIgnoreCase)
            {
                [BoundedMap] = new ArenaModeConfig().Maps[ArenaMapCatalog.CustomsId],
                [ArenaMapCatalog.FactoryDayId] = new() { Enabled = true },
            },
        };
        ArenaContext.Sessions = new(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
    }

    public void Dispose()
    {
        UpdateGateEnabled.SetValue(null, _oldUpdateGate);
        ArenaContext.Sessions = _oldSessions;
        ArenaContext.Snapshots = _oldSnapshots;
        ArenaContext.Config = _oldConfig;
        RouterInstance.SetValue(null, _oldRouter);
    }

    [Fact]
    public async Task MissingNativeUpdateProtectionRejectsArenaBeforeConsumingTheSnapshot()
    {
        var router = RouterWithSnapshot();
        UpdateGateEnabled.SetValue(null, false);
        using var response = JsonDocument.Parse(await InvokeStart(router, new ArenaRaidStartRequest
        {
            RequestId = Token, Map = Map, Mode = Mode,
        }));
        Assert.False(response.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal("Arena profile protection unavailable", response.RootElement.GetProperty("reason").GetString());
        Assert.True(ArenaContext.Snapshots!.HasSnapshot(Session));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(ArenaAreaCatalog.FullMapId)]
    public void LegacyAndExplicitFullMapArmsHaveTheSameReplayIdentity(string? area)
    {
        const string wholeMap = ArenaMapCatalog.FactoryDayId;
        Sessions.Arm(Session, wholeMap, Mode, armToken: Token, areaId: area);
        Assert.Equal(ArenaAreaCatalog.FullMapId, Sessions.GetAreaId(Session));
        Assert.True(Sessions.TryReplayArmedRequest(Session, wholeMap, Mode, false, Token,
            out var replay, ArenaAreaCatalog.FullMapId));
        Assert.Equal(Token, replay);
        Assert.False(Sessions.TryReplayArmedRequest(Session, wholeMap, Mode, false, Token, out _, Area, AreaSize));
    }

    [Fact]
    public void FrozenAreaDoesNotReplayAsFullMapOrAnotherLogicalRequest()
    {
        Assert.True(Sessions.BeginArmPreparation(Session, BoundedMap, Mode, false, Token, Area, AreaSize));
        Assert.Equal(Area, Sessions.GetAreaId(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, false, Token, out _, Area, AreaSize));
        Assert.True(Sessions.CompleteArmPreparation(Session));
        Assert.True(Sessions.TryReplayArmedRequest(Session, BoundedMap.ToUpperInvariant(), Mode.ToUpperInvariant(),
            false, Token, out var replay, Area, AreaSize));
        Assert.Equal(Token, replay);
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, false, Token, out _));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, false, Token, out _, "factory-south-v2"));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, false,
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", out _, Area, AreaSize));
        Assert.False(Sessions.TryReplayArmedRequest(Other, BoundedMap, Mode, false, Token, out _, Area, AreaSize));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, true, Token, out _, Area, AreaSize));
        Start(BoundedMap);
        Assert.Equal(Area, Sessions.GetAreaId(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Mode, false, Token, out _, Area, AreaSize));
    }

    [Theory]
    [InlineData("factory-south-v2", BoundedMap, Mode, false)]
    [InlineData(ArenaAreaCatalog.FactorySouthId, ArenaMapCatalog.FactoryDayId, Mode, false)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterId, BoundedMap, Mode, false)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterPolygonId, BoundedMap, Mode, false)]
    [InlineData("customs-factory-center-teams-v1", BoundedMap, Mode, false)]
    [InlineData(Area, "factory4_night", Mode, false)]
    [InlineData(Area, "laboratory", Mode, false)]
    [InlineData(Area, BoundedMap, "tactical", false)]
    [InlineData(Area, BoundedMap, "gungame", false)]
    [InlineData(Area, BoundedMap, Mode, true)]
    public void InvalidAreaCannotPublishPreparationOrReplaceAnExistingArm(
        string area, string map, string mode, bool pft)
    {
        Assert.False(Sessions.BeginArmPreparation(Session, map, mode, pft, Token, area, AreaSize));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Equal(ArenaAreaCatalog.FullMapId, Sessions.GetAreaId(Session));
        Sessions.Arm(Session, BoundedMap, Mode, armToken: Token, areaId: Area, areaTeamSize: AreaSize);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, areaId: area, areaTeamSize: AreaSize));
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(Area, Sessions.GetAreaId(Session));
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("idle")]
    [InlineData("failed-start")]
    [InlineData("native-end")]
    public void AreaIsReleasedOnlyWhenItsLifecycleReachesIdle(string path)
    {
        Assert.True(Sessions.BeginArmPreparation(Session, BoundedMap, Mode, false, Token, Area, AreaSize));
        if (path == "preparing") Sessions.CancelArmPreparation(Session);
        else if (path == "idle") Sessions.MarkIdle(Session);
        else
        {
            Assert.True(Sessions.CompleteArmPreparation(Session));
            string nativeEnd = Start(BoundedMap);
            if (path == "failed-start")
            {
                Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
                Assert.Equal(Area, Sessions.GetAreaId(Session));
                Sessions.BeginPostRaidFinalization(Session);
            }
            else
            {
                Assert.True(Sessions.BeginNativeEndFinalization(Session, nativeEnd));
                Assert.Equal(Area, Sessions.GetAreaId(Session));
                Assert.True(Sessions.CompleteNativeEndFinalization(Session, nativeEnd));
            }
        }
        Assert.Equal(ArenaAreaCatalog.FullMapId, Sessions.GetAreaId(Session));
        Assert.Equal(ArenaAreaCatalog.FullMapId, Sessions.GetAreaId(Other));
    }

    [Fact]
    public async Task InvalidRouteAreaIsRejectedBeforeEvenStaleSnapshotRecovery()
    {
        var router = RouterWithSnapshot();
        var request = new ArenaRaidStartRequest { Map = BoundedMap, Mode = Mode, RequestId = Token, AreaId = "unknown-area" };
        using var response = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(response.RootElement.GetProperty("armed").GetBoolean());
        Assert.Contains("Unknown or incompatible Arena area", response.RootElement.GetProperty("reason").GetString());
        Assert.True(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
    }

    [Fact]
    public async Task RouteReplayAndStatusEchoTheFrozenAreaAndRejectDowngrade()
    {
        var router = RouterWithSnapshot();
        Sessions.Arm(Session, BoundedMap, Mode, armToken: Token, areaId: Area, areaTeamSize: AreaSize);
        var request = new ArenaRaidStartRequest { Map = BoundedMap, Mode = Mode, RequestId = Token, AreaId = Area, AreaTeamSize = AreaSize };
        using var replay = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(Token, replay.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(Area, replay.RootElement.GetProperty("areaId").GetString());
        using var mismatch = JsonDocument.Parse(await InvokeStart(router, request with { AreaId = null }));
        Assert.False(mismatch.RootElement.GetProperty("armed").GetBoolean());
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(Area, status.RootElement.GetProperty("areaId").GetString());
        Sessions.MarkIdle(Session);
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(ArenaAreaCatalog.FullMapId, idle.RootElement.GetProperty("areaId").GetString());
    }

    [Fact]
    public void CloneFiltersEveryCategoryAndPreservesOriginalRecordsAndOtherRaids()
    {
        var original = Location();
        string sourceJson = JsonSerializer.Serialize(original);
        var clone = JsonSerializer.Deserialize<LocationBase>(sourceJson)!;
        ArmAndStart();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, clone);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
        Assert.Equal(new[] { TestUsecPlayer, TestBearPlayer, TestUsecBot1, TestUsecBot2, TestBearBot1, TestBearBot2, "inside-boss", "inside-unknown" },
            clone.SpawnPointParams!.Select(point => point.Id));
        Assert.True(Sessions.ValidateAreaLocation(Session, out var reason), reason);
        Assert.True(Sessions.TryGetStashedCloneMode(clone, out var mode));
        Assert.Equal(Mode, mode);
        string once = JsonSerializer.Serialize(clone);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, clone);
        Assert.Equal(once, JsonSerializer.Serialize(clone));

        // The direct mutator also copies only the list; its retained marker
        // records remain byte-for-byte unchanged even if the caller aliases them.
        var aliased = Location();
        var markerList = aliased.SpawnPointParams;
        string markersBefore = JsonSerializer.Serialize(markerList);
        ArenaLocationMutator.Mutate(aliased, new(), new(), Mode, Area, AreaSize);
        Assert.NotSame(markerList, aliased.SpawnPointParams);
        Assert.Equal(markersBefore, JsonSerializer.Serialize(markerList));
        Assert.Same(markerList!.First(), aliased.SpawnPointParams!.First());

        ArenaGenerateLocationAndLootPatch.Postfix(Other, BoundedMap, original);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
        Sessions.MarkIdle(Session);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, original);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
        Sessions.Arm(Session, BoundedMap, Mode, areaId: Area, areaTeamSize: AreaSize);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, original);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ArenaAreaCatalog.FullMapId)]
    public void FullMapNeverFiltersEvenOutsideOrUnusableSpawnRecords(string? area)
    {
        var location = Location();
        location.Id = ArenaMapCatalog.FactoryDayId;
        var points = location.SpawnPointParams;
        string before = JsonSerializer.Serialize(points);
        ArenaLocationMutator.Mutate(location, new(), new(), Mode, area);
        Assert.Same(points, location.SpawnPointParams);
        Assert.Equal(before, JsonSerializer.Serialize(location.SpawnPointParams));
    }

    [Theory]
    [InlineData("Pmc")]
    [InlineData("pmc")]
    public void NativePmcSideMaskProvidesEntryAndRespawnChoicesForBothFactions(string side)
    {
        var location = Location();
        foreach (var point in location.SpawnPointParams!.Where(point => point.Categories!.Contains("Player")))
            point.Sides = [side];
        string nativeEnd = ArmAndStart();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, location);
        Assert.Equal(2, location.SpawnPointParams!.Count(point => point.Categories!.Contains("Player")));
        Assert.All(location.SpawnPointParams!.Where(point => point.Categories!.Contains("Player")),
            point => Assert.Equal([side], point.Sides));
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no-bots")]
    [InlineData("no-players")]
    [InlineData("wrong-side")]
    [InlineData("duplicate-position")]
    [InlineData("duplicate-id")]
    public void InsufficientOrAmbiguousAreaSpawnsRejectAtomicallyAndCannotConfirm(string failure)
    {
        var location = Location();
        var points = location.SpawnPointParams!.ToList();
        if (failure == "empty") points.Clear();
        if (failure == "no-bots") points.RemoveAll(point => point.Categories!.Any(category => category is "Bot" or "BotPmc" or "Boss"));
        if (failure == "no-players") points.RemoveAll(point => point.Categories!.Contains("Player"));
        if (failure == "wrong-side") foreach (var point in points) point.Sides = ["Savage"];
        if (failure == "duplicate-position") points.Single(point => point.Id == TestBearPlayer).Position = points[0].Position;
        if (failure == "duplicate-id") points.Single(point => point.Id == TestBearPlayer).Id = TestUsecPlayer;
        location.SpawnPointParams = points;
        string before = JsonSerializer.Serialize(location);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(location, new(), new(), Mode, Area, AreaSize));
        Assert.Equal(before, JsonSerializer.Serialize(location));

        string nativeEnd = ArmAndStart();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, location);
        Assert.Equal(before, JsonSerializer.Serialize(location));
        Assert.False(Sessions.ValidateAreaLocation(Session, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.False(Sessions.IsClientRaidStarted(Session));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [Fact]
    public void ConfirmationRequiresReadyExactCloneAndStillRejectsForgedIdentity()
    {
        string nativeEnd = ArmAndStart();
        Assert.False(Sessions.ValidateAreaLocation(Session, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Sessions.MarkLocationMutationReady(Session);
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, Location());
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, "forged"));
        Assert.False(Sessions.ConfirmClientRaidStarted(Other, Token, nativeEnd));
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.False(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [Fact]
    public void NativeResponseRejectsAReplacedCloneOrOutsidePointsAddedAfterFiltering()
    {
        string nativeEnd = ArmAndStart();
        var location = Location();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        Assert.False(Sessions.ValidateAreaResponse(Session, null, out _));
        Assert.False(Sessions.ValidateAreaResponse(Session,
            JsonSerializer.Deserialize<LocationBase>(JsonSerializer.Serialize(location)), out _));
        location.SpawnPointParams = location.SpawnPointParams!.Append(Point("late-outside", 90, 0, 0, "Bot")).ToList();
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out var outside));
        Assert.Contains("outside or invalid spawn", outside);
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("disabled")]
    [InlineData("wrong-clone-map")]
    public void MissingDisabledOrWrongMapCloneCannotBecomePlayable(string failure)
    {
        string nativeEnd = ArmAndStart();
        var location = Location();
        if (failure == "disabled") ArenaContext.Config.Maps[BoundedMap].Enabled = false;
        if (failure == "wrong-clone-map") location.Id = "laboratory";
        ArenaGenerateLocationAndLootPatch.Postfix(Session, BoundedMap, failure == "null" ? null! : location);
        Assert.False(Sessions.ValidateAreaLocation(Session, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
    }

    [Fact]
    public void FullMapConfirmationKeepsItsExistingNoAreaReadinessRequirement()
    {
        Sessions.Arm(Session, ArenaMapCatalog.FactoryDayId, Mode, armToken: Token);
        string nativeEnd = Start(ArenaMapCatalog.FactoryDayId);
        Assert.True(Sessions.ValidateAreaLocation(Session, out _));
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
    }

    private string ArmAndStart()
    {
        Sessions.Arm(Session, BoundedMap, Mode, armToken: Token, areaId: Area, areaTeamSize: AreaSize);
        return Start(BoundedMap);
    }

    private string Start(string map = Map)
    {
        string identity = Protocol.BuildNativeStartIdentity(Token);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            Sessions.ProcessLocalRaidStart(Session, map, identity));
        Assert.True(Sessions.BindNativeEndIdentity(Session, identity, "factory.Pmc area-test", out var nativeEnd));
        return nativeEnd!;
    }

    private ArenaModeRouter RouterWithSnapshot()
    {
        // Real route and snapshot storage, deliberately no SaveServer/cloner:
        // these cases must never perform profile restoration or persistence.
        var snapshots = new ProfileSnapshotService(null!, null!,
            BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
        var store = (Dictionary<string, SptProfile>)typeof(ProfileSnapshotService).GetField(
            "_snapshots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshots)!;
        store[Session.ToString()] = new SptProfile();
        ArenaContext.Snapshots = snapshots;
        return new ArenaModeRouter(null!, null!, new InRaidConfig
            {
                RaidMenuSettings = new RaidMenuSettings { AiAmount = "AsOnline", AiDifficulty = "AsOnline" },
                CarExtracts = [],
                CoopExtracts = [],
            },
            BotTierScalePatchTests.TestLogger.Create<ArenaModeRouter>());
    }

    private static async Task<string> InvokeStart(ArenaModeRouter router, ArenaRaidStartRequest request)
        => await (ValueTask<string>)typeof(ArenaModeRouter).GetMethod("HandleRaidStartLocked",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(router, [request, Session, CancellationToken.None])!;

    private static async Task<string> InvokeStatus(ArenaModeRouter router)
        => await (ValueTask<string>)typeof(ArenaModeRouter).GetMethod("HandleStatus",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(router, [Session, CancellationToken.None])!;

    private static LocationBase Location() => new()
    {
        Id = BoundedMap,
        Waves = [],
        BossLocationSpawn = [],
        SpawnPointParams =
        [
            CustomsPoint(TestUsecPlayer, 317.338f, 1.128f, -78.756f, "Player"),
            CustomsPoint(TestBearPlayer, 478f, 2.7f, -83.14f, "Player"),
            CustomsPoint(TestUsecBot1, 363.140869f, 1.32006574f, -109.224533f, "BotPmc"),
            CustomsPoint(TestUsecBot2, 327.3948f, 1.52012908f, -102.241684f, "BotPmc"),
            CustomsPoint(TestBearBot1, 391.856567f, 1.50502157f, -86.1867752f, "Bot"),
            CustomsPoint(TestBearBot2, 471.196716f, 2.6568923f, -54.2984543f, "Bot"),
            Point("inside-boss", 450, 0, -100, "Boss"),
            Point("inside-unknown", 380, 0, -50, "ModCategory"),
            Point("out-player", 80, 0, 0, "Player"), Point("out-bot", 80, 0, 0, "Bot"),
            Point("out-coop", 80, 0, 0, "Coop"), Point("out-group", 80, 0, 0, "Group"),
            Point("out-unknown", 80, 0, 0, "ModCategory"), Point("outside-low", 0, -400, 0, "Player"),
            Point("outside-high", 0, 500, 0, "Bot"), Point("wall-inset", 464.603851f, 0, -145.668182f, "Bot"),
            new SpawnPointParam { Id = "missing-position", Categories = ["Bot"] },
        ],
    };

    private static SpawnPointParam Point(string id, float x, float y, float z, string category)
        => new() { Id = id, Position = new Vector3(x, y, z), Categories = [category], Sides = ["All"], BotZoneName = "BotZone" };
}
