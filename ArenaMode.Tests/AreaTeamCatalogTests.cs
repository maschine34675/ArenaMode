using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class AreaTeamCatalogTests
{
    [Fact]
    public void UnifiedCenterHasNewSurveyIdentityWhileRetiredGeometryStaysHistorical()
    {
        var area = ArenaAreaCatalog.CustomsFactoryCenterTeams;
        Assert.Equal("customs-factory-center-polygon-v2", area.Id);
        Assert.Equal(5, area.MaxTeamSize);
        Assert.NotNull(area.TeamStarts);
        Assert.Equal(40, area.Bounds.Outline.Count);
        Assert.False(ArenaAreaCatalog.CustomsFactoryCenterPolygon.Bounds.Outline.SequenceEqual(area.Bounds.Outline));
        Assert.True(ArenaAreaCatalog.TryResolve(area.Id, area.MapId, "teamdeathmatch", false,
            out var resolved, out var reason), reason);
        Assert.Same(area, resolved);
        Assert.False(area.Bounds.LimitsHeight);
        Assert.True(area.IsExcludedSpawn("6e771bc5-ce82-4ab8-88e2-d50dedf4e34e"));
        foreach (var legacy in new[] { ArenaAreaCatalog.FactorySouth, ArenaAreaCatalog.CustomsFactoryCenter,
            ArenaAreaCatalog.CustomsFactoryCenterPolygon })
        {
            Assert.Equal(2, legacy.MaxTeamSize);
            Assert.Null(legacy.TeamStarts);
            Assert.True(ArenaAreaCatalog.TryResolveTeamSize(legacy, null, out int absent, out _));
            Assert.True(ArenaAreaCatalog.TryResolveTeamSize(legacy, 0, out int zero, out _));
            Assert.Equal(0, absent); Assert.Equal(0, zero);
            Assert.False(ArenaAreaCatalog.TryResolveTeamSize(legacy, 2, out _, out _));
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void NewPresetRequiresAnExplicitSupportedSize(int? requested, bool expected)
    {
        foreach (var area in new[] { ArenaAreaCatalog.CustomsFactoryCenterTeams, ArenaAreaCatalog.CustomsFactoryEastPolygon, ArenaAreaCatalog.CustomsYard })
        {
            Assert.Equal(5, area.MaxTeamSize);
            Assert.NotNull(area.TeamStarts);
            Assert.Equal(expected, ArenaAreaCatalog.TryResolveTeamSize(area,
                requested, out int selected, out var reason));
            Assert.Equal(expected ? requested!.Value : 0, selected);
            Assert.Equal(expected, reason == null);
        }
    }

    [Theory]
    [InlineData(ArenaAreaCatalog.FactorySouthId, ArenaMapCatalog.FactoryDayId)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterId, ArenaMapCatalog.CustomsId)]
    [InlineData(ArenaAreaCatalog.CustomsFactoryCenterPolygonId, ArenaMapCatalog.CustomsId)]
    [InlineData("customs-factory-center-teams-v1", ArenaMapCatalog.CustomsId)]
    [InlineData("customs-factory-east-polygon-v1", ArenaMapCatalog.CustomsId)]
    public void HistoricalGeometryDoesNotKeepRetiredIdsPlayable(string retired, string map)
    {
        Assert.False(ArenaAreaCatalog.TryResolve(retired, map, "teamdeathmatch", false, out _, out _));
    }

    [Fact]
    public void TeamStartPoolsOwnTheirSetsAndMatchOnlyExactSideRoleIdentities()
    {
        var westPlayers = new List<string> { "wp" }; var westBots = new List<string> { "wb" };
        var eastPlayers = new List<string> { "ep" }; var eastBots = new List<string> { "eb" };
        var starts = new ArenaTeamStarts(westPlayers, westBots, eastPlayers, eastBots);
        westPlayers.Clear(); westBots[0] = "altered"; eastPlayers.Clear(); eastBots.Clear();
        Assert.True(starts.Allows(true, true, "wp"));
        Assert.True(starts.Allows(true, false, "wb"));
        Assert.True(starts.Allows(false, true, "ep"));
        Assert.True(starts.Allows(false, false, "eb"));
        Assert.False(starts.Allows(true, false, "wp"));
        Assert.False(starts.Allows(false, false, "wb"));
        Assert.False(starts.Allows(true, true, "WP"));
        Assert.False(starts.Allows(true, true, null));
        Assert.False(starts.Allows(true, false, "altered"));
        Assert.Throws<ArgumentException>(() => new ArenaTeamStarts(["p"], ["b"], ["p"], ["other"]));
        Assert.Throws<ArgumentException>(() => new ArenaTeamStarts(["p"], ["b", "b"], ["e"], ["other"]));
    }
}
