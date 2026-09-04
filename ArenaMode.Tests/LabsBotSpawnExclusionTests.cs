using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace ArenaMode.Tests;

// These tests exercise both spawn strategies by temporarily controlling the
// mutator's cached ABPS detection. Do not race another location test collection.
[CollectionDefinition("Labs spawn strategies", DisableParallelization = true)]
public sealed class LabsSpawnStrategiesCollection;

[Collection("Labs spawn strategies")]
public class LabsBotSpawnExclusionTests
{
    private const string LegacyZones = "BotZoneFloor1,BotZoneFloor2,BotZoneBasement,BotZoneGate1,BotZoneGate2";
    private static readonly string[] AllowedZones = ["BotZoneFloor1", "BotZoneFloor2", "BotZoneBasement"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(), new StringToMongoIdConverter() },
    };

    private static LocationBase FreshLocation(string mapId = "laboratory") => new()
    {
        Id = mapId,
        Waves = [],
        BossLocationSpawn = [],
        SpawnPointParams = [],
    };

    [Theory]
    [InlineData("laboratory", "BotZoneGate1", true)]
    [InlineData("LABORATORY", "botzonegate2", true)]
    [InlineData("laboratory", "BotZoneFloor1", false)]
    [InlineData("laboratory", "BotZoneGate10", false)]
    [InlineData("factory4_day", "BotZoneGate1", false)]
    [InlineData("factory4_night", "BotZoneGate1", false)]
    [InlineData("labyrinth", "BotZoneGate2", false)]
    [InlineData(null, "BotZoneGate1", false)]
    [InlineData("laboratory", null, false)]
    public void PolicyMatchesOnlyTheTwoLabsGateZones(string? mapId, string? zone, bool excluded)
        => Assert.Equal(excluded, ArenaBotSpawnPolicy.IsExcludedZone(mapId, zone));

    [Fact]
    public void LabsDefaultNamesExactlyTheThreeAccessibleAreas()
        => Assert.Equal(AllowedZones, new ArenaModeConfig().Maps["laboratory"].SpawnZones.Split(','));

    [Fact]
    public void OlderConfigCannotReEnableGateWavesAndIsNotRewritten()
    {
        var location = FreshLocation();
        var map = new ArenaMapConfig { SpawnZones = LegacyZones };
        var config = new ArenaModeConfig();
        config.Maps["laboratory"] = map;
        var saved = JsonSerializer.Serialize(config);

        WithAbps(false, () => ArenaLocationMutator.Mutate(location, map, config));

        Assert.NotEmpty(location.Waves);
        Assert.Equal(AllowedZones.OrderBy(zone => zone),
            location.Waves.Select(wave => wave.SpawnPoints).Distinct().OrderBy(zone => zone));
        Assert.All(location.Waves, wave => Assert.DoesNotContain(',', wave.SpawnPoints!));
        Assert.NotEmpty(location.BossLocationSpawn);
        Assert.All(location.BossLocationSpawn, spawn => Assert.Equal(string.Join(',', AllowedZones), spawn.BossZone));
        Assert.Equal(saved, JsonSerializer.Serialize(config));
    }

    [Theory]
    [InlineData("BotZoneGate1,BotZoneGate2")]
    [InlineData(" BOTZONEGATE1 , botzonegate2 , ")]
    [InlineData("")]
    public void NoAllowedConfiguredZoneDoesNotBecomeARandomWave(string zones)
    {
        var location = FreshLocation();
        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { SpawnZones = zones }, new ArenaModeConfig()));

        Assert.Empty(location.Waves);
        Assert.Empty(location.BossLocationSpawn);
    }

    [Fact]
    public void ExistingExtractTriggersAndMixedBossZoneListsAreFiltered()
    {
        var location = FreshLocation();
        location.BossLocationSpawn =
        [
            new() { BossName = "pmcUSEC", BossZone = "BotZoneGate1", TriggerId = "extract-gate1", TriggerName = "interactObject" },
            new() { BossName = "pmcBEAR", BossZone = " BotZoneGate2, BOTZONEGATE1 ", TriggerId = "extract-gate2", TriggerName = "interactObject" },
            new() { BossName = "pmcUSEC", BossZone = "BotZoneGate1, BotZoneFloor2, BotZoneGate2", TriggerId = "mixed", TriggerName = "interactObject" },
            new() { BossName = "bossKilla", BossZone = "BotZoneBasement", BossChance = 42, TriggerId = "safe", TriggerName = "interactObject" },
        ];

        ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { SpawnZones = LegacyZones, DisableBosses = false, PmcWaveIntervalSeconds = 0 },
            new ArenaModeConfig());

        Assert.Equal(2, location.BossLocationSpawn.Count);
        Assert.DoesNotContain(location.BossLocationSpawn, spawn => string.IsNullOrWhiteSpace(spawn.BossZone));
        var mixed = Assert.Single(location.BossLocationSpawn, spawn => spawn.TriggerId == "mixed");
        Assert.Equal("BotZoneFloor2", mixed.BossZone);
        Assert.Equal("interactObject", mixed.TriggerName);
        var safe = Assert.Single(location.BossLocationSpawn, spawn => spawn.TriggerId == "safe");
        Assert.Equal("BotZoneBasement", safe.BossZone);
        Assert.Equal(42, safe.BossChance);
    }

    [Fact]
    public void AbpsRetainedWavesCannotKeepGateReferences()
    {
        var location = FreshLocation();
        location.Waves =
        [
            new() { Number = 1, SpawnPoints = "BotZoneGate1" },
            new() { Number = 2, SpawnPoints = "BotZoneGate2" },
            new() { Number = 3, SpawnPoints = "BotZoneGate2,BotZoneFloor2" },
            new() { Number = 4, SpawnPoints = "BotZoneBasement" },
        ];

        WithAbps(true, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { SpawnZones = LegacyZones }, new ArenaModeConfig()));

        Assert.Equal([3, 4], location.Waves.Select(wave => wave.Number));
        Assert.Equal(["BotZoneFloor2", "BotZoneBasement"], location.Waves.Select(wave => wave.SpawnPoints));
    }

    [Theory]
    [InlineData("Bot", false)]
    [InlineData("Bot,Boss", false)]
    [InlineData("BotPmc", false)]
    [InlineData("Player", true)]
    [InlineData("Coop,Group", true)]
    [InlineData("Coop,Opposite", true)]
    [InlineData("Player,Bot,Boss", true)]
    [InlineData("Group,Bot", true)]
    [InlineData("All", true)]
    [InlineData("None", true)]
    [InlineData("FutureCategory,Bot", true)]
    [InlineData("", true)]
    public void OnlyBotExclusiveMarkersAreRemoved(string categories, bool preserved)
    {
        var location = FreshLocation();
        var point = new SpawnPointParam
        {
            Id = "shared-marker", BotZoneName = "BotZoneGate1",
            Categories = categories.Length > 0 ? categories.Split(',') : null,
        };
        location.SpawnPointParams = [point];
        var saved = JsonSerializer.Serialize(point);

        ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "tactical");

        Assert.Equal(preserved ? 1 : 0, location.SpawnPointParams!.Count());
        Assert.Equal(saved, JsonSerializer.Serialize(point));
    }

    [Theory]
    [InlineData("factory4_day")]
    [InlineData("factory4_night")]
    [InlineData("labyrinth")]
    public void OtherArenaMapsKeepIdenticallyNamedZones(string mapId)
    {
        var location = FreshLocation(mapId);
        var marker = new SpawnPointParam { Id = "other-map", BotZoneName = "BotZoneGate1", Categories = ["Bot", "Boss"] };
        location.SpawnPointParams = [marker];
        location.Waves = [new() { SpawnPoints = "BotZoneGate2" }];
        location.BossLocationSpawn = [new() { BossName = "bossKilla", BossChance = 42, BossZone = "BotZoneGate1" }];

        WithAbps(true, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { SpawnZones = "BotZoneGate1", DisableBosses = false, PmcWaveIntervalSeconds = 0 },
            new ArenaModeConfig()));

        Assert.Same(marker, Assert.Single(location.SpawnPointParams!));
        Assert.Equal("BotZoneGate2", Assert.Single(location.Waves).SpawnPoints);
        Assert.Equal("BotZoneGate1", Assert.Single(location.BossLocationSpawn).BossZone);
        Assert.Equal(" BotZoneGate1, BotZoneGate2 ", ArenaBotSpawnPolicy.FilterZones(mapId, " BotZoneGate1, BotZoneGate2 "));
    }

    [SkippableTheory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "tactical")]
    public void InstalledLabsCloneLosesNineBotMarkersAndPreservesAllPlayerData(bool abps, string? mode)
    {
        var database = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        var path = Path.Combine(database, "locations", "laboratory", "base.json");
        Skip.IfNot(File.Exists(path), "Installed Labs database fixture is unavailable.");
        var original = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), JsonOptions)!;
        var originalSnapshot = JsonSerializer.Serialize(original, JsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(originalSnapshot, JsonOptions)!;
        var originalPoints = original.SpawnPointParams!.ToArray();
        var expected = originalPoints.Where(point => point.BotZoneName != "BotZoneGate1"
            && point.BotZoneName != "BotZoneGate2").ToArray();
        Assert.Equal(9, originalPoints.Length - expected.Length);
        Assert.Equal(75, originalPoints.Count(point => point.Categories!.Contains("Player")));
        Assert.Equal(40, originalPoints.Count(point => point.Categories!.Contains("Coop")));

        var config = new ArenaModeConfig();
        var map = new ArenaMapConfig { SpawnZones = LegacyZones, DisableBosses = false };
        WithAbps(abps, () => ArenaLocationMutator.Mutate(clone, map, config, mode));

        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(clone.SpawnPointParams, JsonOptions));
        Assert.Equal(75, clone.SpawnPointParams!.Count(point => point.Categories!.Contains("Player")));
        Assert.All(clone.BossLocationSpawn, spawn => AssertNoGates(spawn.BossZone));
        Assert.All(clone.Waves, wave => AssertNoGates(wave.SpawnPoints));
        Assert.Equal(originalSnapshot, JsonSerializer.Serialize(original, JsonOptions));

        // Repeated processing must not progressively discard unrelated spawn
        // markers or resurrect forbidden zones through appended PMC waves.
        WithAbps(abps, () => ArenaLocationMutator.Mutate(clone, map, config, mode));
        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(clone.SpawnPointParams, JsonOptions));
        Assert.All(clone.BossLocationSpawn, spawn => AssertNoGates(spawn.BossZone));
        Assert.All(clone.Waves, wave => AssertNoGates(wave.SpawnPoints));
        Assert.Equal(originalSnapshot, JsonSerializer.Serialize(original, JsonOptions));
    }

    [SkippableFact]
    public void InstalledLabsAlwaysOffersFiveSafeBotPointsAwayFromEveryPlayerSpawn()
    {
        var database = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        var path = Path.Combine(database, "locations", "laboratory", "base.json");
        Skip.IfNot(File.Exists(path), "Installed Labs database fixture is unavailable.");
        var location = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), JsonOptions)!;

        var playerPoints = location.SpawnPointParams!
            .Where(point => point.Position.HasValue && point.Categories?.Contains("Player") == true)
            .ToArray();
        var safeBotPoints = location.SpawnPointParams!
            .Where(point => point.Position.HasValue
                            && point.Categories?.Contains("Bot") == true
                            && AllowedZones.Contains(point.BotZoneName, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(playerPoints);
        Assert.NotEmpty(safeBotPoints);
        foreach (var playerPoint in playerPoints)
        {
            var player = playerPoint.Position!.Value;
            var capacities = safeBotPoints
                .Where(point => DistanceSquared(player, point.Position!.Value) >= 25f * 25f)
                .GroupBy(point => point.BotZoneName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .Select(point => point.Position!.Value)
                    .Distinct()
                    .Count())
                .ToArray();

            Assert.True(capacities.Any(capacity => capacity >= 5),
                $"Labs player marker {playerPoint.Id} has no allowed bot zone with five points at least 25 m away.");
        }
    }

    private static float DistanceSquared(
        SPTarkov.Server.Core.Models.Eft.Common.Vector3 left,
        SPTarkov.Server.Core.Models.Eft.Common.Vector3 right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        var z = left.Z - right.Z;
        return x * x + y * y + z * z;
    }

    private static void AssertNoGates(string? zones)
        => Assert.DoesNotContain((zones ?? string.Empty).Split(','),
            zone => zone.Equals("BotZoneGate1", StringComparison.OrdinalIgnoreCase)
                    || zone.Equals("BotZoneGate2", StringComparison.OrdinalIgnoreCase));

    private static void WithAbps(bool present, Action action)
    {
        var field = typeof(ArenaLocationMutator).GetField("_abpsPresent", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldValue = field.GetValue(null);
        try
        {
            field.SetValue(null, present);
            action();
        }
        finally
        {
            field.SetValue(null, oldValue);
        }
    }
}
