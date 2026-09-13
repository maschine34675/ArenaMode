using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed partial class ArenaAreaServerTests
{
    private const string TeamArea = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
    private static ArenaAreaDefinition TeamDefinition => ArenaAreaCatalog.CustomsFactoryCenterTeams;

    public static IEnumerable<object?[]> InvalidAreaTeamRequests()
    {
        foreach (int? size in new int?[] { null, 0, -1, 1, 6, int.MinValue, int.MaxValue })
            yield return [TeamArea, Customs, Mode, false, size];
        yield return [ArenaAreaCatalog.CustomsFactoryCenterPolygonId, Customs, Mode, false, 2];
        yield return [CustomsArea, Customs, Mode, false, 2];
        yield return [Area, Map, Mode, false, 2];
        yield return [null, Map, Mode, false, 2];
        yield return [TeamArea, Customs, "tactical", false, 2];
        yield return [TeamArea, Customs, Mode, true, 2];
        yield return [TeamArea, Map, Mode, false, 2];
        yield return ["customs-factory-center-teams-v2", Customs, Mode, false, 2];
    }

    [Theory]
    [MemberData(nameof(InvalidAreaTeamRequests))]
    public async Task AreaTeamInvalidChoiceHasNoSnapshotPreparationRecoveryOrArmSideEffects(
        string? area, string map, string mode, bool pft, int? size)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { AreaId = area, AreaTeamSize = size, Map = map, Mode = mode, PitFireTeamTeam = pft, RequestId = Token };
        using var fresh = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(fresh.RootElement.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(fresh.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Equal(0, Sessions.GetAreaTeamSize(Session));
        Assert.False(Sessions.BeginArmPreparation(Session, map, mode, pft, Token, area, size));
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, Token, area, size));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.False(Sessions.IsExternalProfileWriteProtected(Session));

        Assert.True(ArenaContext.Snapshots.TakeSnapshot(Session));
        using var stale = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(stale.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(ArenaContext.Snapshots.HasSnapshot(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));

        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: TeamArea, areaTeamSize: 3);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, Token, area, size));
        using var live = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(live.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(TeamArea, Sessions.GetAreaId(Session));
        Assert.Equal(3, Sessions.GetAreaTeamSize(Session));
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AreaTeamRouteFreezesEchoesAndReplaysOnlyTheExactSize(int size)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = Customs, Mode = Mode, RequestId = Token, AreaId = TeamArea, AreaTeamSize = size };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(size, first.RootElement.GetProperty("areaTeamSize").GetInt32());
        Assert.Equal(TeamArea, first.RootElement.GetProperty("areaId").GetString());
        Assert.Equal(size, Sessions.GetAreaTeamSize(Session));
        Assert.Equal(0, Sessions.GetAreaTeamSize(Other));
        Assert.Equal(1, cloner.Calls);

        using var replay = JsonDocument.Parse(await InvokeStart(router,
            request with { Map = Customs.ToUpperInvariant(), Mode = Mode.ToUpperInvariant() }));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(Token, replay.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(size, replay.RootElement.GetProperty("areaTeamSize").GetInt32());
        foreach (var changed in new[]
        {
            request with { AreaTeamSize = size == 5 ? 2 : size + 1 },
            request with { AreaTeamSize = null },
            request with { AreaTeamSize = 0 },
            request with { AreaId = ArenaAreaCatalog.CustomsFactoryCenterPolygonId, AreaTeamSize = 0 },
            request with { RequestId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
        })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, changed));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(size, Sessions.GetAreaTeamSize(Session));
            Assert.Equal(Token, Sessions.GetArmToken(Session));
        }
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(size, status.RootElement.GetProperty("areaTeamSize").GetInt32());
        Sessions.MarkIdle(Session);
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(0, idle.RootElement.GetProperty("areaTeamSize").GetInt32());
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("idle")]
    [InlineData("failed-start")]
    [InlineData("native-end")]
    public void AreaTeamSizeFollowsItsExactArmThroughRecoveryAndIdle(string path)
    {
        Assert.True(Sessions.BeginArmPreparation(Session, Customs, Mode, false, Token, TeamArea, 4));
        Assert.Equal(4, Sessions.GetAreaTeamSize(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, Customs, Mode, false, Token, out _, TeamArea, 4));
        if (path == "preparing") Sessions.CancelArmPreparation(Session);
        else if (path == "idle") Sessions.MarkIdle(Session);
        else
        {
            Assert.True(Sessions.CompleteArmPreparation(Session));
            Assert.True(Sessions.TryReplayArmedRequest(Session, Customs, Mode, false, Token, out var replay, TeamArea, 4));
            Assert.Equal(Token, replay);
            Assert.False(Sessions.TryReplayArmedRequest(Session, Customs, Mode, false, Token, out _, TeamArea, 3));
            Assert.False(Sessions.TryReplayArmedRequest(Session, Customs, Mode, false, Token, out _, TeamArea));
            string end = Start(Customs);
            Assert.Equal(4, Sessions.GetAreaTeamSize(Session));
            if (path == "failed-start")
            {
                Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
                Assert.Equal(4, Sessions.GetAreaTeamSize(Session));
                Sessions.BeginPostRaidFinalization(Session);
            }
            else
            {
                Assert.True(Sessions.BeginNativeEndFinalization(Session, end));
                Assert.Equal(4, Sessions.GetAreaTeamSize(Session));
                Assert.True(Sessions.CompleteNativeEndFinalization(Session, end));
            }
        }
        Assert.Equal(0, Sessions.GetAreaTeamSize(Session));
    }

    [Fact]
    public async Task AreaTeamLegacyOmissionAndExplicitZeroRemainEquivalent()
    {
        var (router, _, _, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest { Map = Customs, Mode = Mode, RequestId = Token, AreaId = Area };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(0, first.RootElement.GetProperty("areaTeamSize").GetInt32());
        using var replay = JsonDocument.Parse(await InvokeStart(router, request with { AreaTeamSize = 0 }));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(0, Sessions.GetAreaTeamSize(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.False(Sessions.BeginTeamArmPreparation(Other, Map, "tactical", Token, areaTeamSize: 2));
        Assert.False(Sessions.IsExternalProfileWriteProtected(Other));
    }

    [Fact]
    public void AreaTeamRequestDtoRetainsMissingVersusExplicitChoice()
    {
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{}")!.AreaTeamSize);
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"areaTeamSize\":null}")!.AreaTeamSize);
        Assert.Equal(0, JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"areaTeamSize\":0}")!.AreaTeamSize);
        Assert.Equal(5, JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"areaTeamSize\":5}")!.AreaTeamSize);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"areaTeamSize\":2.5}"));
    }

    [SkippableTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AreaTeamInstalledCloneHasCapacityForEitherLocalFactionAndPreservesAllNativeMarkers(int size)
    {
        var source = ReadAreaTeamLocation();
        string before = JsonSerializer.Serialize(source, CustomsJsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(before, CustomsJsonOptions)!;
        var markerList = clone.SpawnPointParams!;
        var expected = markerList.Where(p => TeamDefinition.AllowsSpawn(p.Position!.Value.X,
            p.Position.Value.Y, p.Position.Value.Z, p.Id)).ToArray();
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: TeamArea, areaTeamSize: size);
        string nativeEnd = Start(Customs);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, clone);
        Assert.Equal(28, clone.SpawnPointParams!.Count());
        Assert.Equal(expected, clone.SpawnPointParams);
        Assert.True(Sessions.ValidateAreaResponse(Session, clone, out var reason), reason);
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.Equal(size, Sessions.GetAreaTeamSize(Session));
        foreach (bool usec in new[] { true, false })
        {
            var players = clone.SpawnPointParams!.Where(p => TeamDefinition.TeamStarts!.Allows(usec, true, p.Id)).ToArray();
            var bots = clone.SpawnPointParams!.Where(p => TeamDefinition.TeamStarts!.Allows(usec, false, p.Id)).ToArray();
            Assert.True(players.Length >= 1);
            Assert.Equal(5, bots.Length);
            Assert.True(bots.Length >= size); // this side is all-bot when the local PMC belongs to the other side
            Assert.All(players, p => Assert.Contains(p.Sides!, value => value is "All" or "Pmc" || value == (usec ? "Usec" : "Bear")));
            Assert.All(bots, p => Assert.Contains(p.Categories!, value => value is "Bot" or "BotPmc" or "All"));
            Assert.All(bots, p => Assert.False(string.IsNullOrWhiteSpace(p.BotZoneName)));
            Assert.All(bots, bot => Assert.All(players, player => Assert.True(AreaTeamHorizontal(bot, player) >= 1.5)));
            for (int i = 0; i < bots.Length; i++)
                Assert.All(bots.Skip(i + 1), other => Assert.True(AreaTeamHorizontal(bots[i], other) >= 1.5));
        }
        Assert.DoesNotContain(clone.SpawnPointParams!, p => p.Id == "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e");
        Assert.Equal(before, JsonSerializer.Serialize(source, CustomsJsonOptions));
        string once = JsonSerializer.Serialize(clone, CustomsJsonOptions);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, clone);
        Assert.Equal(once, JsonSerializer.Serialize(clone, CustomsJsonOptions));
        GenerateLocationAndLootPatch.Postfix(Other, Customs, source);
        Assert.Equal(before, JsonSerializer.Serialize(source, CustomsJsonOptions));
        Sessions.MarkIdle(Session);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, source);
        Assert.Equal(before, JsonSerializer.Serialize(source, CustomsJsonOptions));
    }

    [SkippableTheory]
    [InlineData("missing-usec-bot")]
    [InlineData("missing-bear-bot")]
    [InlineData("no-usec-player")]
    [InlineData("no-bear-player")]
    [InlineData("unknown-side")]
    [InlineData("boss-only")]
    [InlineData("unknown-category")]
    [InlineData("missing-zone")]
    [InlineData("duplicate-position")]
    [InlineData("close-despite-height")]
    [InlineData("player-bot-overlap")]
    [InlineData("opposing-sectors-close")]
    [InlineData("outside")]
    [InlineData("duplicate-id")]
    [InlineData("wrong-id")]
    [InlineData("wrong-location")]
    public void AreaTeamTamperingAfterNativeMutationBlocksClientRelease(string tamper)
    {
        var location = ReadAreaTeamLocation();
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: TeamArea, areaTeamSize: 5);
        string nativeEnd = Start(Customs);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var validReason), validReason);
        var points = location.SpawnPointParams!.ToList();
        var westBots = points.Where(p => TeamDefinition.TeamStarts!.Allows(true, false, p.Id)).ToArray();
        var eastBots = points.Where(p => TeamDefinition.TeamStarts!.Allows(false, false, p.Id)).ToArray();
        var westPlayers = points.Where(p => TeamDefinition.TeamStarts!.Allows(true, true, p.Id)).ToArray();
        var eastPlayers = points.Where(p => TeamDefinition.TeamStarts!.Allows(false, true, p.Id)).ToArray();
        switch (tamper)
        {
            case "missing-usec-bot": points.Remove(westBots[0]); break;
            case "missing-bear-bot": points.Remove(eastBots[0]); break;
            case "no-usec-player": foreach (var p in westPlayers) p.Sides = ["Savage"]; break;
            case "no-bear-player": foreach (var p in eastPlayers) p.Sides = ["Usec"]; break;
            case "unknown-side": foreach (var p in westPlayers) p.Sides = ["Pmc", "Unknown"]; break;
            case "boss-only": westBots[0].Categories = ["Boss"]; break;
            case "unknown-category": westBots[0].Categories = ["Bot", "Unknown"]; break;
            case "missing-zone": westBots[0].BotZoneName = ""; break;
            case "duplicate-position": westBots[1].Position = westBots[0].Position; break;
            case "close-despite-height":
                var p0 = westBots[0].Position!.Value;
                westBots[1].Position = new Vector3(p0.X + 0.01f, p0.Y + 100, p0.Z); break;
            case "player-bot-overlap": westPlayers[0].Position = westBots[0].Position; break;
            case "opposing-sectors-close":
                var east = eastBots[0].Position!.Value;
                westBots[0].Position = new Vector3(east.X - 4, east.Y + 20, east.Z); break;
            case "outside": westBots[0].Position = new Vector3(0, 1, 0); break;
            case "duplicate-id": points.Add(westBots[0]); break;
            case "wrong-id": westBots[0].Id = "same-position-unassigned-id"; break;
            case "wrong-location": location.Id = Map; break;
            default: throw new ArgumentOutOfRangeException(nameof(tamper));
        }
        location.SpawnPointParams = points;
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        if (tamper == "close-despite-height") Assert.Contains("1.5m", reason);
        if (tamper == "opposing-sectors-close") Assert.Contains("25m", reason);
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.Equal(5, Sessions.GetAreaTeamSize(Session));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [SkippableFact]
    public void AreaTeamRequiredCapacityUsesSelectedSizeRatherThanMaximumOrAlliedCount()
    {
        var location = ReadAreaTeamLocation();
        var points = location.SpawnPointParams!.ToList();
        foreach (bool usec in new[] { true, false })
        {
            var bots = points.Where(p => TeamDefinition.TeamStarts!.Allows(usec, false, p.Id)).ToArray();
            foreach (var point in bots.Skip(2)) points.Remove(point);
        }
        location.SpawnPointParams = points;
        ArenaLocationMutator.Mutate(location, new(), new(), Mode, TeamArea, 2);
        ArenaLocationMutator.ValidateAreaSpawns(location, Mode, TeamArea, 2);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateAreaSpawns(location, Mode, TeamArea, 3));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateAreaSpawns(location, Mode, TeamArea));
        var west = location.SpawnPointParams!.First(p => TeamDefinition.TeamStarts!.Allows(true, false, p.Id));
        location.SpawnPointParams = location.SpawnPointParams!.Where(p => !ReferenceEquals(p, west)).ToList();
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.ValidateAreaSpawns(location, Mode, TeamArea, 2));
    }

    [SkippableFact]
    public void AreaTeamInvalidStaticStartsFailBeforeMutatingTheCloneAndKeepOrdinaryDataIntact()
    {
        var location = ReadAreaTeamLocation();
        var points = location.SpawnPointParams!.ToList();
        points.Remove(points.First(p => TeamDefinition.TeamStarts!.Allows(false, false, p.Id)));
        location.SpawnPointParams = points;
        string before = JsonSerializer.Serialize(location, CustomsJsonOptions);
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(location, new(), new(), Mode, TeamArea, 5));
        Assert.Same(points, location.SpawnPointParams);
        Assert.Equal(before, JsonSerializer.Serialize(location, CustomsJsonOptions));
        Assert.Throws<InvalidOperationException>(() => ArenaLocationMutator.Mutate(location, new(), new(), Mode, TeamArea, null));
        Assert.Equal(before, JsonSerializer.Serialize(location, CustomsJsonOptions));
    }

    [SkippableFact]
    public void AreaTeamNativeAllCategoriesAndFactionSpecificPmcMasksAreNotMistakenForInvalidStarts()
    {
        var location = ReadAreaTeamLocation();
        foreach (var point in location.SpawnPointParams!)
            foreach (bool usec in new[] { true, false })
            {
                if (TeamDefinition.TeamStarts!.Allows(usec, true, point.Id))
                {
                    point.Categories = ["All"];
                    point.Sides = [usec ? "Usec" : "Bear"];
                }
                if (TeamDefinition.TeamStarts.Allows(usec, false, point.Id)) point.Categories = ["All"];
            }
        ArenaLocationMutator.Mutate(location, new(), new(), Mode, TeamArea, 5);
        ArenaLocationMutator.ValidateAreaSpawns(location, Mode, TeamArea, 5);
        Assert.Equal(28, location.SpawnPointParams!.Count());
    }

    private static LocationBase ReadAreaTeamLocation()
    {
        string database = Environment.GetEnvironmentVariable("SPT_DATABASE") ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        string path = Path.Combine(database, "locations", Customs, "base.json");
        Skip.IfNot(File.Exists(path), "Set SPT_DATABASE to the installed SPT database directory.");
        return JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), CustomsJsonOptions)!;
    }

    private static double AreaTeamHorizontal(SpawnPointParam a, SpawnPointParam b)
    {
        var ap = a.Position!.Value; var bp = b.Position!.Value;
        double x = (double)ap.X - bp.X, z = (double)ap.Z - bp.Z;
        return Math.Sqrt(x * x + z * z);
    }
}
