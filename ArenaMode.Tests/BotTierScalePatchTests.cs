using System.Collections;
using System.Reflection;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed partial class BotTierScalePatchTests : IDisposable
{
    private const string ArenaSession = "0123456789abcdef01234567";
    private const string OrdinarySession = "89abcdef0123456701234567";
    private readonly ArenaSessionService? _previousSessions = ArenaContext.Sessions;
    private readonly ArenaModeConfig _previousConfig = ArenaContext.Config;

    public BotTierScalePatchTests()
    {
        ArenaContext.Sessions = null;
        ArenaContext.Config = new ArenaModeConfig
        {
            RaidTimeMinutes = 1,
            Maps = new Dictionary<string, ArenaMapConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["factory4_day"] = new()
                {
                    ScaleBotTier = true,
                    BotTierStartLevel = 12,
                    BotTierEndLevel = 12,
                    BotTierRampMinutes = 15,
                },
                ["laboratory"] = new()
                {
                    ScaleBotTier = true,
                    BotTierStartLevel = 22,
                    BotTierEndLevel = 22,
                    BotTierRampMinutes = 15,
                },
            },
        };
        ArenaBotTierScalePatch.ResetReporting();
    }

    public void Dispose()
    {
        ArenaContext.Sessions = _previousSessions;
        ArenaContext.Config = _previousConfig;
        ArenaBotTierScalePatch.ResetReporting();
    }

    [Fact]
    public void ProtectedTeamArenaPreservesNativePmcLevelAndMapRange()
    {
        var sessionId = new MongoId(ArenaSession);
        ArenaContext.Sessions = StartArena(sessionId, "factory4_day", protectedTeam: true);
        var details = Details("factory4_day", playerLevel: 47, min: 33, max: 61);

        ArenaBotTierScalePatch.Prefix(sessionId, details);

        Assert.Equal(47, details.PlayerLevel);
        Assert.Equal(33, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(61, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void SoloArenaStillAppliesConfiguredTierRamp()
    {
        var sessionId = new MongoId(ArenaSession);
        ArenaContext.Sessions = StartArena(sessionId, "factory4_day", protectedTeam: false);
        var details = Details("factory4_day", playerLevel: 47, min: 33, max: 61);

        ArenaBotTierScalePatch.Prefix(sessionId, details);

        Assert.Equal(12, details.PlayerLevel);
        Assert.Equal(7, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(17, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void OrdinarySessionOnSameMapIsNotCapturedByLiveArenaSession()
    {
        var arenaSessionId = new MongoId(ArenaSession);
        ArenaContext.Sessions = StartArena(arenaSessionId, "factory4_day", protectedTeam: false);
        var details = Details("factory4_day", playerLevel: 38, min: 25, max: 55);

        ArenaBotTierScalePatch.Prefix(new MongoId(OrdinarySession), details);

        Assert.Equal(38, details.PlayerLevel);
        Assert.Equal(25, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(55, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void ExactSessionWithWrongMapLeavesNativeDetailsUntouched()
    {
        var sessionId = new MongoId(ArenaSession);
        ArenaContext.Sessions = StartArena(sessionId, "factory4_day", protectedTeam: false);
        var details = Details("laboratory", playerLevel: 38, min: 25, max: 55);

        ArenaBotTierScalePatch.Prefix(sessionId, details);

        Assert.Equal(38, details.PlayerLevel);
        Assert.Equal(25, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(55, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void StaleExactSessionLeavesNativeDetailsUntouched()
    {
        var sessionId = new MongoId(ArenaSession);
        var sessions = StartArena(sessionId, "factory4_day", protectedTeam: false);
        MakeRaidStale(sessions, sessionId);
        ArenaContext.Sessions = sessions;
        var details = Details("factory4_day", playerLevel: 38, min: 25, max: 55);

        ArenaBotTierScalePatch.Prefix(sessionId, details);

        Assert.Equal(38, details.PlayerLevel);
        Assert.Equal(25, details.LocationSpecificPmcLevelOverride!.Min);
        Assert.Equal(55, details.LocationSpecificPmcLevelOverride.Max);
    }

    [Fact]
    public void PatchTargetsExactSessionAwareGenerateBotWaveOverloadAsPrefix()
    {
        MethodInfo resolver = typeof(ArenaBotTierScalePatch).GetMethod(
            "GetTargetMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var target = Assert.IsAssignableFrom<MethodInfo>(
            resolver.Invoke(new ArenaBotTierScalePatch(), null));

        Assert.Equal("GenerateBotWave", target.Name);
        Assert.Equal(typeof(BotController), target.DeclaringType);
        Assert.Equal(
            [typeof(MongoId), typeof(GenerateCondition), typeof(BotGenerationDetails)],
            target.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        MethodInfo prefix = typeof(ArenaBotTierScalePatch).GetMethod(
            nameof(ArenaBotTierScalePatch.Prefix),
            BindingFlags.Static | BindingFlags.Public)!;
        Assert.NotNull(prefix.GetCustomAttribute<PatchPrefixAttribute>());
        Assert.Null(typeof(ArenaBotTierScalePatch).GetMethod(
            "Postfix",
            BindingFlags.Static | BindingFlags.Public));
    }

    private static BotGenerationDetails Details(string location, int playerLevel, int min, int max)
        => new()
        {
            Location = location,
            PlayerLevel = playerLevel,
            LocationSpecificPmcLevelOverride = new MinMax<int>(min, max),
        };

    private static ArenaSessionService StartArena(
        MongoId sessionId,
        string location,
        bool protectedTeam)
    {
        var sessions = new ArenaSessionService(TestLogger.Create<ArenaSessionService>());
        string token = sessions.Arm(
            sessionId,
            location,
            "tactical",
            protectPitFireTeam: protectedTeam);
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(
                sessionId,
                location,
                Protocol.BuildNativeStartIdentity(token)));
        return sessions;
    }

    private static void MakeRaidStale(ArenaSessionService sessions, MongoId sessionId)
    {
        FieldInfo sessionsField = typeof(ArenaSessionService).GetField(
            "_sessions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entries = Assert.IsAssignableFrom<IDictionary>(sessionsField.GetValue(sessions));
        object? entry = entries[sessionId.ToString()];
        Assert.NotNull(entry);
        FieldInfo inRaidAt = entry!.GetType().GetField(
            "InRaidAtUtc",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        inRaidAt.SetValue(entry, DateTime.UtcNow.AddMinutes(-17));
    }

    public class TestLogger : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;

        public static ISptLogger<T> Create<T>()
            => DispatchProxy.Create<ISptLogger<T>, TestLogger>();
    }
}
