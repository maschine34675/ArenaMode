using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Tests;

/// <summary>
/// Team Deathmatch and Domination requests are ordinary requests now: no client clock rides on
/// them, the gear band is the step's fixed band from the first request, and the generated
/// reserve is never cut.
/// </summary>
public sealed partial class BotTierScalePatchTests
{
    private static string StartIndependentTeamArena(string mode="teamdeathmatch",bool pft=false,string? difficulty=null)
    {
        var sessions=new ArenaSessionService(TestLogger.Create<ArenaSessionService>());
        var id=new MongoId(ArenaSession);
        var token=sessions.Arm(id,"factory4_day",mode,protectPitFireTeam:pft,difficulty:difficulty);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(id,"factory4_day",Protocol.BuildNativeStartIdentity(token)));
        ArenaContext.Sessions=sessions;
        var map=ArenaContext.Config.Maps["factory4_day"];
        map.BotTierStartLevel=1; map.BotTierEndLevel=65; map.BotTierRampMinutes=15;
        return token;
    }

    /// <summary>A request without any clock gets the step's fixed band at second 0 and later, and keeps its batch.</summary>
    [Theory]
    [InlineData("teamdeathmatch","maximum",0,65)] [InlineData("teamdeathmatch","maximum",14,65)]
    [InlineData("domination","maximum",0,65)] [InlineData("domination","maximum",14,65)]
    [InlineData("teamdeathmatch","reduced",0,39)] [InlineData("teamdeathmatch","reduced",14,39)]
    [InlineData("domination","reduced",0,39)] [InlineData("domination","reduced",14,39)]
    public void ARequestWithoutAnyClockGetsTheFixedBandAndKeepsTheBatch(string mode,string step,int minutes,int level)
    {
        StartIndependentTeamArena(mode,difficulty:step);
        if(minutes>0) SetElapsed(ArenaContext.Sessions!,new MongoId(ArenaSession),TimeSpan.FromMinutes(minutes));
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details);
        Assert.Equal(level,details.PlayerLevel);
        Assert.Equal(Math.Max(1,level-5),details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(level+5,details.LocationSpecificPmcLevelOverride.Max);
        Assert.Equal(15,details.BotCountToGenerate);
        Assert.Equal(15,ArenaContext.Config.Maps["factory4_day"].BotTierRampMinutes);
    }

    /// <summary>A foreign, stale or protected request keeps every native value.</summary>
    [Theory]
    [InlineData("otherSession")] [InlineData("wrongMap")] [InlineData("oldRaid")] [InlineData("pft")]
    public void ForeignOrProtectedRequestsStayNative(string cause)
    {
        StartIndependentTeamArena(cause=="pft"?"tactical":"teamdeathmatch",cause=="pft");
        if(cause=="oldRaid") MakeRaidStale(ArenaContext.Sessions!,new MongoId(ArenaSession));
        var details=Details(cause=="wrongMap"?"laboratory":"factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(cause=="otherSession"?OrdinarySession:ArenaSession),details);
        Assert.Equal(15,details.BotCountToGenerate);
        Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max);
    }

    [Theory]
    [InlineData("endless")] [InlineData("rounds")] [InlineData("gungame")] [InlineData("tactical")]
    [InlineData("teamdeathmatch")] [InlineData("domination")]
    public void NoModeCutsItsGeneratedReserve(string mode)
    {
        // The patch only writes the level band; it never touches the batch size. The scav modes
        // draw from this reserve during the raid and Tactical keeps its pool as well.
        StartIndependentTeamArena(mode);
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=45;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details);
        Assert.Equal(45,details.BotCountToGenerate);
    }

    [Fact]
    public void GearOptOutPreservesNativeLevelsAndTheBatch()
    {
        StartIndependentTeamArena(); ArenaContext.Config.Maps["factory4_day"].ScaleBotTier=false;
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details);
        Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(15,details.BotCountToGenerate);
    }

    /// <summary>
    /// The clock type is gone and the patch takes only the session and the generation details,
    /// never the GenerateCondition that carried the old `arenaMatchClock` key. So a stale key
    /// of an older client cannot reach the band: by construction (the signature), which is all
    /// this test asserts; it does not feed such a key through the patch.
    /// </summary>
    [Fact]
    public void TheMatchClockChannelIsGoneFromTheSources()
    {
        Assert.Null(typeof(ArenaBotTierScalePatch).Assembly.GetType("ArenaMode.Shared.ArenaDifficultyClock"));
        Assert.Single(typeof(ArenaBotTierScalePatch).GetMethod("Prefix")!.GetParameters().Skip(1));
    }
}
