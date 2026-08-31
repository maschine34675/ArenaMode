using ArenaMode.Server;
using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Eft.Common;

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
public class LocationMutatorTests
{
    private static LocationBase FreshLocation() => new()
    {
        Id = "factory4_day",
        Waves = [],
        BossLocationSpawn = [],
    };

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
}

public class ArenaModeConfigTests
{
    [Fact]
    public void MapMatchingIsCaseInsensitiveAndRejectsUnknownMaps()
    {
        var config = new ArenaModeConfig();

        Assert.True(config.IsMapEnabled("factory4_day"));
        Assert.True(config.IsMapEnabled("FACTORY4_DAY"));
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
}
