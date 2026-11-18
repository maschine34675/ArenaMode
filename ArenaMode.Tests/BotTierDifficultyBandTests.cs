using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Tests;

/// <summary>
/// The server gear band of docs/reviews/DIFFICULTY-MODEL-20261118.md: a fixed level per step in
/// every mode but Gun Game, the Gun Game ramp on the server wall clock, the protected bypass and
/// the opt-out. The map is configured 1..65 over 15 minutes, so Maximum is 65 and Reduced 39.
/// </summary>
public sealed partial class BotTierScalePatchTests
{
    private const string BandMap = "factory4_day";

    private (ArenaSessionService Sessions, MongoId Id) StartBand(
        string mode, string? difficulty, bool protectedTeam = false, int start = 1, int end = 65)
    {
        ArenaContext.Config.RaidTimeMinutes = 60;
        var map = ArenaContext.Config.Maps[BandMap];
        map.BotTierStartLevel = start;
        map.BotTierEndLevel = end;
        map.BotTierRampMinutes = 15;
        map.ScaleBotTier = true;
        var id = new MongoId(ArenaSession);
        var sessions = StartArena(id, BandMap, protectedTeam, mode, difficulty);
        ArenaContext.Sessions = sessions;
        return (sessions, id);
    }

    private static int LevelAt(ArenaSessionService sessions, MongoId id, TimeSpan elapsed)
    {
        SetElapsed(sessions, id, elapsed);
        var details = Details(BandMap, 47, 33, 61);
        ArenaBotTierScalePatch.Prefix(id, details);
        int level = details.PlayerLevel!.Value;
        Assert.Equal(Math.Max(1, level - 5), details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(level + 5, details.LocationSpecificPmcLevelOverride.Max);
        return level;
    }

    [Theory]
    [InlineData("deathmatch", "maximum", 65)]
    [InlineData("deathmatch", null, 65)]
    [InlineData("deathmatch", "reduced", 39)]
    [InlineData("teamdeathmatch", "maximum", 65)]
    [InlineData("teamdeathmatch", "reduced", 39)]
    [InlineData("domination", "maximum", 65)]
    [InlineData("domination", "reduced", 39)]
    [InlineData("tactical", "maximum", 65)]
    [InlineData("tactical", "reduced", 39)]
    [InlineData("zombies", "maximum", 65)]
    [InlineData("zombies", "reduced", 39)]
    public void EveryModeButGunGameHoldsTheStepsGearLevelFromTheFirstRequestOnward(string mode, string? step, int level)
    {
        var (sessions, id) = StartBand(mode, step);
        foreach (var elapsed in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(40) })
            Assert.Equal(level, LevelAt(sessions, id, elapsed));
    }

    [Fact]
    public void ReducedNeverGoesBelowTheMapsStartLevel()
    {
        var (sessions, id) = StartBand("tactical", "reduced", start: 50, end: 65);
        Assert.Equal(50, LevelAt(sessions, id, TimeSpan.Zero));
        Assert.Equal(50, LevelAt(sessions, id, TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData("maximum", 0, 1)]
    [InlineData("maximum", 7.5, 33)]
    [InlineData("maximum", 15, 65)]
    [InlineData("maximum", 40, 65)]
    [InlineData("reduced", 0, 1)]
    [InlineData("reduced", 7.5, 20)]
    [InlineData("reduced", 15, 39)]
    [InlineData("reduced", 40, 39)]
    public void GunGameRampsFromTheStartLevelToTheStepsLevelOverTheMapsRampMinutes(string step, double minutes, int level)
    {
        var (sessions, id) = StartBand("gungame", step);
        Assert.Equal(level, LevelAt(sessions, id, TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void GunGameRampLengthIsTheMapsNotTheClientsWireClock()
    {
        var (sessions, id) = StartBand("gungame", "maximum");
        ArenaContext.Config.Maps[BandMap].BotTierRampMinutes = 30;
        Assert.Equal(33, LevelAt(sessions, id, TimeSpan.FromMinutes(15)));
    }

    [Theory]
    [InlineData("maximum")]
    [InlineData("reduced")]
    public void ProtectedTeamRaidsWriteNoBandAtEitherStep(string step)
    {
        var (_, id) = StartBand("tactical", step, protectedTeam: true);
        var details = Details(BandMap, 47, 33, 61);
        ArenaBotTierScalePatch.Prefix(id, details);
        Assert.Equal(47, details.PlayerLevel);
        Assert.Equal(33, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61, details.LocationSpecificPmcLevelOverride.Max);
        Assert.False(ArenaBotTierOwnership.TryGetRange(details, new(1, 100), 79, out _));
    }

    [Theory]
    [InlineData("tactical", "maximum")]
    [InlineData("tactical", "reduced")]
    [InlineData("gungame", "maximum")]
    [InlineData("gungame", "reduced")]
    public void OptOutKeepsTheNativeLevelsAtEveryStep(string mode, string step)
    {
        var (sessions, id) = StartBand(mode, step);
        ArenaContext.Config.Maps[BandMap].ScaleBotTier = false;
        SetElapsed(sessions, id, TimeSpan.FromMinutes(10));
        var details = Details(BandMap, 47, 33, 61);
        ArenaBotTierScalePatch.Prefix(id, details);
        Assert.Equal(47, details.PlayerLevel);
        Assert.Equal(33, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void TheContextCarriesTheModeAndTheFrozenStep()
    {
        var (sessions, id) = StartBand("gungame", "reduced");
        Assert.True(sessions.TryGetBotTierContext(id, BandMap, out _, out var prot, out var identity, out var mode, out var step));
        Assert.False(prot);
        Assert.NotNull(identity);
        Assert.Equal("gungame", mode);
        Assert.Equal(ArenaDifficultyStep.Reduced, step);
    }
}
