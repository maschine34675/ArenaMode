using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace ArenaMode.Tests;

public sealed class CustomsContourTests
{
    private const string SniperId = "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e";
    private static ArenaAreaDefinition Area => ArenaAreaCatalog.CustomsFactoryCenterPolygon;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(), new StringToMongoIdConverter(), new ListOrTConverterFactory() },
    };

    [Fact]
    public void CustomsContourPreservesTheFifteenUserSurveyCoordinatesAndUsesANewIdentity()
    {
        // Independent copy from the exported 20260907-192946 draft, not a
        // generated expected value from the catalogue under test.
        const string survey = """
            [[464.7143,-145.724579],[467.552673,-128.753784],[485.3326,-117.640137],
             [493.686554,-103.316223],[505.736938,-27.9927254],[506.786652,-24.71637],
             [379.45694,-7.35720825],[372.48645,-10.0381222],[355.6324,-8.132172],
             [349.910278,-3.30896],[327.595,-1.9775393],[311.6721,-104.798462],
             [315.104736,-112.206207],[322.2989,-117.058868],[330.003784,-129.5967]]
            """;
        using var expected = JsonDocument.Parse(survey);
        Assert.Equal(15, Area.Bounds.Outline.Count);
        for (int index = 0; index < 15; index++)
        {
            Assert.Equal(expected.RootElement[index][0].GetSingle(), Area.Bounds.Outline[index].X);
            Assert.Equal(expected.RootElement[index][1].GetSingle(), Area.Bounds.Outline[index].Z);
        }
        Assert.Equal("customs-factory-center-polygon-v1", Area.Id);
        Assert.NotEqual(ArenaAreaCatalog.CustomsFactoryCenter.Id, Area.Id);
        Assert.Equal(ArenaMapCatalog.CustomsId, Area.MapId);
        Assert.Equal(2, Area.MaxTeamSize);
        Assert.True(Area.Bounds.IsValid);
        Assert.False(Area.Bounds.LimitsHeight);
    }

    [Theory]
    [InlineData(-10000f)]
    [InlineData(-1f)]
    [InlineData(1f)]
    [InlineData(20f)]
    [InlineData(10000f)]
    public void CustomsContourActorsSpawnsAndPathsIgnoreHeightButKeepHorizontalContainment(float height)
    {
        Assert.True(Area.Bounds.Contains(400, height, -80));
        Assert.True(Area.AllowsSpawn(400, height, -80, "ordinary-native-point"));
        Assert.True(Area.Bounds.ContainsPath(new ArenaAreaPoint(390, -10000, -80),
            new[] { new ArenaAreaPoint(400, height, -80), new ArenaAreaPoint(410, 10000, -80) },
            true, ArenaAreaCatalog.NavigationInset));
        Assert.False(Area.Bounds.Contains(520, height, -80));
        Assert.False(Area.AllowsSpawn(520, height, -80, "ordinary-native-point"));
        Assert.False(Area.Bounds.ContainsPath(new ArenaAreaPoint(400, height, -80),
            new[] { new ArenaAreaPoint(520, height, -80) }, true));
        var edge = Area.Bounds.Outline[0];
        Assert.True(Area.Bounds.Contains(edge.X, height, edge.Z));
        Assert.False(Area.AllowsSpawn(edge.X, height, edge.Z, "edge-point"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void CustomsContourNeverAcceptsNonfiniteCoordinatesEvenWithoutHeightLimits(float invalid)
    {
        Assert.False(Area.Bounds.Contains(400, invalid, -80));
        Assert.False(Area.Bounds.Contains(invalid, 1, -80));
        Assert.False(Area.Bounds.Contains(400, 1, invalid));
        Assert.False(Area.AllowsSpawn(400, invalid, -80));
        Assert.False(Area.Bounds.ContainsPath(new ArenaAreaPoint(400, 1, -80),
            new[] { new ArenaAreaPoint(410, invalid, -80) }, true));
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(Area.Bounds.Outline, invalid, 12, limitsHeight: false));
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(Area.Bounds.Outline, 0, invalid, limitsHeight: false));
    }

    [Fact]
    public void CustomsContourHeightOptOutDoesNotPermitShortcutsAcrossConcaveNotches()
    {
        var unlimited = new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(0, 0), new ArenaAreaVertex(10, 0), new ArenaAreaVertex(10, 10),
            new ArenaAreaVertex(7, 10), new ArenaAreaVertex(7, 3), new ArenaAreaVertex(3, 3),
            new ArenaAreaVertex(3, 10), new ArenaAreaVertex(0, 10),
        }, 0, 12, limitsHeight: false);
        var left = new ArenaAreaPoint(1, -100, 8);
        var right = new ArenaAreaPoint(9, 100, 8);
        Assert.True(unlimited.Contains(left.X, left.Y, left.Z));
        Assert.True(unlimited.Contains(right.X, right.Y, right.Z));
        Assert.False(unlimited.ContainsPath(left, new[] { right }, true));
        Assert.False(unlimited.ContainsPath(left, new[] { right }, true, ArenaAreaCatalog.NavigationInset));
        Assert.True(unlimited.ContainsPath(left, new[]
            { new ArenaAreaPoint(1, -10, 1), new ArenaAreaPoint(9, 30, 1), right },
            true, ArenaAreaCatalog.NavigationInset));
        Assert.False(unlimited.ContainsPath(left, new[] { right }, false));
    }

    [Fact]
    public void CustomsContourExclusionsAreExactImmutableAndIndependentOfHeight()
    {
        Assert.True(Area.IsExcludedSpawn(SniperId));
        Assert.False(Area.IsExcludedSpawn(SniperId.ToUpperInvariant()));
        Assert.False(Area.IsExcludedSpawn(null));
        foreach (float height in new[] { -100f, 1f, 100f })
        {
            Assert.False(Area.AllowsSpawn(400, height, -80, SniperId));
            Assert.True(Area.AllowsSpawn(400, height, -80, "other-sniper-or-ground-point"));
        }
        var excluded = new List<string> { SniperId };
        var copy = new ArenaAreaDefinition("immutable-test", "test", Area.MapId,
            Area.Bounds, 0.5f, 3f, 2, excluded);
        excluded.Clear();
        excluded.Add("other-point");
        Assert.True(copy.IsExcludedSpawn(SniperId));
        Assert.False(copy.IsExcludedSpawn("other-point"));
        Assert.Throws<ArgumentException>(() => new ArenaAreaDefinition("invalid-test", "test", Area.MapId,
            Area.Bounds, 0.5f, 3f, 2, new[] { "" }));
    }

    [Fact]
    public void CustomsContourDoesNotChangeLegacyHeightBandsOrIdentities()
    {
        var customs = ArenaAreaCatalog.CustomsFactoryCenter;
        var factory = ArenaAreaCatalog.FactorySouth;
        Assert.Equal("customs-factory-center-v1", customs.Id);
        Assert.Equal("factory-south-v1", factory.Id);
        Assert.True(customs.Bounds.LimitsHeight);
        Assert.True(factory.Bounds.LimitsHeight);
        Assert.Equal(4, customs.Bounds.Outline.Count);
        Assert.Equal(305, customs.Bounds.MinX);
        Assert.Equal(420, customs.Bounds.MaxX);
        Assert.Equal(-135, customs.Bounds.MinZ);
        Assert.Equal(-65, customs.Bounds.MaxZ);
        Assert.True(customs.AllowsSpawn(350, 1, -80));
        Assert.False(customs.AllowsSpawn(350, -1, -80));
        Assert.False(customs.AllowsSpawn(350, 4, -80));
        Assert.True(customs.Bounds.Contains(350, 4, -80));
        Assert.False(customs.Bounds.Contains(350, 13, -80));
        Assert.False(customs.Bounds.Contains(350, -1, -80));
        Assert.True(factory.AllowsSpawn(0, 1, 0));
        Assert.False(factory.AllowsSpawn(0, 3, 0));
        Assert.False(factory.Bounds.Contains(0, 11, 0));
        Assert.False(customs.IsExcludedSpawn(SniperId));
        Assert.False(factory.IsExcludedSpawn(SniperId));
    }

    [SkippableFact]
    public void CustomsContourInstalledDatabaseKeepsSeventeenPmcElevenBotPositionsAndSafeSpacing()
    {
        var database = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        string path = Path.Combine(database, "locations", ArenaMapCatalog.CustomsId, "base.json");
        Skip.IfNot(File.Exists(path), "Installed Customs database fixture is unavailable.");
        var original = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), JsonOptions)!;
        string before = JsonSerializer.Serialize(original, JsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(before, JsonOptions)!;
        var config = new ArenaModeConfig();
        ArenaLocationMutator.Mutate(clone, config.Maps[Area.MapId], config, "teamdeathmatch", Area.Id);
        ArenaLocationMutator.ValidateAreaSpawns(clone, "teamdeathmatch", Area.Id);
        var points = clone.SpawnPointParams!.ToArray();
        Assert.DoesNotContain(points, point => point.Id == SniperId);
        var sniper = Assert.Single(original.SpawnPointParams!, point => point.Id == SniperId);
        var sniperPosition = sniper.Position!.Value;
        Assert.True(Area.Bounds.Contains(sniperPosition.X, sniperPosition.Y, sniperPosition.Z, ArenaAreaCatalog.SpawnInset));
        Assert.False(Area.AllowsSpawn(sniperPosition.X, sniperPosition.Y, sniperPosition.Z, sniper.Id));
        Assert.All(points, point => Assert.True(Area.AllowsSpawn(
            point.Position!.Value.X, point.Position.Value.Y, point.Position.Value.Z, point.Id)));
        var bots = points.Where(point => point.Categories!.Any(category => category is "Bot" or "BotPmc" or "Boss")
                && !ArenaBotSpawnPolicy.IsExcludedZone(Area.MapId, point.BotZoneName)
                && !ArenaBotSpawnPolicy.IsExcludedPoint(Area.MapId, point.Id))
            .Select(point => point.Position!.Value).Distinct().ToArray();
        Assert.Equal(11, bots.Length);
        foreach (string faction in new[] { "Usec", "Bear" })
        {
            var players = points.Where(point => point.Categories!.Contains("Player")
                    && point.Sides!.Any(side => side is "All" or "Pmc" || side == faction))
                .Select(point => point.Position!.Value).Distinct().ToArray();
            Assert.Equal(17, players.Length);
            foreach (var player in players)
            {
                Assert.True(players.Any(other => Distance(player, other) >= 40),
                    $"{faction} start {player} has no 40m respawn alternative.");
                Assert.True(bots.Count(bot => Distance(player, bot) >= 25) >= 3,
                    $"{faction} start {player} cannot place all three AI participants at least 25m away.");
            }
        }
        Assert.Empty(clone.Waves!);
        Assert.All(clone.BossLocationSpawn!, spawn => Assert.Equal(0, spawn.BossChance));
        Assert.Equal(before, JsonSerializer.Serialize(original, JsonOptions));
        // Source/database immutability and coordinate capacity are verified;
        // scene connectivity still belongs to the live NavMesh readiness checks.
    }

    private static double Distance(Vector3 first, Vector3 second)
    {
        double x = first.X - second.X, y = first.Y - second.Y, z = first.Z - second.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
}

