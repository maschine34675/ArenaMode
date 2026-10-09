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
    public static IEnumerable<object[]> BoundedModeCases()
    {
        foreach (var area in new[] { ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
                     ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId })
        foreach (var mode in new[] { "deathmatch", "gungame", "tactical", "zombie" })
            yield return [area, mode];
    }

    [Theory]
    [MemberData(nameof(BoundedModeCases))]
    public async Task CustomsNonTdmRouteArmsExactAreaWithoutInventingATeamSize(string area, string mode)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = ParticipantRequest(Customs, mode, ArenaParticipantPolicy.IsSolo(mode) ? 8 : null)
            with { AreaId = area, AreaTeamSize = 0 };
        using var result = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(result.RootElement.GetProperty("armed").GetBoolean(), result.RootElement.ToString());
        Assert.Equal(area, Sessions.GetAreaId(Session));
        Assert.Equal(0, Sessions.GetAreaTeamSize(Session));
        Assert.Equal(request.Participants, Sessions.GetParticipants(Session));
        using var replay = JsonDocument.Parse(await InvokeStart(router, request with { AreaTeamSize = null }));
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        foreach (var changed in new[] { request with { AreaId = ArenaAreaCatalog.FullMapId },
                     request with { AreaTeamSize = 5 }, request with { AreaId = "invalid-area" } })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, changed));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(area, Sessions.GetAreaId(Session));
            Assert.Equal(Token, Sessions.GetArmToken(Session));
        }
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
    }

    [SkippableTheory]
    [MemberData(nameof(BoundedModeCases))]
    public void CustomsEveryModeFiltersRealSpawnsAndRejectsLateOutsidePoints(string area, string mode)
    {
        string path = Path.Combine(Environment.GetEnvironmentVariable("SPT_DATABASE")
            ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database", "locations", Customs, "base.json");
        Skip.IfNot(File.Exists(path), "Installed Customs database fixture is unavailable.");
        var original = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), CustomsJsonOptions)!;
        string before = JsonSerializer.Serialize(original, CustomsJsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(before, CustomsJsonOptions)!;
        int? total = ArenaParticipantPolicy.IsSolo(mode) ? 8 : null;
        Sessions.Arm(Session, Customs, mode, armToken: Token, areaId: area,
            deathmatchHostility: mode == "deathmatch" ? "ffa" : null, participants: total);
        string nativeEnd = Start(Customs);
        GenerateLocationAndLootPatch.Postfix(Session, Customs, clone);
        Assert.True(Sessions.ValidateAreaResponse(Session, clone, out var reason), reason);
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, nativeEnd));
        Assert.True(ArenaAreaCatalog.TryResolve(area, Customs, mode, false, out var definition, out _));
        Assert.NotEmpty(clone.SpawnPointParams!);
        Assert.All(clone.SpawnPointParams!, point => Assert.True(definition!.AllowsSpawn(
            (float)point.Position!.Value.X, (float)point.Position.Value.Y, (float)point.Position.Value.Z, point.Id)));
        Assert.Empty(clone.Waves!);
        Assert.Empty(clone.BossLocationSpawn!);
        clone.SpawnPointParams = clone.SpawnPointParams!.Append(Point("outside-after-filter", 0, 0, 0, "Bot")).ToList();
        Assert.False(Sessions.ValidateAreaResponse(Session, clone, out _));
        Assert.Equal(before, JsonSerializer.Serialize(original, CustomsJsonOptions));
    }
}
