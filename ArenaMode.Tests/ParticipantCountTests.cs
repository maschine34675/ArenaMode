using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

// Uses the production route, session machine, profile snapshot, and location
// mutator through the existing serialized ArenaContext fixture.
public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData("deathmatch", 2)]
    [InlineData("deathmatch", 3)]
    [InlineData("deathmatch", 24)]
    [InlineData("gungame", 2)]
    [InlineData("gungame", 23)]
    [InlineData("teamdeathmatch", 4)]
    [InlineData("teamdeathmatch", 24)]
    public async Task ParticipantRouteEchoReplayAndStatusKeepTheExactTotal(string mode, int total)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = ParticipantRequest(Map, mode, total);
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(total, first.RootElement.GetProperty("participants").GetInt32());
        Assert.False(first.RootElement.GetProperty("participantBosses").GetBoolean());
        Assert.Equal(total, Sessions.GetParticipants(Session));
        Assert.Null(Sessions.GetParticipants(Other));

        ArenaContext.Config.Maps[Map].BotMax = 97;
        using var replay = JsonDocument.Parse(await InvokeStart(router,
            request with { Map = Map.ToUpperInvariant(), Mode = mode.ToUpperInvariant() }));
        Assert.True(replay.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(Token, replay.RootElement.GetProperty("armToken").GetString());
        Assert.Equal(total, replay.RootElement.GetProperty("participants").GetInt32());
        foreach (var changed in new[]
        {
            request with { Participants = total == 24 ? 4 : 24 },
            request with { Participants = null },
            request with { RequestId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
        })
        {
            using var rejected = JsonDocument.Parse(await InvokeStart(router, changed));
            Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
            Assert.Equal(total, Sessions.GetParticipants(Session));
            Assert.Equal(Token, Sessions.GetArmToken(Session));
        }
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(total, status.RootElement.GetProperty("participants").GetInt32());
        Assert.False(status.RootElement.GetProperty("participantBosses").GetBoolean());
        Sessions.MarkIdle(Session);
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(JsonValueKind.Null, idle.RootElement.GetProperty("participants").ValueKind);
        Assert.False(idle.RootElement.GetProperty("participantBosses").GetBoolean());
    }

    public static IEnumerable<object?[]> ParticipantInvalidRequests()
    {
        foreach (int total in new[] { int.MinValue, -1, 0, 1, 25, int.MaxValue })
            yield return [Map, "deathmatch", null, null, false, total];
        foreach (int total in new[] { 2, 3, 5, 23, 25 })
            yield return [Map, Mode, null, null, false, total];
        yield return [Map, "tactical", null, null, false, 4];
        yield return [Map, "tactical", null, null, true, 10];
        yield return [Map, "endless", null, null, false, 4];
        yield return [Map, "rounds", null, null, false, 4];
        yield return [Customs, Mode, TeamArea, 3, false, 6];
        yield return [Customs, Mode, CustomsArea, null, false, 4];
        yield return ["woods", "gungame", null, null, false, 8];
    }

    [Theory]
    [MemberData(nameof(ParticipantInvalidRequests))]
    public async Task ParticipantInvalidRequestHasNoPreparationSnapshotRecoveryOrArmSideEffects(
        string map, string mode, string? area, int? areaSize, bool pft, int total)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = ParticipantRequest(map, mode, total) with
            { AreaId = area, AreaTeamSize = areaSize, PitFireTeamTeam = pft };
        using var fresh = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(fresh.RootElement.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(fresh.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.False(Sessions.BeginArmPreparation(Session, map, mode, pft, Token, area, areaSize,
            request.DeathmatchHostility, total));
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, Token, area, areaSize,
            request.DeathmatchHostility, total));
        Assert.False(Sessions.IsExternalProfileWriteProtected(Session));
        Assert.Null(Sessions.GetParticipants(Session));

        Assert.True(ArenaContext.Snapshots.TakeSnapshot(Session));
        using var stale = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(stale.RootElement.GetProperty("armed").GetBoolean());
        Assert.True(ArenaContext.Snapshots.HasSnapshot(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));

        Sessions.Arm(Session, Map, "gungame", armToken: Token, participants: 4);
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, map, mode, pft, Token, area, areaSize,
            request.DeathmatchHostility, total));
        using var armed = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(armed.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(4, Sessions.GetParticipants(Session));
        Assert.Equal(1, cloner.Calls);
        Assert.Same(original, saves.GetProfile(Session));
    }

    [Theory]
    [InlineData("deathmatch", false, 4, true)]
    [InlineData("gungame", false, 2, true)]
    [InlineData("gungame", true, 24, false)]
    [InlineData("teamdeathmatch", false, 10, false)]
    [InlineData("gungame", false, null, false)]
    public async Task ParticipantBossSlotIsServerOwnedAndFrozenAcrossLostResponseAndReload(
        string mode, bool disabled, int? total, bool expected)
    {
        const string map = ArenaMapCatalog.LabyrinthId;
        ArenaContext.Config.Maps[map] = new() { Enabled = true, DisableBosses = disabled };
        var (router, _, _, cloner) = CustomsRouter();
        var request = ParticipantRequest(map, mode, total);
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(expected, first.RootElement.GetProperty("participantBosses").GetBoolean());
        Assert.Equal(expected, Sessions.GetParticipantBosses(Session));
        ArenaContext.Config.Maps[map].DisableBosses = !disabled;
        using var retry = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(retry.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(expected, retry.RootElement.GetProperty("participantBosses").GetBoolean());
        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(expected, status.RootElement.GetProperty("participantBosses").GetBoolean());
        Assert.Equal(1, cloner.Calls);
        Sessions.MarkIdle(Session);
        Assert.False(Sessions.GetParticipantBosses(Session));
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("idle")]
    [InlineData("failed-start")]
    [InlineData("native-end")]
    public void ParticipantSelectionLivesThroughItsExactArmAndRecoveryUntilIdle(string path)
    {
        const string map = ArenaMapCatalog.LabyrinthId;
        ArenaContext.Config.Maps[map] = new() { Enabled = true, DisableBosses = false };
        Assert.True(Sessions.BeginArmPreparation(Session, map, "gungame", false, Token, participants: 4));
        Assert.Equal(4, Sessions.GetParticipants(Session));
        Assert.True(Sessions.GetParticipantBosses(Session));
        Assert.False(Sessions.TryReplayArmedRequest(Session, map, "gungame", false, Token, out _, participants: 4));
        ArenaContext.Config.Maps[map].DisableBosses = true;
        if (path == "preparing") Sessions.CancelArmPreparation(Session);
        else if (path == "idle") Sessions.MarkIdle(Session);
        else
        {
            Assert.True(Sessions.CompleteArmPreparation(Session));
            Assert.True(Sessions.TryReplayArmedRequest(Session, map, "gungame", false, Token, out _, participants: 4));
            Assert.True(Sessions.GetParticipantBosses(Session));
            string end = Start(map);
            if (path == "failed-start")
            {
                Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
                Assert.Equal(4, Sessions.GetParticipants(Session));
                Assert.True(Sessions.GetParticipantBosses(Session));
                Sessions.BeginPostRaidFinalization(Session);
            }
            else
            {
                Assert.True(Sessions.BeginNativeEndFinalization(Session, end));
                Assert.Equal(4, Sessions.GetParticipants(Session));
                Assert.True(Sessions.GetParticipantBosses(Session));
                Assert.True(Sessions.CompleteNativeEndFinalization(Session, end));
            }
        }
        Assert.Null(Sessions.GetParticipants(Session));
        Assert.False(Sessions.GetParticipantBosses(Session));
    }

    [Fact]
    public void ParticipantDtoKeepsLegacyOmissionAndRejectsNonIntegerCounts()
    {
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{}")!.Participants);
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"participants\":null}")!.Participants);
        Assert.Equal(0, JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"participants\":0}")!.Participants);
        Assert.Equal(4, JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"participants\":4}")!.Participants);
        foreach (string value in new[] { "3.5", "\"4\"", "true", "2147483648", "{}", "[]" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"participants\":" + value + "}"));
    }

    [Theory]
    [InlineData("deathmatch", 2, false)]
    [InlineData("deathmatch", 4, true)]
    [InlineData("gungame", 24, false)]
    [InlineData("gungame", 3, true)]
    [InlineData("teamdeathmatch", 24, true)]
    public void ParticipantMutatorClosesEverySpawnTrackAndRetainsOnlyRequestedCapacity(string mode, int total, bool abps)
    {
        var field = typeof(ArenaLocationMutator).GetField("_abpsPresent", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? old = field.GetValue(null);
        field.SetValue(null, abps);
        try
        {
            var original = ParticipantLocation();
            string before = JsonSerializer.Serialize(original);
            var clone = original with { }; // Deliberately alias child records: selected mutation must not write them.
            var spawnPoints = clone.SpawnPointParams;
            ArenaLocationMutator.Mutate(clone, new() { BotMax = 91, MaxBotPerZone = 40, DisableBosses = false },
                new(), mode, participants: total);
            Assert.Equal(total - 1, clone.BotMax);
            Assert.Equal(total - 1, clone.BotMaxPvE);
            Assert.Equal(total - 1, clone.MaxBotPerZone);
            Assert.Empty(clone.Waves!);
            Assert.Empty(clone.BossLocationSpawn!);
            Assert.False(clone.NewSpawn);
            Assert.False(clone.OfflineNewSpawn);
            Assert.Equal(0, clone.BotSpawnCountStep);
            Assert.Equal(0, clone.BotLocationModifier!.NonWaveSpawnBotsLimitPerPlayer);
            Assert.Equal(0, clone.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE);
            Assert.Same(spawnPoints, clone.SpawnPointParams);
            Assert.Equal(before, JsonSerializer.Serialize(original));
            ArenaLocationMutator.ValidateParticipantPopulation(clone, Map, mode, total);
        }
        finally { field.SetValue(null, old); }
    }

    [Theory]
    [InlineData("deathmatch", 4)]
    [InlineData("gungame", 2)]
    [InlineData("teamdeathmatch", 24)]
    public void ParticipantNativeCloneIdentityAndClientConfirmationRequireTheFrozenPopulation(string mode, int total)
    {
        Sessions.Arm(Session, Map, mode, armToken: Token,
            deathmatchHostility: mode == "deathmatch" ? "ffa" : null, participants: total);
        string end = Start();
        Assert.False(Sessions.ValidateAreaLocation(Session, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, end));
        var location = ParticipantLocation();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Map, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var reason), reason);
        Assert.False(Sessions.ValidateAreaResponse(Session, location with { }, out _));
        Assert.False(Sessions.ValidateAreaResponse(Session, null, out _));
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, end));
        Assert.Equal(total, Sessions.GetParticipants(Session));
    }

    public static IEnumerable<object[]> ParticipantCloneDrifts()
    {
        foreach (string drift in new[] { "map", "bot-cap", "pve-cap", "zone-cap", "waves", "bosses",
                     "null-waves", "null-bosses", "new-spawn", "offline-spawn", "start", "stop", "step",
                     "on-min", "on-max", "off-min", "off-max", "modifier", "limit", "pve-limit" })
            yield return [drift];
    }

    [Theory]
    [MemberData(nameof(ParticipantCloneDrifts))]
    public void ParticipantFinalCloneDriftBlocksNativeResponseAndClientActivation(string drift)
    {
        Sessions.Arm(Session, Map, "gungame", armToken: Token, participants: 4);
        string end = Start();
        var location = ParticipantLocation();
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Map, location);
        Assert.True(Sessions.ValidateAreaResponse(Session, location, out var valid), valid);
        switch (drift)
        {
            case "map": location.Id = ArenaMapCatalog.FactoryNightId; break;
            case "bot-cap": location.BotMax = 4; break;
            case "pve-cap": location.BotMaxPvE = 4; break;
            case "zone-cap": location.MaxBotPerZone = 4; break;
            case "waves": location.Waves = [new() { SlotsMax = 1 }]; break;
            case "bosses": location.BossLocationSpawn = [new() { BossName = "bossTagillaAgro", BossChance = 0 }]; break;
            case "null-waves": location.Waves = null!; break;
            case "null-bosses": location.BossLocationSpawn = null!; break;
            case "new-spawn": location.NewSpawn = true; break;
            case "offline-spawn": location.OfflineNewSpawn = true; break;
            case "start": location.BotStart = 0; break;
            case "stop": location.BotStop = 9999; break;
            case "step": location.BotSpawnCountStep = 1; break;
            case "on-min": location.BotSpawnTimeOnMin = 1; break;
            case "on-max": location.BotSpawnTimeOnMax = 1; break;
            case "off-min": location.BotSpawnTimeOffMin = 0; break;
            case "off-max": location.BotSpawnTimeOffMax = 0; break;
            case "modifier": location.BotLocationModifier = null!; break;
            case "limit": location.BotLocationModifier!.NonWaveSpawnBotsLimitPerPlayer = 1; break;
            case "pve-limit": location.BotLocationModifier!.NonWaveSpawnBotsLimitPerPlayerPvE = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(drift));
        }
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.False(Sessions.ValidateAreaLocation(Session, out _));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, end));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
        Assert.Equal(4, Sessions.GetParticipants(Session));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("removed")]
    [InlineData("no-clone")]
    [InlineData("wrong-callback-map")]
    [InlineData("wrong-clone-map")]
    public void ParticipantMissingOrMismatchedMutationCannotFallBackToVanilla(string failure)
    {
        Sessions.Arm(Session, Map, "gungame", armToken: Token, participants: 4);
        string end = Start();
        var location = ParticipantLocation();
        string map = Map;
        if (failure == "disabled") ArenaContext.Config.Maps[Map].Enabled = false;
        if (failure == "removed") ArenaContext.Config.Maps.Remove(Map);
        if (failure == "wrong-callback-map") map = ArenaMapCatalog.FactoryNightId;
        if (failure == "wrong-clone-map") location.Id = ArenaMapCatalog.FactoryNightId;
        string before = JsonSerializer.Serialize(location);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, map, failure == "no-clone" ? null! : location);
        Assert.Equal(before, JsonSerializer.Serialize(location));
        Assert.False(Sessions.ValidateAreaResponse(Session, location, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session, Token, end));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session, Token));
    }

    [Fact]
    public void ParticipantLegacyAndOrdinaryMapsKeepTheirExistingContracts()
    {
        var original = ParticipantLocation();
        string before = JsonSerializer.Serialize(original);
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Map, original);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Sessions.Arm(Session, Map, "gungame", armToken: Token);
        string end = Start();
        Assert.Null(Sessions.GetParticipants(Session));
        Assert.False(Sessions.GetParticipantBosses(Session));
        Assert.True(Sessions.ValidateAreaResponse(Session, null, out _));
        Assert.True(Sessions.ConfirmClientRaidStarted(Session, Token, end));
        ArenaGenerateLocationAndLootPatch.Postfix(Other, Map, original);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        ArenaGenerateLocationAndLootPatch.Postfix(Session, Map, original);
        Assert.True(original.NewSpawn);
        Assert.True(original.OfflineNewSpawn);
        Assert.NotEmpty(original.BossLocationSpawn!);
        Assert.Equal(ArenaContext.Config.Maps[Map].BotMax, original.BotMax);
    }

    [Theory]
    [InlineData(ArenaMapCatalog.EquatorId, 4)]
    [InlineData(ArenaMapCatalog.EquatorId, 24)]
    [InlineData(ArenaMapCatalog.Bay5Id, 2)]
    [InlineData(ArenaMapCatalog.LabyrinthId, 24)]
    public void ParticipantPolicyAllowsLargerSelectionsAndMutatorSuppliesMissingPumpRecord(string map, int total)
    {
        Assert.True(ArenaParticipantPolicy.TryResolve(map, "gungame", false, false, total, out var selected, out _));
        Assert.Equal(total, selected);
        var location = new LocationBase { Id = map };
        ArenaLocationMutator.Mutate(location, new(), new(), "gungame", participants: total);
        Assert.NotNull(location.BotLocationModifier);
        ArenaLocationMutator.ValidateParticipantPopulation(location, map, "gungame", total);
        Assert.False(ArenaParticipantPolicy.TryResolve(map, "gungame", false, true, total, out _, out _));
        Assert.True(ArenaParticipantPolicy.TryResolve(map, "gungame", true, false, total, out var bounded, out _));
        Assert.Equal(total, bounded);
        Assert.False(ArenaParticipantPolicy.TryResolve(map, "teamdeathmatch", true, false, total, out _, out _));
    }

    [Fact]
    public void ParticipantLabsCloneCannotChangeAliasedNativeWaveRecordsWhileRemovingThem()
    {
        var original = ParticipantLocation();
        original.Id = ArenaMapCatalog.LaboratoryId;
        original.Waves = [new() { SpawnPoints = "BotZone,GateBotZone", SlotsMax = 4 }];
        original.BossLocationSpawn = [new() { BossName = "pmcUSEC", BossChance = 100, BossZone = "BotZone,GateBotZone" }];
        string before = JsonSerializer.Serialize(original);
        var clone = original with { };
        ArenaLocationMutator.Mutate(clone, new(), new(), "gungame", participants: 4);
        ArenaLocationMutator.ValidateParticipantPopulation(clone, ArenaMapCatalog.LaboratoryId, "gungame", 4);
        Assert.Empty(clone.Waves!);
        Assert.Empty(clone.BossLocationSpawn!);
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    private static ArenaRaidStartRequest ParticipantRequest(string map, string mode, int? total)
        => new()
        {
            Map = map, Mode = mode, RequestId = Token, Participants = total,
            DeathmatchHostility = mode == "deathmatch" ? "ffa" : null,
        };

    private static LocationBase ParticipantLocation() => new()
    {
        Id = Map, BotMax = 24, BotMaxPvE = 24, MaxBotPerZone = 15,
        Waves = [new() { SpawnPoints = "BotZone", SlotsMax = 4 }],
        BossLocationSpawn = [new() { BossName = "pmcUSEC", BossChance = 100 },
            new() { BossName = "bossTagillaAgro", BossChance = 100 }],
        NewSpawn = true, OfflineNewSpawn = true,
        BotLocationModifier = new() { NonWaveSpawnBotsLimitPerPlayer = 99, NonWaveSpawnBotsLimitPerPlayerPvE = 99 },
        SpawnPointParams = [Point("p1", 0, 0, 0, "Player"), Point("b1", 50, 0, 0, "Bot")],
    };
}
