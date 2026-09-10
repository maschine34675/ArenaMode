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
    private static string StartTimedArena(string mode="teamdeathmatch",bool pft=false)
    {
        var sessions=new ArenaSessionService(TestLogger.Create<ArenaSessionService>());
        var id=new MongoId(ArenaSession);
        var token=sessions.Arm(id,"factory4_day",mode,protectPitFireTeam:pft);
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
    [Theory]
    [InlineData(60,0,1)] [InlineData(60,24,33)] [InlineData(60,48,65)]
    [InlineData(300,120,33)] [InlineData(300,240,65)] [InlineData(300,299,65)]
    [InlineData(1200,480,33)] [InlineData(1200,960,65)]
    public void FiniteGearUsesPlayingClockAndDoesNotPrefillReserve(int duration,double elapsed,int level)
    {
        var token=StartTimedArena(); var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        var condition=Condition(token,duration,elapsed);
        Assert.IsType<JsonElement>(condition.ExtensionData![ArenaDifficultyClock.WireKey]);
        BotTierScalePatch.Prefix(new MongoId(ArenaSession),details,condition);
        Assert.Equal(level,details.PlayerLevel); Assert.Equal(Math.Max(1,level-5),details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(level+5,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(1,details.BotCountToGenerate);
        Assert.Equal(15,ArenaContext.Config.Maps["factory4_day"].BotTierRampMinutes);
    }
    [Theory]
    [InlineData("token")] [InlineData("version")] [InlineData("duration0")] [InlineData("durationLarge")]
    [InlineData("negativeTime")] [InlineData("lateTime")] [InlineData("zeroBatch")] [InlineData("negativeBatch")]
    [InlineData("otherSession")] [InlineData("wrongMap")] [InlineData("oldRaid")] [InlineData("solo")]
    [InlineData("pft")] [InlineData("malformed")]
    public void InvalidOrForeignClockPreservesAllNativeDetails(string cause)
    {
        var token=StartTimedArena(cause is "solo" or "pft"?"tactical":"teamdeathmatch",cause=="pft");
        var condition=Condition(cause=="token"?"old-arm":token,cause=="duration0"?0:cause=="durationLarge"?7201:300,
            cause=="negativeTime"?-1:cause=="lateTime"?301:120,cause=="zeroBatch"?0:cause=="negativeBatch"?-1:1,cause=="version"?0:1);
        if(cause=="malformed") condition.ExtensionData![ArenaDifficultyClock.WireKey]=JsonSerializer.SerializeToElement("invalid");
        if(cause=="oldRaid") MakeRaidStale(ArenaContext.Sessions!,new MongoId(ArenaSession));
        var details=Details(cause=="wrongMap"?"laboratory":"factory4_day",47,33,61); details.BotCountToGenerate=15;
        BotTierScalePatch.Prefix(new MongoId(cause=="otherSession"?OrdinarySession:ArenaSession),details,condition);
        Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(15,details.BotCountToGenerate);
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
        BotTierScalePatch.Prefix(new MongoId(ArenaSession),details,
            new GenerateCondition {Role="assault",Limit=2,Difficulty="normal"});
        Assert.Equal(45,details.BotCountToGenerate);
    }

    [Fact]
    public void GearOptOutPreservesNativeLevelsWithoutGeneratingAnUnusedReserve()
    {
        var token=StartTimedArena(); ArenaContext.Config.Maps["factory4_day"].ScaleBotTier=false;
        var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        BotTierScalePatch.Prefix(new MongoId(ArenaSession),details,Condition(token,300,240));
        Assert.Equal(47,details.PlayerLevel); Assert.Equal(33,details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61,details.LocationSpecificPmcLevelOverride.Max); Assert.Equal(1,details.BotCountToGenerate);
    }
    [Fact]
    public void AbsentClockKeepsLegacyCurveAndBatch()
    {
        StartTimedArena(); var details=Details("factory4_day",47,33,61); details.BotCountToGenerate=15;
        BotTierScalePatch.Prefix(new MongoId(ArenaSession),details,new GenerateCondition());
        Assert.Equal(1,details.PlayerLevel); Assert.Equal(15,details.BotCountToGenerate);
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
