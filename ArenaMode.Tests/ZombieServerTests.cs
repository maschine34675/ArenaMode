using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

/// <summary>
/// Zombie mode's server-side pieces not already covered by
/// LocationMutatorTests (the native infection event block, the raised bot
/// caps): the hostility branch that must do nothing at all, and the area
/// catalog's full-map resolution for the new mode id.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class ZombieServerTests : IDisposable
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ArenaModeConfig _oldConfig = ArenaContext.Config;

    public ZombieServerTests()
    {
        ArenaContext.Config = new ArenaModeConfig { RaidTimeMinutes = 1 };
        ArenaContext.Sessions = new ArenaSessionService(
            BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
    }

    public void Dispose()
    {
        ArenaContext.Sessions = _oldSessions;
        ArenaContext.Config = _oldConfig;
    }

    [Theory]
    [InlineData("ffa")]
    [InlineData("all_vs_player")]
    public void ZombieLeavesHostilityVanillaEvenUnderFfa(string hostilityMode)
    {
        // Infected Mind settings (verified against the installed
        // SPT_Data/database/bots/types/infected*.json) already make every
        // infected role AlwaysEnemies to BEAR, USEC and Savage alike and
        // friendly to the other infected roles. The generic branch below —
        // reached by every other mode — would instead apply
        // ArenaContext.Config.HostilityMode, and "ffa" in particular would
        // turn every zombie (side Savage) against every other zombie. Prove
        // it does not fire under EITHER configured hostility mode.
        ArenaContext.Config.HostilityMode = hostilityMode;
        var location = Location();
        var sourceEntries = location.BotLocationModifier!.AdditionalHostilitySettings!;
        var before = JsonSerializer.Serialize(sourceEntries);
        Start(Session, "zombie", location);

        AdjustBotHostilityPatch.Postfix(location);

        Assert.Same(sourceEntries, location.BotLocationModifier.AdditionalHostilitySettings);
        Assert.Equal(before, JsonSerializer.Serialize(location.BotLocationModifier.AdditionalHostilitySettings));
    }

    [Fact]
    public void ZombieResolvesFactoryWithoutAnArea()
    {
        // Zombie has no bounded area of its own in v0 (decision 1): Factory
        // must resolve as the ordinary full map, both implicitly (no id) and
        // with the explicit full-map id, exactly like every other full-map mode.
        Assert.True(ArenaAreaCatalog.TryResolve(null, ArenaMapCatalog.FactoryDayId, "zombie", false,
            out var impliedArea, out var impliedReason));
        Assert.Null(impliedArea);
        Assert.Null(impliedReason);

        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.FactoryDayId, "zombie", false,
            out var explicitArea, out var explicitReason));
        Assert.Null(explicitArea);
        Assert.Null(explicitReason);
    }

    private static void Start(MongoId session, string mode, LocationBase location)
    {
        var sessions = ArenaContext.Sessions!;
        var token = sessions.Arm(session, location.Id, mode);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(session, location.Id, Protocol.BuildNativeStartIdentity(token)));
        sessions.StashLocationClone(session, location);
    }

    private static LocationBase Location() => new()
    {
        Id = ArenaMapCatalog.FactoryDayId,
        BotLocationModifier = new BotLocationModifier
        {
            AdditionalHostilitySettings =
            [
                new AdditionalHostilitySettings
                {
                    BotRole = "infectedAssault",
                    AlwaysFriends = ["infectedAssault", "infectedPmc", "infectedCivil", "infectedLaborant"],
                    AlwaysEnemies = [],
                    UsecPlayerBehaviour = "AlwaysEnemies",
                    UsecEnemyChance = 100,
                    BearPlayerBehaviour = "AlwaysEnemies",
                    BearEnemyChance = 100,
                    SavagePlayerBehaviour = "AlwaysEnemies",
                    SavageEnemyChance = 100,
                },
            ],
        },
    };
}
