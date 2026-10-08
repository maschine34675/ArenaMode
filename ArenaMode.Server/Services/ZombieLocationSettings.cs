using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Server.Services;

/// <summary>
/// The native infection event block Zombie mode writes onto the per-raid
/// clone (Location.Events.Halloween2024 — JsonType\LocationSettings.cs:96-130
/// in the decompile) plus the two bot caps it also needs raised.
///
/// This is additive, not a replacement: EFT's own infected brain
/// (StandartBotBrain.cs:185-188 -> InfectedLayersStrategy) and the pursuit/call
/// system (BotHalloweenWithZombies.cs and friends) read these fields on the
/// bots Arena's own director spawns exactly the same as on any bot the native
/// crowd spawner would have produced — a "zombie" is only as infected as this
/// block says it is, whoever put it in the world. Every field below cites the
/// decompiled line(s) that make it matter, and the values are the ones
/// decided for the zombie prototype (orchestrator decision 2), not a generic
/// default — so this type is read once at the end of a quiet-branch Mutate
/// and never shared with any other mode's clone.
///
/// Split into its own pure, static-field type rather than inlined in
/// ArenaLocationMutator so the client stage (which reaches the same native
/// constants when it repairs BotsEventsController's null spawner, decision 3)
/// and this review can both read the reasoning in one place.
/// </summary>
public static class ZombieLocationSettings
{
    // LocalGame.vmethod_1 (EFT\LocalGame.cs:161-200) only preloads
    // BotHalloweenWithZombies.GetProfilesOnStart() when
    // Location.Events.Halloween2024.InfectionPercentage > 0. A zombie raid
    // must always look "infected" to that check even though the client
    // replaces the resulting list with its own small opening wave (zombie
    // client stage, decision 3c) rather than letting the native 498-profile
    // list through.
    public const double InfectionPercentage = 100;
    public const double MinInfectionPercentage = 100;

    // BotHalloweenCrowdSpawn.CalcBotsCanSpawnedPerCrowd returns 0 when this is
    // 0 (native facts), which is how the native crowd spawner is switched off
    // — Arena's own wave director (ZombieMode/ZombieWavePolicy, client stage)
    // is the only thing allowed to put a zombie into the world in v0.
    public const int MaxCrowdAttackSpawnLimit = 0;

    // CrowdsLimit/CrowdCooldownPerPlayerSec are belt-and-suspenders against
    // the same native crowd spawner: even if a future build changes what a
    // zero MaxCrowdAttackSpawnLimit does, a zero crowd limit and an
    // hour-long per-player cooldown leave it nothing to do.
    public const int CrowdsLimit = 0;
    public const double CrowdCooldownPerPlayerSec = 3600;

    // TeleportBotNearToTarget (InfectedLayersStrategy.cs:115-147) is a no-op
    // while this is 0 — which is the case with no Halloween config at all.
    // Normally moot for Arena's own zombies: ZombieNativeEventBridge's fifth
    // Harmony hook (client stage) suppresses this native ctor-time teleport
    // outright for every bot spawned during a live Zombie raid, because
    // decompile verification showed it is NOT the close-range "move toward
    // the player" behaviour the old comment here assumed — with no
    // established pursuit yet (true for every opening-wave spawn, and for the
    // whole raid whenever the native event never armed), GetNearestPursuitCenter
    // falls back to the bot's own just-assigned spawn position, which
    // collapses the teleport's direction vector to zero and makes it relocate
    // the bot to a semi-random cover point near ITS OWN spawn instead —
    // defeating BurstSpawnService.SpawnInfected's documented 25-70m ring, not
    // honouring it. 25 is kept anyway as the sane fallback for the one case
    // the client hook cannot cover — that Harmony patch failing to install on
    // a future game build (TryEnable fails open) — so the native teleport, if
    // it ever does run, still keeps roughly the ring's own spacing rather than
    // whatever the engine's own default would be.
    public const double MinSpawnDistToPlayer = 25;

    // InfectedTargetLayer.ShallUseNow (InfectedTargetLayer.cs:81-97) refuses
    // every positioned target unless this is large; 400 keeps the layer
    // usable across every Arena map in scope for v0 (Factory is tiny; Labs
    // and Labyrinth, allowed by default, are much larger).
    public const double TargetPointSearchRadiusLimit = 400;

    // BotHalloweenEventPursuit.CheckOnVisual (lines 93-116) grows the call
    // radius by ZombieCallDeltaRadius every ZombieCallPeriodSec up to
    // ZombieCallRadiusLimit, calling every CONNECTED zombie within that
    // radius toward the last seen position. 400/50/1 means a sighting reaches
    // every zombie on the map within eight seconds.
    public const double ZombieCallRadiusLimit = 400;
    public const double ZombieCallDeltaRadius = 50;
    public const double ZombieCallPeriodSec = 1;

