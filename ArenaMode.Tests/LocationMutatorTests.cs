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
    [InlineData(ArenaMapCatalog.EquatorId, "deathmatch")]
    [InlineData(ArenaMapCatalog.EquatorId, "gungame")]
    [InlineData(ArenaMapCatalog.Bay5Id, "deathmatch")]
    [InlineData(ArenaMapCatalog.Bay5Id, "gungame")]
    public void ImportedMapGeneratedWavesUseOnlyTheAuthoredBotZoneOnTheRaidClone(string mapId, string mode)
    {
        var original = FreshLocation();
        original.Id = mapId;
        original.DisabledForScav = true;
        var before = JsonSerializer.Serialize(original, JsonOptions);
        var clone = JsonSerializer.Deserialize<LocationBase>(before, JsonOptions)!;
        var config = new ArenaModeConfig();
        WithAbps(false, () => ArenaLocationMutator.Mutate(clone, config.Maps[mapId], config, mode));
        Assert.NotEmpty(clone.Waves!);
        Assert.All(clone.Waves!, wave => Assert.Equal("BotZone_Main", wave.SpawnPoints));
        Assert.NotEmpty(clone.BossLocationSpawn!);
        Assert.All(clone.BossLocationSpawn!, wave => Assert.Equal("BotZone_Main", wave.BossZone));
        Assert.Equal(8, clone.BotMax);
        Assert.True(clone.DisabledForScav);
        Assert.Equal(before, JsonSerializer.Serialize(original, JsonOptions));
    }

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
        // Removed, not merely silenced. A zero chance already cannot spawn, but
        // BotProfileClient's constructor orders profiles from every entry in
        // this list without ever asking ShallSpawn, and the server rounds each
        // role/difficulty pair up to bot.json's presetBatch — so three silenced
        // bosses were still worth a full batch of profiles each in the loading
        // screen.
        Assert.Empty(clone.BossLocationSpawn!);
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
    [InlineData(false)]
    [InlineData(true)]
    public void ZombieSuppressesEveryAmbientSpawnTrack(bool abps)
    {
        // Same quiet contract as Team Deathmatch (OwnsEntirePopulation covers
        // both) plus the native infection event, asserted separately below.
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
        WithAbps(abps, () => ArenaLocationMutator.Mutate(clone, map, config, "ZOMBIE"));

        Assert.True(ArenaLocationMutator.IsQuietMap("zombie"));
        Assert.Empty(clone.Waves!);
        Assert.Empty(clone.BossLocationSpawn!);
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
    [InlineData(null, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.5)]
    public void ZombieEnablesTheNativeInfectionEventOnTheClone(double? mapLookCoeff, double expectedLookCoeff)
    {
        var location = FreshLocation();
        if (mapLookCoeff.HasValue)
            location.Events = new LocationEvents { Halloween2024 = new Halloween2024 { InfectedLookCoeff = mapLookCoeff } };
        var beforeHadEventsBlock = location.Events != null;

        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { DisableBosses = true, PmcWaveIntervalSeconds = 0 },
            new ArenaModeConfig { RaidTimeMinutes = 20 }, "zombie"));

        // A template with no Events/Halloween2024 object at all (Factory's own
        // base.json has one — InfectedLookCoeff 0.5 — but a map could omit it)
        // gets one CREATED, never left null.
        var halloween = location.Events?.Halloween2024;
        Assert.NotNull(halloween);
        if (!beforeHadEventsBlock)
            Assert.NotNull(location.Events);

        Assert.Equal(100, halloween!.InfectionPercentage);
        Assert.Equal(100, halloween.MinInfectionPercentage);
        Assert.Equal(0, halloween.MaxCrowdAttackSpawnLimit);
        Assert.Equal(0, halloween.CrowdsLimit);
        Assert.Equal(3600, halloween.CrowdCooldownPerPlayerSec);
        Assert.Equal(25, halloween.MinSpawnDistToPlayer);
        Assert.Equal(400, halloween.TargetPointSearchRadiusLimit);
        Assert.Equal(400, halloween.ZombieCallRadiusLimit);
        Assert.Equal(50, halloween.ZombieCallDeltaRadius);
        Assert.Equal(1, halloween.ZombieCallPeriodSec);
        // Never written as 0 (native facts: 0 blinds every infected outright) —
        // a missing OR zeroed map value floors to the native baseline instead.
        Assert.Equal(expectedLookCoeff, halloween.InfectedLookCoeff);
        var param = Assert.Single(halloween.CrowdAttackSpawnParams!);
        Assert.Equal("infectedAssault", param.Role);
        Assert.Equal("normal", param.Difficulty);
        Assert.Equal(1, param.Weight);
    }

    [Fact]
    public void ZombieKeepsAMapsExistingCrowdAttackSpawnParamsAndLookCoeff()
    {
        var location = FreshLocation();
        location.Events = new LocationEvents
        {
            Halloween2024 = new Halloween2024
            {
                InfectedLookCoeff = 0.5,
                CrowdAttackSpawnParams = [new CrowdAttackSpawnParam { Role = "infectedPmc", Difficulty = "hard", Weight = 3 }],
            },
        };

        WithAbps(false, () => ArenaLocationMutator.Mutate(location, new ArenaMapConfig(), new ArenaModeConfig(), "zombie"));

        var halloween = location.Events!.Halloween2024!;
        Assert.Equal(0.5, halloween.InfectedLookCoeff);
        var param = Assert.Single(halloween.CrowdAttackSpawnParams!);
        Assert.Equal("infectedPmc", param.Role);
        Assert.Equal("hard", param.Difficulty);
        Assert.Equal(3, param.Weight);
    }

    [Theory]
    [InlineData(24, 15)]
    [InlineData(60, 60)]
    [InlineData(62, 63)]
    [InlineData(64, 64)]
    [InlineData(100, 120)]
    public void ZombieRaisesTheCloneBotCaps(int mapBotMax, int mapMaxBotPerZone)
    {
        // BotSpawner's native point-selection code reads MaxBotPerZone, so a
        // map whose configured caps are already generous must never be
        // LOWERED back down to the minimum — only raised when it falls short.
        var location = FreshLocation();
        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { BotMax = mapBotMax, MaxBotPerZone = mapMaxBotPerZone },
            new ArenaModeConfig(), "zombie"));

        // Maximum configured infected + four PFT companions + two release
        // reserve slots must fit even when the native map cap is much smaller.
        Assert.True(ZombieLocationSettings.MinimumBotCap >= 56 + 4 + 2);
        Assert.Equal(Math.Max(mapBotMax, 64), location.BotMax);
        Assert.Equal(Math.Max(mapBotMax, 64), location.BotMaxPvE);
        Assert.Equal(Math.Max(mapMaxBotPerZone, 64), location.MaxBotPerZone);
    }

    [Fact]
    public void ThePopulationPolicyAgreesWithTheModesItNames()
    {
        // The two ids decide whether the server silences a map's own population
        // and whether the client skips its profile preload. They are declared
        // once, in the shared policy, and ArenaModeCatalog now points at them —
        // this pins the meaning as well as the spelling.
        Assert.True(ArenaPopulationPolicy.OwnsEntirePopulation(ArenaPopulationPolicy.TeamDeathmatch));
        Assert.True(ArenaPopulationPolicy.OwnsEntirePopulation(ArenaPopulationPolicy.Tactical));
        Assert.True(ArenaPopulationPolicy.OwnsEntirePopulation(ArenaPopulationPolicy.Zombie));
        Assert.True(ArenaPopulationPolicy.OwnsEntirePopulation("TEAMDEATHMATCH"));
        Assert.True(ArenaPopulationPolicy.OwnsEntirePopulation("ZOMBIE"));
        foreach (var loud in new[] { "endless", "rounds", "gungame", "", null })
            Assert.False(ArenaPopulationPolicy.OwnsEntirePopulation(loud));

        // The preload skip is NARROWER than that: Team Deathmatch is the only
        // mode that also throws the pre-generated PMC pool away, so it is the
        // only one that loses nothing by never fetching it. Zombie also owns
        // its population but is deliberately excluded too — see the policy's
        // doc comment on SkipsProfilePreload for why its case differs from
        // Team Deathmatch's.
        Assert.True(ArenaPopulationPolicy.SkipsProfilePreload(true, "bigmap",
            ArenaPopulationPolicy.TeamDeathmatch, "BIGMAP"));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, "bigmap",
            ArenaPopulationPolicy.Tactical, "bigmap"));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, "bigmap",
            ArenaPopulationPolicy.Zombie, "bigmap"));

        // Everything else about the gate, one refusal at a time.
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(false, "bigmap", "teamdeathmatch", "bigmap"));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, "factory4_day", "teamdeathmatch", "bigmap"));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, null, "teamdeathmatch", "bigmap"));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, "bigmap", "teamdeathmatch", null));
        Assert.False(ArenaPopulationPolicy.SkipsProfilePreload(true, "bigmap", "endless", "bigmap"));
    }

    [Fact]
    public void TheNativeEventBridgeGateMatchesTheExactArmedZombieRaid()
    {
        // ZombieNativeEventBridge's four Harmony hooks (client-side, not
        // directly testable here — see AGENTS.md) all gate on this one pure
        // function. Confirm it fires for the exact armed Zombie raid...
        Assert.True(ArenaPopulationPolicy.IsArmedZombieLoading(true, "bigmap",
            ArenaPopulationPolicy.Zombie, "BIGMAP"));

        // ...and nothing else, including the other population-owning modes:
        // this gate is Zombie-specific, unlike SkipsProfilePreload's own
        // Team-Deathmatch-specific gate above.
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, "bigmap",
            ArenaPopulationPolicy.TeamDeathmatch, "bigmap"));
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, "bigmap",
            ArenaPopulationPolicy.Tactical, "bigmap"));

        // Everything else about the gate, one refusal at a time — same shape
        // as SkipsProfilePreload's own refusals above.
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(false, "bigmap", "zombie", "bigmap"));
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, "factory4_day", "zombie", "bigmap"));
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, null, "zombie", "bigmap"));
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, "bigmap", "zombie", null));
        Assert.False(ArenaPopulationPolicy.IsArmedZombieLoading(true, "bigmap", "endless", "bigmap"));
    }

    [Fact]
    public void ALoudModeDropsTheBossesItDisabledButKeepsItsPmcs()
    {
        var location = FreshLocation();
        location.BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 40 },
            new BossLocationSpawn { BossName = "bossTagilla", BossChance = 100 },
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 0 },
        ];

        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { DisableBosses = true, PmcWaveIntervalSeconds = 0 },
            new ArenaModeConfig { RaidTimeMinutes = 20 }, "endless"));

        // The PMC entry is the pressure this mode wants and is raised to
        // certainty; the two bosses were switched off and are now gone rather
        // than sitting at zero, which is what stops them being ordered as
        // profiles nobody can spawn.
        var spawns = location.BossLocationSpawn!.ToList();
        Assert.Single(spawns);
        Assert.Equal("pmcUSEC", spawns[0].BossName);
        Assert.Equal(100, spawns[0].BossChance);
    }

    [Fact]
    public void ALoudModeKeepsItsBossesWhenTheyAreNotDisabled()
    {
        // Labyrinth ships with DisableBosses false and its bosses are the point.
        // A filter that removed them would empty the fight the same way the
        // comma in SpawnPoints once emptied the Lab.
        var location = FreshLocation();
        location.BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossTagilla", BossChance = 100 },
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 35 },
        ];

        WithAbps(false, () => ArenaLocationMutator.Mutate(location,
            new ArenaMapConfig { DisableBosses = false, PmcWaveIntervalSeconds = 0 },
            new ArenaModeConfig { RaidTimeMinutes = 20 }, "rounds"));

        Assert.Equal(2, location.BossLocationSpawn!.Count());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("endless", false)]
    [InlineData("rounds", false)]
    [InlineData("gungame", false)]
    [InlineData("tactical", true)]
    [InlineData("zombie", true)]
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
        Assert.True(config.IsMapEnabled("bigmap"));
        Assert.True(config.IsMapEnabled("BIGMAP"));
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
        Assert.True(saved["maps"]![ArenaMapCatalog.CustomsId]!["enabled"]!.GetValue<bool>());
        Assert.Equal("ZoneFactoryCenter", saved["maps"]![ArenaMapCatalog.CustomsId]!["spawnZones"]!.GetValue<string>());
        Assert.False(ConfigStore.FillMissingKeys(saved, defaults));
    }
}
