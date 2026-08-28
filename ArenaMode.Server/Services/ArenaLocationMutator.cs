using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace ArenaMode.Server.Services;

/// <summary>
/// Mutates the PER-RAID LocationBase clone for an arena raid — never the shared
/// database (that is the whole "never leaks into normal raids" guarantee).
///
/// Two-track spawn strategy:
/// - With BotPlacementSystem (ABPS) installed, its client-side continuous spawn pump
///   reads BotStart/BotStop/BotSpawnTime*/PeriodCheck/CountStep and the per-player
///   non-wave limit FROM THIS CLONE — setting those fields drives its pump without
///   touching ABPS config (which would leak into normal raids).
/// - Without ABPS, the vanilla wave scheduler runs, so a dense assault wave plan
///   is generated instead.
/// PMC pressure comes from BossLocationSpawn entries in both tracks.
/// </summary>
public static class ArenaLocationMutator
{
    private static bool? _abpsPresent;

    public static bool AbpsPresent
    {
        get
        {
            _abpsPresent ??= AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                (assembly.GetName().Name ?? string.Empty)
                .Contains("botplacementsystem", StringComparison.OrdinalIgnoreCase));
            return _abpsPresent.Value;
        }
    }

    public static void Mutate(LocationBase location, ArenaMapConfig map, ArenaModeConfig config)
    {
        var raidSeconds = Math.Max(60, config.RaidTimeMinutes * 60);

        // Raid timing: the clock must never pressure an arena session.
        location.EscapeTimeLimit = config.RaidTimeMinutes;
        location.EscapeTimeLimitPVE = config.RaidTimeMinutes;

        // Alive caps and spawn cadence (read by vanilla scheduler AND ABPS's pump).
        location.BotMax = map.BotMax;
        location.BotMaxPvE = map.BotMax;
        location.MaxBotPerZone = map.MaxBotPerZone;
        location.BotSpawnPeriodCheck = map.BotSpawnPeriodCheckSeconds;
        location.BotStart = 0;
        location.BotStop = raidSeconds;
        location.BotSpawnTimeOnMin = raidSeconds;
        location.BotSpawnTimeOnMax = raidSeconds;
        location.BotSpawnTimeOffMin = 0;
        location.BotSpawnTimeOffMax = 0;
        location.BotSpawnCountStep = 1;

        if (location.BotLocationModifier != null)
        {
            location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer = 999;
            location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE = 999;
        }

        if (!AbpsPresent)
            location.Waves = GenerateAssaultWaves(map, raidSeconds).ToList();

        MutateBossSpawns(location, map, raidSeconds);

        if (config.StripLoot)
            location.Loot = [];
    }

    private static IEnumerable<Wave> GenerateAssaultWaves(ArenaMapConfig map, int raidSeconds)
    {
        // Wave 0: instant opening pressure.
        yield return MakeAssaultWave(0, -1, -1, 4, map);

        var interval = Math.Max(10, map.AssaultWaveIntervalSeconds);
        var count = Math.Min(map.MaxScheduledWaves, raidSeconds / interval);
        for (var i = 0; i < count; i++)
        {
            var time = 15 + i * interval;
            yield return MakeAssaultWave(i + 1, time, time + 15, map.AssaultSlotsMax, map);
        }
    }

    private static Wave MakeAssaultWave(int number, int timeMin, int timeMax, int slotsMax, ArenaMapConfig map)
    {
        return new Wave
        {
            BotPreset = "normal",
            BotSide = "Savage",
            WildSpawnType = WildSpawnType.assault,
            SpawnPoints = map.SpawnZones,
            IsPlayers = false,
            KeepZoneOnSpawn = false,
            Number = number,
            SlotsMin = map.AssaultSlotsMin,
            SlotsMax = slotsMax,
            TimeMin = timeMin,
            TimeMax = timeMax,
            SptId = $"arena_assault_{number}",
            SpawnMode = ["regular", "pve"],
        };
    }

    private static void MutateBossSpawns(LocationBase location, ArenaMapConfig map, int raidSeconds)
    {
        var spawns = location.BossLocationSpawn?.ToList() ?? [];

        foreach (var spawn in spawns)
        {
            if (IsPmc(spawn.BossName))
            {
                spawn.BossChance = 100;
                spawn.BossEscortAmount = "1";
            }
            else if (map.DisableBosses)
            {
                spawn.BossChance = 0;
            }
        }

        if (map.PmcWaveIntervalSeconds > 0)
        {
            var slots = Math.Min(10, Math.Max(0, (raidSeconds - 120) / map.PmcWaveIntervalSeconds));
            for (var i = 0; i < slots; i++)
            {
                var side = i % 2 == 0 ? "pmcUSEC" : "pmcBEAR";
                spawns.Add(new BossLocationSpawn
                {
                    BossName = side,
                    BossChance = 100,
                    BossZone = map.SpawnZones,
                    BossDifficulty = "normal",
                    BossEscortType = side,
                    BossEscortAmount = "1",
                    BossEscortDifficulty = "normal",
                    Time = 120 + i * map.PmcWaveIntervalSeconds,
                    ForceSpawn = true,
                    IgnoreMaxBots = true,
                    IsRandomTimeSpawn = false,
                    TriggerId = string.Empty,
                    TriggerName = string.Empty,
                    SpawnMode = ["regular", "pve"],
                });
            }
        }

        location.BossLocationSpawn = spawns;
    }

    private static bool IsPmc(string? bossName)
        => string.Equals(bossName, "pmcUSEC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(bossName, "pmcBEAR", StringComparison.OrdinalIgnoreCase);
}