    // EnemyInfo.cs:763/841-848 multiplies infected vision by this value; 0
    // would blind every zombie outright (native facts). A map's own tuned
    // value (Factory's base.json already ships 0.5) survives; an absent or
    // zero value is floored to the native baseline of 1.0 instead of ever
    // being written as 0 — this is the one field a map value can win on.
    public const double DefaultInfectedLookCoeff = 1.0;

    // BotHalloweenDistributions reads this list to weight which (role,
    // difficulty) pairs the now-disabled native crowd spawner would have
    // used. It cannot cause a spawn once MaxCrowdAttackSpawnLimit is 0, but a
    // map that ships none (or Factory's own pmc-only weights, which do not
    // name an infected role at all) still gets a sane non-empty default
    // instead of an empty list, in case any other reader of this block
    // (another server mod, a future SPT build) expects one.
    public const string DefaultSpawnParamRole = "infectedAssault";
    public const string DefaultSpawnParamDifficulty = "normal";
    public const int DefaultSpawnParamWeight = 1;

    // BotSpawner's native point-selection code reads MaxBotPerZone (native
    // facts) — below this the engine throttles valid spawn POSITIONS even for
    // a bot Arena's own director places outside the normal wave/boss path,
    // well before ZombieMaxAlive (Arena's own alive cap, client stage
    // decision 5) is ever reached. BotMax/BotMaxPvE are raised the same way
    // so the generic alive-cap route agrees with the per-zone one. Fixed at
    // 64 rather than derived from ZombieMaxAlive (the server never learns
    // that value — ArenaServerClient's raid-start payload carries no
    // Zombie-specific tuning): Settings.ZombieMaxAlive's own AcceptableValueRange
    // ceiling is kept at 56, not 60, specifically so the client-side cap it
    // feeds (ArenaBotCapPatch: ZombieMaxAlive + 2 + up to four PFT companions) stays
    // under this fixed floor instead of occasionally exceeding it.
    public const int MinimumBotCap = 64; // 56 infected + four protected companions + activation headroom.

    /// <summary>
    /// Mutates the clone in place. Fails closed by construction: every
    /// Halloween2024/LocationEvents property referenced here is a real,
    /// compiled member of the SPT server model (verified against
    /// server-csharp-main's LocationBase.cs), so a future SPT release that
    /// removes or renames one breaks the BUILD rather than silently skipping
    /// this mutation.
    /// </summary>
    public static void Apply(LocationBase location)
    {
        location.Events ??= new LocationEvents();
        var halloween = location.Events.Halloween2024 ?? new Halloween2024();

        halloween.InfectionPercentage = InfectionPercentage;
        halloween.MinInfectionPercentage = MinInfectionPercentage;
        halloween.MaxCrowdAttackSpawnLimit = MaxCrowdAttackSpawnLimit;
        halloween.CrowdsLimit = CrowdsLimit;
        halloween.CrowdCooldownPerPlayerSec = CrowdCooldownPerPlayerSec;
        halloween.MinSpawnDistToPlayer = MinSpawnDistToPlayer;
        halloween.TargetPointSearchRadiusLimit = TargetPointSearchRadiusLimit;
        halloween.ZombieCallRadiusLimit = ZombieCallRadiusLimit;
        halloween.ZombieCallDeltaRadius = ZombieCallDeltaRadius;
        halloween.ZombieCallPeriodSec = ZombieCallPeriodSec;

        // Never let a missing OR zeroed map value through: a null-coalesce
        // alone would still write back a literal 0 a map explicitly carried.
        halloween.InfectedLookCoeff = halloween.InfectedLookCoeff is > 0
            ? halloween.InfectedLookCoeff
            : DefaultInfectedLookCoeff;

        var existingParams = halloween.CrowdAttackSpawnParams?.ToList();
        halloween.CrowdAttackSpawnParams = existingParams is { Count: > 0 }
            ? existingParams
            : new List<CrowdAttackSpawnParam>
            {
                new()
                {
                    Role = DefaultSpawnParamRole,
                    Difficulty = DefaultSpawnParamDifficulty,
                    Weight = DefaultSpawnParamWeight,
                },
            };

        location.Events.Halloween2024 = halloween;

        location.BotMax = Math.Max(location.BotMax, MinimumBotCap);
        location.BotMaxPvE = Math.Max(location.BotMaxPvE ?? 0, MinimumBotCap);
        location.MaxBotPerZone = Math.Max(location.MaxBotPerZone ?? 0, MinimumBotCap);
    }
}
