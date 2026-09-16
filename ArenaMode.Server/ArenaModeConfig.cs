using System.Text.Json.Serialization;
using ArenaMode.Shared;

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

    /// <summary>
    /// How many rungs a Gun Game ladder has. The weapon pool is drawn into this
    /// many equal power bands, one weapon taken at random from each, so a longer
    /// ladder is also a finer-grained climb.
    /// </summary>
    [JsonPropertyName("gunGameRungs")]
    public int GunGameRungs { get; set; } = 24;

    /// <summary>
    /// Template ids the Gun Game weapon pool and the attachment draw must never
    /// hand out. Seeded with the game's own placeholders — the eight templates
    /// whose display name says !!!DO_NOT_USE!!! (one of them !!!DO NOT USE!!!)
    /// — plus the unfinished items that carry no marker at all; both groups are
    /// refused by ArenaTemplateEligibility whatever this list says, so they are
    /// here to be visible and not to be switched off. The list itself is for
    /// the next one, or for a round that misbehaves for a reason no name
    /// reveals. The weapon pool reads it at server start; the per-raid
    /// cartridge draw and the attachment draw at every raid.
    /// </summary>
    [JsonPropertyName("excludedTemplates")]
    public List<string> ExcludedTemplates { get; set; } = new(Services.ArenaTemplateEligibility.SeededExclusions);


    [JsonPropertyName("maps")]
    public Dictionary<string, ArenaMapConfig> Maps { get; set; } = new()
    {
        [ArenaMapCatalog.FactoryDayId] = new ArenaMapConfig(),

        // EFT presents this as the PAST time choice on the Factory-day tile;
        // its own database entry is deliberately Enabled=false so it never gets
        // a duplicate tile. Arena's client filter opens only that native variant.
        [ArenaMapCatalog.FactoryNightId] = new ArenaMapConfig(),

        // Customs is admitted only with its versioned bounded independent TDM
        // preset; enabling this map never enables a whole-map Arena raid.
        // This is the native zone carried by the Factory Center bot markers,
        // not Factory's similarly named single "BotZone".
        [ArenaMapCatalog.CustomsId] = new ArenaMapConfig
        {
            SpawnZones = "ZoneFactoryCenter",
        },

        // Labyrinth needs a third-party mod to be selectable at all (it ships
        // disabled and keycard-gated, and those fields are read at the MENU,
        // before any per-raid clone exists). With that mod present it needs
        // nothing else from us: its scene carries a single BotZone named
        // "BotZone" — the same name factory uses — so the burst spawner works
        // there unchanged.
        //
        // Bosses stay ON here, unlike everywhere else. Shadow of Tagilla and
        // Killa ARE Labyrinth's content; switching them off would leave an
        // empty maze.
        [ArenaMapCatalog.LabyrinthId] = new ArenaMapConfig
        {
            DisableBosses = false,
            BurstMinAliveBots = 10,
        },

        // The Lab. Selectable only because an arena raid waives its keycard in
        // the menu (MapAccessPatch) — the location itself is enabled already.
        //
        // Keep the three accessible bot areas. Gate1/Gate2 are behind extraction
        // doors and strand arena bots until someone activates those extracts.
        // ArenaBotSpawnPolicy also excludes them from older saved configs and
        // the client's independent scene-zone selection. Player spawns stay as-is.
        [ArenaMapCatalog.LaboratoryId] = new ArenaMapConfig
        {
            SpawnZones = "BotZoneFloor1,BotZoneFloor2,BotZoneBasement",
            BotMax = 20,
            BurstMinAliveBots = 10,

            // The Lab is large and stacked over three levels, so the Factory
            // numbers leave bots patrolling a floor you are not on while you
            // spend a round looking for them. Every group is told, and told four
            // times as often. The cost argument behind the radius — more bots at
            // once in the state that switches on cover-finding and path-vision
            // raycasts — is weaker here, because the Lab runs a smaller
            // population than Factory does.
            HuntSweepSeconds = 5,
            HuntRadius = 0,
        },
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

    /// <summary>
    /// Let the bots' GEAR climb over the raid by raising the level range the
    /// progressive bot system derives its tiers from. Off: whatever tier the
    /// player's profile level happens to sit in, for the whole raid.
    /// </summary>
    [JsonPropertyName("scaleBotTier")]
    public bool ScaleBotTier { get; set; } = true;

    /// <summary>Bot level at the start of a raid — tier 1 territory.</summary>
    [JsonPropertyName("botTierStartLevel")]
    public int BotTierStartLevel { get; set; } = 1;

    /// <summary>
    /// Bot level once the ramp is finished. The band spreads five levels either
    /// side, so aim at the MIDDLE of the tier you want: 45 straddles tiers 4
    /// and 5, while 66 sits entirely inside tier 7, the top one. Values above
    /// 79 are clamped — the experience table ends there, and past it the band
    /// collapses back onto a single level.
    /// </summary>
    [JsonPropertyName("botTierEndLevel")]
    public int BotTierEndLevel { get; set; } = 66;

    [JsonPropertyName("botTierRampMinutes")]
    public int BotTierRampMinutes { get; set; } = 15;

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

    /// <summary>
    /// How often the client tells nearby bot groups where the player is.
    ///
    /// Sixty metres every fifteen seconds is a Factory number: there, that radius
    /// is most of the map and a bot that has not found you soon will. On a large,
    /// multi-storey map it is the difference between a fight and a search — which
    /// is why the Lab overrides both.
    /// </summary>
    [JsonPropertyName("huntSweepSeconds")]
    public int HuntSweepSeconds { get; set; } = 15;

    /// <summary>Only groups with a bot this close are told. 0 means every group, at any range.</summary>
    [JsonPropertyName("huntRadius")]
    public int HuntRadius { get; set; } = 60;

    /// <summary>Client burst top-up: keep at least this many bots alive.</summary>
    [JsonPropertyName("burstMinAliveBots")]
    public int BurstMinAliveBots { get; set; } = 12;

    [JsonPropertyName("burstCooldownSeconds")]
    public int BurstCooldownSeconds { get; set; } = 8;
}
