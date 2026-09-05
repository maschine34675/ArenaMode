using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json.Converters;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ArenaMode.Tests;

/// <summary>
/// What the server does to a raid's location clone.
///
/// This is the one place where a mistake is invisible in the log and obvious in
/// the raid: a wave whose zone name does not match anything is dropped silently
/// by the engine, and the player simply finds an empty map. That shipped once —
/// the whole comma-separated zone list was written into a field the engine
/// compares by exact name, so every generated wave on the Lab was discarded.
/// </summary>
[Collection("Labs spawn strategies")]
public class LocationMutatorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(), new StringToMongoIdConverter() },
    };

    private static LocationBase FreshLocation() => new()
    {
        Id = "factory4_day",
        Waves = [],
        BossLocationSpawn = [],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TeamDeathmatchSuppressesEveryAmbientSpawnTrack(bool abps)
    {
        var location = FreshLocation();
        location.Waves = [new Wave { SpawnPoints = "BotZone", SlotsMin = 1, SlotsMax = 5 }];
        location.BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100 },
            new BossLocationSpawn { BossName = "pmcBEAR", BossChance = 100 },
            new BossLocationSpawn { BossName = "bossTagilla", BossChance = 100 },
        ];
        location.BotLocationModifier = new BotLocationModifier
        {
            NonWaveSpawnBotsLimitPerPlayer = 20,
            NonWaveSpawnBotsLimitPerPlayerPvE = 20,
        };
        location.NewSpawn = true;
        location.OfflineNewSpawn = true;
        var templateSnapshot = JsonSerializer.Serialize(location, JsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(templateSnapshot, JsonOptions)!;
        var config = new ArenaModeConfig { RaidTimeMinutes = 20 };
        var map = new ArenaMapConfig { DisableBosses = false, PmcWaveIntervalSeconds = 60 };
        WithAbps(abps, () => ArenaLocationMutator.Mutate(clone, map, config, "TEAMDEATHMATCH"));

        Assert.True(ArenaLocationMutator.IsQuietMap("teamdeathmatch"));
        Assert.Empty(clone.Waves!);
        Assert.Equal(3, clone.BossLocationSpawn!.Count());
        Assert.All(clone.BossLocationSpawn!, spawn => Assert.Equal(0, spawn.BossChance));
        Assert.Equal(1200, clone.BotStart);
        Assert.Equal(0, clone.BotStop);
        Assert.Equal(0, clone.BotSpawnCountStep);
        Assert.Equal(0, clone.BotSpawnTimeOnMin);
        Assert.Equal(0, clone.BotSpawnTimeOnMax);
        Assert.Equal(1200, clone.BotSpawnTimeOffMin);
        Assert.Equal(1200, clone.BotSpawnTimeOffMax);
        Assert.False(clone.NewSpawn);
        Assert.False(clone.OfflineNewSpawn);
        Assert.Equal(0, clone.BotLocationModifier!.NonWaveSpawnBotsLimitPerPlayer);
        Assert.Equal(0, clone.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE);
        Assert.Equal(templateSnapshot, JsonSerializer.Serialize(location, JsonOptions));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("endless", false)]
    [InlineData("rounds", false)]
    [InlineData("gungame", false)]
    [InlineData("tactical", true)]
    public void ExistingModesKeepTheirAmbientSpawnPolicy(string? mode, bool quiet)
    {
        var location = FreshLocation();
        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { PmcWaveIntervalSeconds = 60 }, new ArenaModeConfig(), mode));
        Assert.Equal(quiet, ArenaLocationMutator.IsQuietMap(mode));
        Assert.Equal(quiet, !location.Waves!.Any());
        Assert.Equal(quiet, !location.BossLocationSpawn!.Any());
        Assert.Equal(!quiet, location.NewSpawn);
        Assert.Equal(!quiet, location.OfflineNewSpawn);
    }

    [Fact]
    public void RaidTimeAndBotCapComeFromTheConfig()
    {
        var location = FreshLocation();
        var config = new ArenaModeConfig { RaidTimeMinutes = 45 };
        var map = new ArenaMapConfig { BotMax = 18 };

        ArenaLocationMutator.Mutate(location, map, config);

        Assert.Equal(45, location.EscapeTimeLimit);
        Assert.Equal(18, location.BotMax);
    }

    [Fact]
    public void EveryGeneratedWaveNamesExactlyOneRealZone()
    {
        var location = FreshLocation();
        var map = new ArenaMapConfig
        {
            SpawnZones = "BotZoneFloor1,BotZoneFloor2,BotZoneBasement,BotZoneGate1,BotZoneGate2",
        };

        ArenaLocationMutator.Mutate(location, map, new ArenaModeConfig());

        var zones = map.SpawnZones.Split(',').Select(zone => zone.Trim()).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(location.Waves!);

        foreach (var wave in location.Waves!)
        {
            var named = wave.SpawnPoints ?? string.Empty;

            Assert.False(named.Contains(','),
                $"wave {wave.Number} names '{named}' — the engine matches a zone by EXACT name, "
                + "so a list matches nothing and the wave is dropped");

            Assert.True(zones.Contains(named), $"wave {wave.Number} names '{named}', which is not one of the map's zones");
        }
    }

    [Fact]
    public void GeneratedWavesSpreadAcrossAllTheZones()
    {
        var location = FreshLocation();
        var map = new ArenaMapConfig { SpawnZones = "A,B,C" };

        ArenaLocationMutator.Mutate(location, map, new ArenaModeConfig { RaidTimeMinutes = 60 });

        var used = location.Waves!.Select(wave => wave.SpawnPoints).Distinct().ToList();
        Assert.Equal(3, used.Count);
    }

    [Fact]
    public void ASingleZoneMapStillGetsWaves()
    {
        var location = FreshLocation();
        var map = new ArenaMapConfig { SpawnZones = "BotZone" };

        ArenaLocationMutator.Mutate(location, map, new ArenaModeConfig());

        Assert.NotEmpty(location.Waves!);
        Assert.All(location.Waves!, wave => Assert.Equal("BotZone", wave.SpawnPoints));
    }

    [Fact]
    public void AnEmptyZoneSettingFallsBackRatherThanProducingNamelessWaves()
    {
        var location = FreshLocation();
        var map = new ArenaMapConfig { SpawnZones = string.Empty };

        ArenaLocationMutator.Mutate(location, map, new ArenaModeConfig());

        Assert.All(location.Waves!, wave => Assert.False(string.IsNullOrWhiteSpace(wave.SpawnPoints)));
    }

    [Fact]
    public void DisablingBossesLeavesNoBossSpawns()
    {
        var location = FreshLocation();
        location.BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossTagilla", BossChance = 100 },
        ];

        ArenaLocationMutator.Mutate(location, new ArenaMapConfig { DisableBosses = true }, new ArenaModeConfig());

        Assert.DoesNotContain(location.BossLocationSpawn!,
            spawn => spawn.BossName == "bossTagilla" && spawn.BossChance > 0);
    }

    [Fact]
    public void MapsThatKeepTheirBossesKeepThem()
    {
        var location = FreshLocation();
        location.BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 100 },
        ];

        ArenaLocationMutator.Mutate(location, new ArenaMapConfig { DisableBosses = false }, new ArenaModeConfig());

        Assert.Contains(location.BossLocationSpawn!,
            spawn => spawn.BossName == "bossKilla" && spawn.BossChance > 0);
    }

    [SkippableFact]
    public void InstalledFactoryNightCloneUsesItsRealBotZoneAndPreservesTheTemplate()
    {
        var database = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";
        var path = Path.Combine(database, "locations", ArenaMapCatalog.FactoryNightId, "base.json");
        Skip.IfNot(File.Exists(path), "Installed Factory-night database fixture is unavailable.");

        var original = JsonSerializer.Deserialize<LocationBase>(File.ReadAllText(path), JsonOptions)!;
        var originalSnapshot = JsonSerializer.Serialize(original, JsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(originalSnapshot, JsonOptions)!;
        var realBotZones = clone.SpawnPointParams!
            .Where(point => point.Categories?.Contains("Bot") == true)
            .Select(point => point.BotZoneName)
            .Where(zone => !string.IsNullOrWhiteSpace(zone))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("BotZone", realBotZones);
        var config = new ArenaModeConfig();
        WithAbps(false, () => ArenaLocationMutator.Mutate(
            clone, config.Maps[ArenaMapCatalog.FactoryNightId], config));

        Assert.Equal(ArenaMapCatalog.FactoryNightId, clone.Id);
        Assert.NotEmpty(clone.Waves!);
        Assert.All(clone.Waves!, wave =>
        {
            Assert.DoesNotContain(',', wave.SpawnPoints!);
            Assert.Contains(wave.SpawnPoints!, realBotZones);
        });
        Assert.Equal(originalSnapshot, JsonSerializer.Serialize(original, JsonOptions));
    }

    private static void WithAbps(bool present, Action action)
    {
        var field = typeof(ArenaLocationMutator).GetField(
            "_abpsPresent", BindingFlags.Static | BindingFlags.NonPublic)!;
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

public class ArenaModeConfigTests
{
    [Fact]
    public void MapMatchingIsCaseInsensitiveAndRejectsUnknownMaps()
    {
        var config = new ArenaModeConfig();

        Assert.True(config.IsMapEnabled("factory4_day"));
        Assert.True(config.IsMapEnabled("FACTORY4_DAY"));
        Assert.True(config.IsMapEnabled("factory4_night"));
        Assert.True(config.IsMapEnabled("FACTORY4_NIGHT"));
        Assert.False(config.IsMapEnabled("customs"));
        Assert.False(config.IsMapEnabled(null));
    }

    [Fact]
    public void ADisabledMapIsNotEnabled()
    {
        var config = new ArenaModeConfig();
        config.Maps["factory4_day"].Enabled = false;

        Assert.False(config.IsMapEnabled("factory4_day"));
    }

    [Fact]
    public void LegacyServerConfigGainsNightWithoutChangingSavedValues()
    {
        var saved = JsonNode.Parse("""
            {
              "maps": {
                "factory4_day": { "enabled": false },
                "labyrinth": { "enabled": true },
                "laboratory": { "enabled": true }
              }
            }
            """);
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig());

        Assert.True(ConfigStore.FillMissingKeys(saved, defaults));
        Assert.False(saved!["maps"]![ArenaMapCatalog.FactoryDayId]!["enabled"]!.GetValue<bool>());
        Assert.True(saved["maps"]![ArenaMapCatalog.FactoryNightId]!["enabled"]!.GetValue<bool>());
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
    }
}
