using System.Collections;
using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class TeamDeathmatchServerTests : IDisposable
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId OtherSession = new("89abcdef0123456701234567");
    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ArenaModeConfig _oldConfig = ArenaContext.Config;

    public TeamDeathmatchServerTests()
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

    [Fact]
    public void BothFactionsOverrideConflictingHostilityWithoutChangingSharedRecords()
    {
        var location = Location();
        var sourceEntries = location.BotLocationModifier!.AdditionalHostilitySettings!.ToList();
        var before = JsonSerializer.Serialize(sourceEntries);
        Start(Session, "teamdeathmatch", location);

        ArenaAdjustBotHostilityPatch.Postfix(location);

        var result = location.BotLocationModifier.AdditionalHostilitySettings!.ToList();
        Assert.Equal(before, JsonSerializer.Serialize(sourceEntries));
        Assert.NotSame(sourceEntries[0], result[0]);
        Assert.NotSame(sourceEntries[1], result[1]);
        Assert.Same(sourceEntries[2], result[2]);
        AssertFaction(result[0], "pmcUSEC", "pmcBEAR");
        AssertFaction(result[1], "pmcBEAR", "pmcUSEC");
        Assert.NotSame(sourceEntries[0].AlwaysFriends, result[0].AlwaysFriends);
        Assert.NotSame(sourceEntries[0].AlwaysEnemies, result[0].AlwaysEnemies);
        Assert.NotSame(sourceEntries[0].Neutral, result[0].Neutral);
        Assert.NotSame(sourceEntries[0].Warn, result[0].Warn);
        Assert.NotSame(sourceEntries[0].ChancedEnemies, result[0].ChancedEnemies);
        Assert.NotSame(sourceEntries[0].ChancedEnemies![2], result[0].ChancedEnemies![0]);
        Assert.Equal("Warn", result[0].SavagePlayerBehaviour);
        Assert.Equal(19, result[0].SavageEnemyChance);

        // A later consumer modifying even the retained non-PMC chance record
        // must not write back into the singleton record SPT added by reference.
        result[0].ChancedEnemies![0].EnemyChance = 99;
        Assert.Equal(17, sourceEntries[0].ChancedEnemies![2].EnemyChance);
    }

    [Theory]
    [InlineData("ffa")]
    [InlineData("all_vs_player")]
    public void TeamRelationsDoNotDependOnTheOldFreeForAllSetting(string hostilityMode)
    {
        ArenaContext.Config.HostilityMode = hostilityMode;
        var location = Location();
        Start(Session, "TEAMDEATHMATCH", location);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        var entries = location.BotLocationModifier!.AdditionalHostilitySettings!.ToList();
        AssertFaction(entries[0], "pmcUSEC", "pmcBEAR");
        AssertFaction(entries[1], "pmcBEAR", "pmcUSEC");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingPmcRoleSettingsAreAddedWithoutChangingOtherRoles(bool nullSettings)
    {
        var location = Location();
        var other = location.BotLocationModifier!.AdditionalHostilitySettings!.Last();
        location.BotLocationModifier.AdditionalHostilitySettings = nullSettings ? null : [other];
        Start(Session, "teamdeathmatch", location);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        var entries = location.BotLocationModifier.AdditionalHostilitySettings!.ToList();
        Assert.Equal(nullSettings ? 2 : 3, entries.Count);
        AssertFaction(entries.Single(entry => entry.BotRole == "pmcUSEC"), "pmcUSEC", "pmcBEAR", false);
        AssertFaction(entries.Single(entry => entry.BotRole == "pmcBEAR"), "pmcBEAR", "pmcUSEC", false);
        if (!nullSettings)
            Assert.Same(other, entries[0]);
    }

    [Fact]
    public void OrdinaryCloneOnTheSameMapRemainsByteIdentical()
    {
        Start(Session, "teamdeathmatch", Location());
        var ordinary = Location();
        var before = JsonSerializer.Serialize(ordinary);
        ArenaAdjustBotHostilityPatch.Postfix(ordinary);
        Assert.Equal(before, JsonSerializer.Serialize(ordinary));
        Assert.False(ArenaContext.Sessions!.TryGetStashedCloneMode(ordinary, out var mode));
        Assert.Null(mode);
    }

    [Theory]
    [InlineData("armed")]
    [InlineData("idle")]
    [InlineData("stale")]
    public void NonLiveClonesNeverReceiveTeamHostility(string state)
    {
        var location = Location();
        var sessions = ArenaContext.Sessions!;
        if (state == "armed")
        {
            sessions.Arm(Session, location.Id, "teamdeathmatch");
            sessions.StashLocationClone(Session, location);
        }
        else
        {
            Start(Session, "teamdeathmatch", location);
            if (state == "idle")
                sessions.MarkIdle(Session);
            else
            {
                var entries = (IDictionary)typeof(ArenaSessionService).GetField(
                    "_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sessions)!;
                var entry = entries[Session.ToString()]!;
                entry.GetType().GetField("InRaidAtUtc")!.SetValue(entry, DateTime.UtcNow.AddHours(-1));
            }
        }

        var before = JsonSerializer.Serialize(location);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        Assert.Equal(before, JsonSerializer.Serialize(location));
        Assert.False(sessions.TryGetStashedCloneMode(location, out _));
    }

    [Theory]
    [InlineData("endless")]
    [InlineData("rounds")]
    [InlineData("gungame")]
    [InlineData("tactical")]
    public void OtherActiveModesStillUseTheirExistingFfaPolicy(string mode)
    {
        var location = Location();
        Start(Session, "teamdeathmatch", Location());
        Start(OtherSession, mode, location);
        Assert.True(ArenaContext.Sessions!.TryGetStashedCloneMode(location, out var actualMode));
        Assert.Equal(mode, actualMode);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        Assert.All(location.BotLocationModifier!.AdditionalHostilitySettings!, entry =>
        {
            Assert.Equal("AlwaysEnemies", entry.UsecPlayerBehaviour);
            Assert.Equal("AlwaysEnemies", entry.BearPlayerBehaviour);
            Assert.Equal(100, entry.UsecEnemyChance);
            Assert.Equal(100, entry.BearEnemyChance);
            Assert.Empty(entry.AlwaysFriends!);
            Assert.All(entry.ChancedEnemies!, chance => Assert.Equal(100, chance.EnemyChance));
        });
    }

    [Fact]
    public void OtherModesKeepAllVsPlayerSemanticsAndSharedChanceRecords()
    {
        ArenaContext.Config.HostilityMode = "all_vs_player";
        var location = Location();
        var source = location.BotLocationModifier!.AdditionalHostilitySettings!.First();
        var sharedChances = source.ChancedEnemies;
        var friends = source.AlwaysFriends;
        Start(Session, "endless", location);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        Assert.Same(sharedChances, source.ChancedEnemies);
        Assert.Same(friends, source.AlwaysFriends);
        Assert.Equal(100, source.ChancedEnemies![0].EnemyChance);
        Assert.Equal(17, source.ChancedEnemies[2].EnemyChance);
        Assert.Equal("AlwaysEnemies", source.UsecPlayerBehaviour);
        Assert.Equal("AlwaysEnemies", source.BearPlayerBehaviour);
        Assert.Equal("Warn", source.SavagePlayerBehaviour);
        Assert.Equal(19, source.SavageEnemyChance);
    }

    [Fact]
    public void AbsentSessionOrModifierFailsOpen()
    {
        var location = Location();
        location.BotLocationModifier = null!;
        Start(Session, "teamdeathmatch", location);
        ArenaAdjustBotHostilityPatch.Postfix(location);
        Assert.Null(location.BotLocationModifier);
        Assert.False(ArenaContext.Sessions!.TryGetStashedCloneMode(null!, out _));
        ArenaContext.Sessions = null;
        var ordinary = Location();
        var before = JsonSerializer.Serialize(ordinary);
        ArenaAdjustBotHostilityPatch.Postfix(ordinary);
        ArenaAdjustBotHostilityPatch.Postfix(null!);
        Assert.Equal(before, JsonSerializer.Serialize(ordinary));
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
        Id = "factory4_day",
        BotLocationModifier = new BotLocationModifier
        {
            AdditionalHostilitySettings = [Entry("pmcUSEC"), Entry("PMCBEAR"), Entry("assault")],
        },
    };

    private static AdditionalHostilitySettings Entry(string role) => new()
    {
        BotRole = role,
        AlwaysFriends = ["pmcUSEC", "pmcBEAR", "gifter"],
        AlwaysEnemies = ["pmcUSEC", "pmcBEAR", "bossKilla"],
        Warn = ["pmcUSEC", "pmcBEAR", "marksman"],
        Neutral = ["pmcUSEC", "pmcBEAR", "arenaFighter"],
        ChancedEnemies =
        [
            new ChancedEnemy { Role = "pmcUSEC", EnemyChance = 100 },
            new ChancedEnemy { Role = "pmcBEAR", EnemyChance = 100 },
            new ChancedEnemy { Role = "assault", EnemyChance = 17 },
        ],
        UsecPlayerBehaviour = "AlwaysEnemies",
        UsecEnemyChance = 100,
        BearPlayerBehaviour = "AlwaysEnemies",
        BearEnemyChance = 100,
        SavagePlayerBehaviour = "Warn",
        SavageEnemyChance = 19,
    };

    private static void AssertFaction(AdditionalHostilitySettings entry, string own, string enemy, bool hasOtherRoles = true)
    {
        Assert.Equal(own, entry.BotRole);
        Assert.Contains(own, entry.AlwaysFriends!);
        Assert.DoesNotContain(enemy, entry.AlwaysFriends!);
        Assert.Contains(enemy, entry.AlwaysEnemies!);
        Assert.DoesNotContain(own, entry.AlwaysEnemies!);
        Assert.DoesNotContain(entry.ChancedEnemies!, chance => chance.Role == own || chance.Role == enemy);
        Assert.DoesNotContain(own, entry.Neutral!);
        Assert.DoesNotContain(enemy, entry.Neutral!);
        Assert.DoesNotContain(own, entry.Warn!);
        Assert.DoesNotContain(enemy, entry.Warn!);
        Assert.Equal(own == "pmcUSEC" ? "AlwaysFriends" : "AlwaysEnemies", entry.UsecPlayerBehaviour);
        Assert.Equal(own == "pmcBEAR" ? "AlwaysFriends" : "AlwaysEnemies", entry.BearPlayerBehaviour);
        Assert.Equal(own == "pmcUSEC" ? 0 : 100, entry.UsecEnemyChance);
        Assert.Equal(own == "pmcBEAR" ? 0 : 100, entry.BearEnemyChance);
        if (hasOtherRoles)
        {
            Assert.Contains("gifter", entry.AlwaysFriends!);
            Assert.Contains("bossKilla", entry.AlwaysEnemies!);
            Assert.Contains("marksman", entry.Warn!);
            Assert.Contains("arenaFighter", entry.Neutral!);
            Assert.Equal(17, Assert.Single(entry.ChancedEnemies!).EnemyChance);
        }
    }
}
