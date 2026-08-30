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

    /// <summary>
    /// Tactical wants an empty map between rounds, which is the opposite of what
    /// every other mode wants. It spawns its own squad and the round ends when
    /// that squad is dead — so a pump quietly feeding fresh bots in would mean
    /// the round never ends. The mode reaches here because the client announces
    /// it at raid start and the session remembers it.
    /// </summary>
    public static bool IsQuietMap(string? mode)
        => string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase);

    public static void Mutate(LocationBase location, ArenaMapConfig map, ArenaModeConfig config, string? mode = null)
    {
        var raidSeconds = Math.Max(60, config.RaidTimeMinutes * 60);
        var quiet = IsQuietMap(mode);

        // Raid timing: the clock must never pressure an arena session.
        location.EscapeTimeLimit = config.RaidTimeMinutes;
        location.EscapeTimeLimitPVE = config.RaidTimeMinutes;

        // Alive caps (BotMax feeds the maxCap route; the client-side cap patch is the
        // authority when ABPS is installed — its pump reads _maxCount, not the clone)
        // and spawn cadence. Note BotSpawnPeriodCheck is floored to 10-15s by both the
        // vanilla scheduler and ABPS's pump — the density beyond that comes from the
        // client burst spawner.
        location.BotMax = map.BotMax;
        location.BotMaxPvE = map.BotMax;
        location.MaxBotPerZone = map.MaxBotPerZone;
        location.BotSpawnPeriodCheck = map.BotSpawnPeriodCheckSeconds;
        if (quiet)
        {
            // A window that never opens. BotStart after BotStop leaves the pump
            // — vanilla's and ABPS's alike — with no interval to spawn in, and
            // clearing the flags stops the continuous spawner outright. Both,
            // because either one alone is a single point of failure for a mode
            // whose rounds cannot end while bots keep arriving.
            location.BotStart = raidSeconds;
            location.BotStop = 0;
            location.BotSpawnTimeOnMin = 0;
            location.BotSpawnTimeOnMax = 0;
            location.BotSpawnTimeOffMin = raidSeconds;
            location.BotSpawnTimeOffMax = raidSeconds;
            location.BotSpawnCountStep = 0;
            location.NewSpawn = false;
            location.OfflineNewSpawn = false;

            if (location.BotLocationModifier != null)
            {
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer = 0;
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE = 0;
            }

            // No scheduled waves either — the squad is the round.
            location.Waves = [];
        }
        else
        {
            location.BotStart = 0;
            location.BotStop = raidSeconds;
            location.BotSpawnTimeOnMin = raidSeconds;
            location.BotSpawnTimeOnMax = raidSeconds;
            location.BotSpawnTimeOffMin = 0;
            location.BotSpawnTimeOffMax = 0;
            location.BotSpawnCountStep = 1;

            // The continuous (non-waves) spawner only RUNS when these flags are set —
            // ABPS's server turns them off when its scav waves are disabled in config,
            // which would leave the arena with no scav spawn source at all.
            location.NewSpawn = true;
            location.OfflineNewSpawn = true;

            if (location.BotLocationModifier != null)
            {
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer = 999;
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE = 999;
            }

            if (!AbpsPresent)
                location.Waves = GenerateAssaultWaves(map, raidSeconds).ToList();
        }

        // Bosses are off in a quiet map for the same reason as the waves: a boss
        // arriving unbidden is a bot the round is not counting.
        MutateBossSpawns(location, map, raidSeconds, quiet || map.DisableBosses, quiet);

        if (config.StripLoot)
            location.Loot = [];
    }

    private static IEnumerable<Wave> GenerateAssaultWaves(ArenaMapConfig map, int raidSeconds)
    {
        // ONE zone per wave, round-robin. Wave.SpawnPoints is matched against a
        // zone name by exact string, so writing the whole comma-separated list
        // into it matches no zone at all and the wave is silently dropped. That
        // did not show on Factory or Labyrinth, where the list is a single name;
        // the Lab has five, and every generated wave there would have vanished.
        var zones = (map.SpawnZones ?? string.Empty)
            .Split([','], StringSplitOptions.RemoveEmptyEntries)
            .Select(zone => zone.Trim())
            .Where(zone => zone.Length > 0)
            .ToArray();

        if (zones.Length == 0)
            zones = ["BotZone"];

        // Wave 0: instant opening pressure.
        yield return MakeAssaultWave(0, -1, -1, 4, map, zones[0]);

        var interval = Math.Max(10, map.AssaultWaveIntervalSeconds);
        var count = Math.Min(map.MaxScheduledWaves, raidSeconds / interval);
        for (var i = 0; i < count; i++)
        {
            var time = 15 + i * interval;
            yield return MakeAssaultWave(i + 1, time, time + 15, map.AssaultSlotsMax, map, zones[(i + 1) % zones.Length]);
        }
    }

    private static Wave MakeAssaultWave(int number, int timeMin, int timeMax, int slotsMax, ArenaMapConfig map,
        string zone)
    {
        return new Wave
        {
            BotPreset = "normal",
            BotSide = "Savage",
            WildSpawnType = WildSpawnType.assault,
            SpawnPoints = zone,
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

    private static void MutateBossSpawns(LocationBase location, ArenaMapConfig map, int raidSeconds,
        bool disableBosses, bool quietMap)
    {
        var spawns = location.BossLocationSpawn?.ToList() ?? [];

        foreach (var spawn in spawns)
        {
            // In a quiet map even the PMC entries are silenced: the round counts
            // the squad it spawned, and anything arriving on its own is a bot the
            // count does not know about.
            if (disableBosses && quietMap)
            {
                spawn.BossChance = 0;
                continue;
            }

            if (IsPmc(spawn.BossName))
            {
                spawn.BossChance = 100;
                spawn.BossEscortAmount = "1";
            }
            else if (disableBosses)
            {
                spawn.BossChance = 0;
            }
        }

        if (!quietMap && map.PmcWaveIntervalSeconds > 0)
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