public sealed partial class ArenaAreaServerTests
{
    [Fact]
    public async Task CustomsContourRouteFreezesTheNewVersionAndCannotReplayAsTheOldRectangle()
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = Customs, Mode = Mode, RequestId = Token, AreaId = ArenaAreaCatalog.CustomsFactoryCenterPolygonId };
        using var armed = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(armed.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(request.AreaId, armed.RootElement.GetProperty("areaId").GetString());
        Assert.Equal(1, cloner.Calls);
        Assert.True(ArenaContext.Snapshots!.HasSnapshot(Session));
        foreach (var invalid in new[]
        {
            request with { AreaId = CustomsArea },
            request with { AreaId = ArenaAreaCatalog.FullMapId },
            request with { AreaId = "customs-factory-center-polygon-v2" },
            request with { AreaId = request.AreaId.ToUpperInvariant() },
            request with { Map = Map },
            request with { Mode = "tactical" },
            request with { PitFireTeamTeam = true },
        })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, invalid));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(Token, Sessions.GetArmToken(Session));
            Assert.Equal(request.AreaId, Sessions.GetAreaId(Session));
            Assert.Equal(1, cloner.Calls);
            Assert.Same(original, saves.GetProfile(Session));
        }
        using var replayed = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(replayed.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(1, cloner.Calls);
    }

    [Theory]
    [InlineData("endless", Customs, false)]
    [InlineData("rounds", Customs, false)]
    [InlineData("gungame", Customs, false)]
    [InlineData("tactical", Customs, false)]
    [InlineData(Mode, Map, false)]
    [InlineData(Mode, Customs, true)]
    public async Task CustomsContourInvalidStartCannotTakeSnapshotOrPublishArm(string mode, string map, bool pft)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        using var rejected = JsonDocument.Parse(await InvokeStart(router, new ArenaRaidStartRequest
        {
            Map = map, Mode = mode, PitFireTeamTeam = pft, RequestId = Token,
            AreaId = ArenaAreaCatalog.CustomsFactoryCenterPolygonId,
        }));
        Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Theory]
    [InlineData("excluded-id")]
    [InlineData("outside")]
    [InlineData("duplicate")]
    public void CustomsContourFinalSpawnCheckRejectsLateTamperingWithoutReleasingTheRaid(string tamper)
    {
        const string contour = ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
        const string sniperId = "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e";
        var location = CustomsLocation();
        location.SpawnPointParams = location.SpawnPointParams!.Concat(new[]
        {
            CustomsPoint("low-player", 400, -40, -80, "Player"),
            CustomsPoint("high-bot", 400, 80, -80, "Bot"),
            CustomsPoint(sniperId, 400, 1, -80, "Bot"),
        }).ToList();
        string before = JsonSerializer.Serialize(location);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.Equal(before, JsonSerializer.Serialize(location)); // no ordinary-raid mutation
        Sessions.Arm(Session, Customs, Mode, armToken: Token, areaId: contour);
        string nativeEnd = Start(Customs);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        Assert.Contains(location.SpawnPointParams!, point => point.Id == "low-player");
        Assert.Contains(location.SpawnPointParams!, point => point.Id == "high-bot");
        Assert.DoesNotContain(location.SpawnPointParams!, point => point.Id == sniperId);
        SpawnPointParam late = tamper switch
        {
            "excluded-id" => CustomsPoint(sniperId, 400, 1, -80, "Bot"),
            "outside" => CustomsPoint("late-outside", 520, 1, -80, "Coop"),
            _ => location.SpawnPointParams!.First(),
        };
        location.SpawnPointParams = location.SpawnPointParams!.Append(late).ToList();
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.False(Sessions.IsClientRaidStarted(Session));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }
}
