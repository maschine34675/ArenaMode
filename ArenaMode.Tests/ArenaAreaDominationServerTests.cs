using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

// Domination plays in the three Customs areas (docs/reviews/DOMINATION-VANILLA-MAPS-20261113.md). The server
// follows the shared catalogue: the same arm, replay, size and spawn rules as independent Team Deathmatch.
public sealed partial class ArenaAreaServerTests
{
    private const string Domination = "domination";

    [Fact]
    public void DominationArmsInACustomsAreaWithItsNegotiatedTeamSize()
    {
        Assert.True(Sessions.BeginArmPreparation(Session, BoundedMap, Domination, false, Token, Area, 3));
        Assert.Equal(Area, Sessions.GetAreaId(Session));
        Assert.Equal(3, Sessions.GetAreaTeamSize(Session));
        Assert.True(Sessions.CompleteArmPreparation(Session));
        Assert.True(Sessions.TryReplayArmedRequest(Session, BoundedMap.ToUpperInvariant(), Domination.ToUpperInvariant(),
            false, Token, out var replay, Area, 3));
        Assert.Equal(Token, replay);
        // Another size or another area is another logical request, never a replay.
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Domination, false, Token, out _, Area, 4));
        Assert.False(Sessions.TryReplayArmedRequest(Session, BoundedMap, Domination, false, Token, out _,
            ArenaAreaCatalog.CustomsYardId, 3));
    }

    [Theory]
    [InlineData(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.CustomsId, false, 0)]
    [InlineData(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.CustomsId, false, 3)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaMapCatalog.CustomsId, true, 3)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaMapCatalog.CustomsId, false, 0)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaMapCatalog.CustomsId, false, 1)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaMapCatalog.CustomsId, false, 6)]
    [InlineData(ArenaAreaCatalog.CustomsYardId, ArenaMapCatalog.LaboratoryId, false, 3)]
    [InlineData(ArenaAreaCatalog.CustomsYardId, ArenaMapCatalog.FactoryDayId, false, 3)]
    [InlineData(ArenaAreaCatalog.FactorySouthId, ArenaMapCatalog.CustomsId, false, 3)]
    [InlineData("customs-yard-polygon-v2", ArenaMapCatalog.CustomsId, false, 3)]
    public void DominationRefusesWholeCustomsASquadAnUnsupportedSizeAndAreasOnOtherMaps(
        string area, string map, bool pft, int size)
    {
        Assert.False(Sessions.BeginArmPreparation(Session, map, Domination, pft, Token, area, size));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, Domination, pft, areaId: area, areaTeamSize: size));
    }

    [Theory]
    [InlineData("teamdeathmatch")]
    [InlineData(Domination)]
    public void TheIndependentTeamModesKeepTheSameAreaSpawnsAndFactionStarts(string mode)
    {
        var location = Location();
        ArenaLocationMutator.Mutate(location, new(), new(), mode, Area, AreaSize);
        Assert.Equal(new[] { TestUsecPlayer, TestBearPlayer, TestUsecBot1, TestUsecBot2, TestBearBot1, TestBearBot2, "inside-boss", "inside-unknown" },
            location.SpawnPointParams!.Select(point => point.Id));
        // The size is explicit in both, as the shared rule demands.
        Assert.ThrowsAny<Exception>(() => ArenaLocationMutator.Mutate(Location(), new(), new(), mode, Area, null));
    }
}
