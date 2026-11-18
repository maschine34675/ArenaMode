using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;

namespace ArenaMode.Tests;

public sealed partial class BotTierScalePatchTests
{
    private static string StartTimedArena(string mode="teamdeathmatch",bool pft=false,string? difficulty=null)
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
    private static GenerateCondition Condition(string token,int duration,double elapsed,int limit=1,int version=1)
    {
        // Actual native deserialization, including JsonExtensionData as JsonElement.
        var json=JsonSerializer.Serialize(new Dictionary<string,object> {
            ["Role"]="pmcUSEC", ["Limit"]=limit, ["Difficulty"]="hard",
            [ArenaDifficultyClock.WireKey]=new ArenaDifficultyClock {
                Version=version,ArmToken=token,TimeLimitSeconds=duration,PlayingElapsedSeconds=elapsed }
        });
        return JsonSerializer.Deserialize<GenerateCondition>(json,new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
    }
    /// <summary>The wire clock only sizes the request now; the band is the step's fixed gear level.</summary>
    [Theory]
    [InlineData(60, 0, "maximum", 65)] [InlineData(60, 24, "maximum", 65)] [InlineData(60, 48, "maximum", 65)]
    [InlineData(300, 120, "maximum", 65)] [InlineData(300, 299, "maximum", 65)]
    [InlineData(1200, 480, "maximum", 65)] [InlineData(1200, 960, "maximum", 65)]
    [InlineData(60, 0, "reduced", 39)] [InlineData(300, 240, "reduced", 39)] [InlineData(1200, 960, "reduced", 39)]
    public void FiniteGearIgnoresTheWireClockAndStillDoesNotPrefillReserve(int duration, double elapsed, string step, int level)
    {
        var token=StartTimedArena(difficulty:step); var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        var condition=Condition(token,duration,elapsed);
        Assert.IsType<JsonElement>(condition.ExtensionData![ArenaDifficultyClock.WireKey]);
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details,condition);
        Assert.Equal(level,details.PlayerLevel); Assert.Equal(Math.Max(1,level-5),details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(level+5,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(1,details.BotCountToGenerate);
        Assert.Equal(15,ArenaContext.Config.Maps["factory4_day"].BotTierRampMinutes);
    }

    /// <summary>A foreign or stale request keeps every native value; a bad clock on the live raid only keeps its native size.</summary>
    [Theory]
    [InlineData("otherSession",true)] [InlineData("wrongMap",true)] [InlineData("oldRaid",true)] [InlineData("pft",true)]
    [InlineData("token",false)] [InlineData("version",false)] [InlineData("duration0",false)] [InlineData("durationLarge",false)]
    [InlineData("negativeTime",false)] [InlineData("lateTime",false)] [InlineData("zeroBatch",false)] [InlineData("negativeBatch",false)]
    [InlineData("solo",false)] [InlineData("malformed",false)]
    public void InvalidOrForeignClockNeverShrinksTheReserveAndForeignRequestsStayNative(string cause, bool nativeDetails)
    {
        var token=StartTimedArena(cause is "solo" or "pft"?"tactical":"teamdeathmatch",cause=="pft");
        var condition=Condition(cause=="token"?"old-arm":token,cause=="duration0"?0:cause=="durationLarge"?7201:300,
            cause=="negativeTime"?-1:cause=="lateTime"?301:120,cause=="zeroBatch"?0:cause=="negativeBatch"?-1:1,cause=="version"?0:1);
        if(cause=="malformed") condition.ExtensionData![ArenaDifficultyClock.WireKey]=JsonSerializer.SerializeToElement("invalid");
        if(cause=="oldRaid") MakeRaidStale(ArenaContext.Sessions!,new MongoId(ArenaSession));
        var details=Details(cause=="wrongMap"?"laboratory":"factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(cause=="otherSession"?OrdinarySession:ArenaSession),details,condition);
        Assert.Equal(15,details.BotCountToGenerate);
        if(nativeDetails)
        {
            Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
            Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max);
        }
        else
        {
            // The band does not depend on the clock: a Team Deathmatch request with a broken clock
            // gets the same fixed Maximum band as one without.
            Assert.Equal(65,details.PlayerLevel); Assert.Equal(60,details.LocationSpecificPmcLevelOverride!.Min);
            Assert.Equal(70,details.LocationSpecificPmcLevelOverride.Max);
        }
    }

    [Theory]
    [InlineData("endless")] [InlineData("rounds")] [InlineData("gungame")] [InlineData("tactical")]
    public void AModeWithoutTheTimedClockKeepsItsGeneratedReserve(string mode)
    {
        // The count cap is scoped to the timed Team Deathmatch request on
        // purpose. The three scav modes draw from this reserve during the raid
        // and pay a miss on the SPAWN path rather than in a loading screen, and
        // Tactical keeps its pool as well; shrinking it for them is a trade
        // nobody has measured past minute twenty. If that measurement ever
        // happens, this test is the one that has to change.
        StartTimedArena(mode);
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=45;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details,
            new GenerateCondition {Role="assault",Limit=2,Difficulty="normal"});
        Assert.Equal(45,details.BotCountToGenerate);
    }

    [Fact]
    public void GearOptOutPreservesNativeLevelsWithoutGeneratingAnUnusedReserve()
    {
        var token=StartTimedArena(); ArenaContext.Config.Maps["factory4_day"].ScaleBotTier=false;
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details,Condition(token,300,240));
        Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(1,details.BotCountToGenerate);
    }
    [Theory]
    [InlineData("teamdeathmatch")] [InlineData("domination")]
    public void AbsentClockGetsTheFixedBandAndKeepsTheBatch(string mode)
    {
        StartTimedArena(mode); var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),details,new GenerateCondition());
        Assert.Equal(65,details.PlayerLevel); Assert.Equal(15,details.BotCountToGenerate);
        // Later in the raid the same request gets the same band.
        SetElapsed(ArenaContext.Sessions!,new MongoId(ArenaSession),TimeSpan.FromMinutes(14));
        var late=Details("factory4_day",47,33,61);
        ArenaBotTierScalePatch.Prefix(new MongoId(ArenaSession),late,new GenerateCondition());
        Assert.Equal(65,late.PlayerLevel);
    }
    [Fact]
    public void ClockRejectsNonFiniteValuesAndWrongProtocol()
    {
        foreach(var value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1,301})
            Assert.False(new ArenaDifficultyClock {Version=1,ArmToken="a",TimeLimitSeconds=300,PlayingElapsedSeconds=value}.IsValid());
        Assert.False(new ArenaDifficultyClock {ArmToken="a",TimeLimitSeconds=300}.IsValid());
        Assert.False(new ArenaDifficultyClock {Version=1,ArmToken=" ",TimeLimitSeconds=300}.IsValid());
    }
}
