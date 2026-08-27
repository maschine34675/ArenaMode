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
}
