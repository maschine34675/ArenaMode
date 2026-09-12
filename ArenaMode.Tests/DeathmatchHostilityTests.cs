using System.Collections;
using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

// Reuse the real route/snapshot fixture and serialized ArenaContext isolation.
// These are new Deathmatch cases, not a model of the production state machine.
public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData("deathmatch", null)]
    [InlineData("deathmatch", "")]
    [InlineData("deathmatch", "FFA")]
    [InlineData("deathmatch", "invalid")]
    [InlineData("tactical", "ffa")]
    [InlineData("teamdeathmatch", "all_vs_player")]
    [InlineData("gungame", "ffa")]
    [InlineData("endless", "ffa")]
    [InlineData("rounds", "all_vs_player")]
    public async Task DeathmatchInvalidHostilityHasNoSnapshotPreparationRecoveryOrArmSideEffects(string mode, string? choice)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = Map, Mode = mode, RequestId = Token, DeathmatchHostility = choice };
        using var fresh = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(fresh.RootElement.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(fresh.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Null(Sessions.GetDeathmatchHostility(Session));
        Assert.False(Sessions.BeginArmPreparation(Session, Map, mode, false, Token, deathmatchHostility: choice));
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, Map, mode, armToken: Token, deathmatchHostility: choice));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.False(Sessions.IsExternalProfileWriteProtected(Session));

        Assert.True(ArenaContext.Snapshots.TakeSnapshot(Session));
        using var stale = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(stale.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(ArenaContext.Snapshots.HasSnapshot(Session));
        Assert.Equal(1, cloner.Calls); // only the deliberate capture above
        Assert.Same(original, saves.GetProfile(Session));

        Sessions.Arm(Session, Map, DeathmatchSelection.ModeId, armToken: Token, deathmatchHostility: "ffa");
        using var armed = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(armed.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal("ffa", Sessions.GetDeathmatchHostility(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Theory]
    [InlineData("ffa")]
    [InlineData("all_vs_player")]
    public async Task DeathmatchRouteEchoAndRetryRequireTheExactFrozenChoice(string choice)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = new ArenaRaidStartRequest
            { Map = Map, Mode = DeathmatchSelection.ModeId, RequestId = Token, DeathmatchHostility = choice };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(choice, first.RootElement.GetProperty("deathmatchHostility").GetString());
        Assert.Equal(choice, Sessions.GetDeathmatchHostility(Session));
        Assert.Null(Sessions.GetDeathmatchHostility(Other));
        Assert.Equal(1, cloner.Calls);

        // A live server config change cannot reinterpret an accepted request.
        ArenaContext.Config.HostilityMode = choice == "ffa" ? "all_vs_player" : "ffa";
        using var replay = JsonDocument.Parse(await InvokeStart(router, request with { Mode = "DEATHMATCH" }));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(Token, replay.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(choice, replay.RootElement.GetProperty("deathmatchHostility").GetString());
        foreach (var changed in new[]
        {
            request with { DeathmatchHostility = choice == "ffa" ? "all_vs_player" : "ffa" },
            request with { DeathmatchHostility = null },
            request with { Mode = "endless", DeathmatchHostility = null },
            request with { Mode = "rounds", DeathmatchHostility = null },
            request with { RequestId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
        })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, changed));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(choice, Sessions.GetDeathmatchHostility(Session));
            Assert.Equal(Token, Sessions.GetArmToken(Session));
        }
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(choice, status.RootElement.GetProperty("deathmatchHostility").GetString());
        Sessions.MarkIdle(Session);
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Null(idle.RootElement.GetProperty("deathmatchHostility").GetString());
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("idle")]
    [InlineData("failed-start")]
    [InlineData("native-end")]
    public void DeathmatchChoiceLivesUntilItsExactArmReturnsToIdle(string path)
    {
        Assert.True(Sessions.BeginArmPreparation(Session, Map, "deathmatch", false, Token, deathmatchHostility: "all_vs_player"));
        Assert.Equal("all_vs_player", Sessions.GetDeathmatchHostility(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, Map, "deathmatch", false, Token, out _, deathmatchHostility: "all_vs_player"));
        if (path == "preparing") Sessions.CancelArmPreparation(Session);
        else if (path == "idle") Sessions.MarkIdle(Session);
        else
        {
            Assert.True(Sessions.CompleteArmPreparation(Session));
            Assert.True(Sessions.TryReplayArmedRequest(Session, Map, "deathmatch", false, Token, out var replay, deathmatchHostility: "all_vs_player"));
            Assert.Equal(Token, replay);
            Assert.False(Sessions.TryReplayArmedRequest(Session, Map, "deathmatch", false, Token, out _, deathmatchHostility: "ffa"));
            Assert.False(Sessions.TryReplayArmedRequest(Other, Map, "deathmatch", false, Token, out _, deathmatchHostility: "all_vs_player"));
            string end = Start();
            Assert.Equal("all_vs_player", Sessions.GetDeathmatchHostility(Session));
            if (path == "failed-start")
            {
                Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
                Assert.Equal("all_vs_player", Sessions.GetDeathmatchHostility(Session));
                Sessions.BeginPostRaidFinalization(Session);
            }
            else
            {
                Assert.True(Sessions.BeginNativeEndFinalization(Session, end));
                Assert.Equal("all_vs_player", Sessions.GetDeathmatchHostility(Session));
                Assert.True(Sessions.CompleteNativeEndFinalization(Session, end));
            }
        }
        Assert.Null(Sessions.GetDeathmatchHostility(Session));
    }

    [Theory]
    [InlineData("ffa")]
    [InlineData("all_vs_player")]
    public void DeathmatchCloneUsesFrozenChoiceAndNeverWritesSharedRecords(string choice)
    {
        var location = DeathmatchLocation();
        var sourceEntries = location.BotLocationModifier!.AdditionalHostilitySettings!;
        string source = JsonSerializer.Serialize(sourceEntries);
        ArenaContext.Config.HostilityMode = choice == "ffa" ? "all_vs_player" : "ffa";
        Sessions.Arm(Session, Map, "deathmatch", armToken: Token, deathmatchHostility: choice);
        Start();
        Sessions.StashLocationClone(Session, location);
        Assert.True(Sessions.TryGetStashedCloneMode(location, out var mode, out var actualChoice));
        Assert.Equal("deathmatch", mode);
        Assert.Equal(choice, actualChoice);
        AdjustBotHostilityPatch.Postfix(location);
        var result = location.BotLocationModifier.AdditionalHostilitySettings!.ToList();
        Assert.Equal(source, JsonSerializer.Serialize(sourceEntries));
        Assert.NotSame(sourceEntries, result);
        var knownRoles = new[] { "pmcUSEC", "pmcBEAR", "assault", "marksman", "bossKilla", "arenaFighter", "gifter" };
        foreach (var entry in result)
        {
            Assert.Equal("AlwaysEnemies", entry.UsecPlayerBehaviour);
            Assert.Equal("AlwaysEnemies", entry.BearPlayerBehaviour);
            Assert.Equal(100, entry.UsecEnemyChance);
            Assert.Equal(100, entry.BearEnemyChance);
            Assert.Empty(entry.Neutral!);
            Assert.Empty(entry.Warn!);
            Assert.Empty(entry.ChancedEnemies!);
            foreach (var role in knownRoles)
                Assert.Contains(role, choice == "ffa" ? entry.AlwaysEnemies! : entry.AlwaysFriends!);
            Assert.Empty(choice == "ffa" ? entry.AlwaysFriends! : entry.AlwaysEnemies!);
        }
        // No collection may be shared with another generated role or original.
        result[0].AlwaysFriends!.Add("test-only");
        Assert.DoesNotContain("test-only", result[1].AlwaysFriends!);
        Assert.Equal(source, JsonSerializer.Serialize(sourceEntries));
        Assert.Equal(choice == "ffa" ? "all_vs_player" : "ffa", ArenaContext.Config.HostilityMode);
    }

    [Theory]
    [InlineData("ffa")]
    [InlineData("all_vs_player")]
    public void DeathmatchMissingRoleRecordsAreAddedAndRepeatedApplicationIsStable(string choice)
    {
        var location = DeathmatchLocation();
        location.BotLocationModifier!.AdditionalHostilitySettings = null;
        Sessions.Arm(Session, Map, "deathmatch", armToken: Token, deathmatchHostility: choice);
        Start();
        Sessions.StashLocationClone(Session, location);
        AdjustBotHostilityPatch.Postfix(location);
        var entries = location.BotLocationModifier.AdditionalHostilitySettings!.ToList();
        Assert.Equal(new[] { "pmcUSEC", "pmcBEAR", "assault", "marksman" }, entries.Select(entry => entry.BotRole));
        foreach (var entry in entries)
            Assert.Equal(4, (choice == "ffa" ? entry.AlwaysEnemies : entry.AlwaysFriends)!.Count);
        string first = JsonSerializer.Serialize(location);
        AdjustBotHostilityPatch.Postfix(location);
        Assert.Equal(first, JsonSerializer.Serialize(location));
    }

    [Theory]
    [InlineData("different-object")]
    [InlineData("preparing")]
    [InlineData("armed")]
    [InlineData("idle")]
    [InlineData("expired")]
    public void DeathmatchOnlyTheExactLiveStashedCloneReceivesAnyOverride(string boundary)
    {
        var location = DeathmatchLocation();
        if (boundary == "preparing")
            Assert.True(Sessions.BeginArmPreparation(Session, Map, "deathmatch", false, Token, deathmatchHostility: "ffa"));
        else
            Sessions.Arm(Session, Map, "deathmatch", armToken: Token, deathmatchHostility: "ffa");
        if (boundary is not ("preparing" or "armed")) Start();
        Sessions.StashLocationClone(Session, boundary == "different-object" ? DeathmatchLocation() : location);
        if (boundary == "idle") Sessions.MarkIdle(Session);
        if (boundary == "expired")
        {
            var store = (IDictionary)typeof(ArenaSessionService).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Sessions)!;
            var entry = store[Session.ToString()]!;
            entry.GetType().GetField("InRaidAtUtc")!.SetValue(entry, DateTime.UtcNow.AddDays(-3));
        }
        string before = JsonSerializer.Serialize(location);
        Assert.False(Sessions.TryGetStashedCloneMode(location, out _, out _));
        AdjustBotHostilityPatch.Postfix(location);
        Assert.Equal(before, JsonSerializer.Serialize(location));
    }

    [Fact]
    public void DeathmatchRequestDtoRejectsNonStringChoice()
    {
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{}")!.DeathmatchHostility);
        Assert.Equal("ffa", JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"deathmatchHostility\":\"ffa\"}")!.DeathmatchHostility);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"deathmatchHostility\":true}"));
    }

    private static LocationBase DeathmatchLocation() => new()
    {
        Id = Map,
        BotLocationModifier = new BotLocationModifier
        {
            AdditionalHostilitySettings =
            [
                new AdditionalHostilitySettings
                {
                    BotRole = "pmcUSEC", AlwaysFriends = ["pmcBEAR", "gifter"],
                    AlwaysEnemies = ["pmcUSEC", "bossKilla"], Neutral = ["arenaFighter"], Warn = ["marksman"],
                    ChancedEnemies = [new ChancedEnemy { Role = "assault", EnemyChance = 17 }],
                    UsecPlayerBehaviour = "Warn", BearPlayerBehaviour = "AlwaysFriends",
                    UsecEnemyChance = 12, BearEnemyChance = 0,
                },
            ],
        },
    };
}
