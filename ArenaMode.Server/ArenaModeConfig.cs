using System.Text.Json.Serialization;

namespace ArenaMode.Server;

/// <summary>
/// Mod config, persisted as user/mods/ArenaMode/config/config.json.
/// Wave/bot fields are consumed from M3 on; kept in the schema now so the
/// file format stays stable.
/// </summary>
public class ArenaModeConfig
{
    [JsonPropertyName("debug")]
    public bool Debug { get; set; } = false;

    /// <summary>How long an armed arena flag survives without the raid actually starting.</summary>
    [JsonPropertyName("armedFlagTtlSeconds")]
    public int ArmedFlagTtlSeconds { get; set; } = 300;

    [JsonPropertyName("raidTimeMinutes")]
    public int RaidTimeMinutes { get; set; } = 60;

    /// <summary>"ffa" or "all_vs_player" (consumed from M3).</summary>
    [JsonPropertyName("hostilityMode")]
    public string HostilityMode { get; set; } = "ffa";

    [JsonPropertyName("stripLoot")]
    public bool StripLoot { get; set; } = true;

    [JsonPropertyName("maps")]
    public Dictionary<string, ArenaMapConfig> Maps { get; set; } = new()
    {
        ["factory4_day"] = new ArenaMapConfig(),
    };

    public bool IsMapEnabled(string? map)
        => map != null
           && Maps.TryGetValue(map.ToLowerInvariant(), out var mapConfig)
           && mapConfig.Enabled;
}

public class ArenaMapConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("botMax")]
    public int BotMax { get; set; } = 24;

    [JsonPropertyName("maxBotPerZone")]
    public int MaxBotPerZone { get; set; } = 15;

    [JsonPropertyName("botSpawnPeriodCheckSeconds")]
    public int BotSpawnPeriodCheckSeconds { get; set; } = 2;

    [JsonPropertyName("disableBosses")]
    public bool DisableBosses { get; set; } = true;

    /// <summary>Spawn zone(s) for generated waves (factory has exactly one).</summary>
    [JsonPropertyName("spawnZones")]
    public string SpawnZones { get; set; } = "BotZone";

    /// <summary>Fallback wave schedule when BotPlacementSystem is NOT installed.</summary>
    [JsonPropertyName("assaultWaveIntervalSeconds")]
    public int AssaultWaveIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("assaultSlotsMin")]
    public int AssaultSlotsMin { get; set; } = 3;

    [JsonPropertyName("assaultSlotsMax")]
    public int AssaultSlotsMax { get; set; } = 4;

    [JsonPropertyName("maxScheduledWaves")]
    public int MaxScheduledWaves { get; set; } = 60;

    /// <summary>PMC pressure via BossLocationSpawn appends (0 = none).</summary>
    [JsonPropertyName("pmcWaveIntervalSeconds")]
    public int PmcWaveIntervalSeconds { get; set; } = 240;

    /// <summary>Client burst top-up: keep at least this many bots alive.</summary>
    [JsonPropertyName("burstMinAliveBots")]
    public int BurstMinAliveBots { get; set; } = 12;

    [JsonPropertyName("burstCooldownSeconds")]
    public int BurstCooldownSeconds { get; set; } = 8;
}
