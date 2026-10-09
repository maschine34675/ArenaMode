using ArenaMode.Shared;
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
    /// Tactical and Team Deathmatch own their entire bot population. Tactical
    /// counts one round's squad; Team Deathmatch reserves equal faction slots.
    /// An ambient pump or scheduled wave would bypass either mode's roster.
    /// The mode is announced at raid start and remembered by the exact session.
    /// </summary>
    public static bool IsQuietMap(string? mode)
        => ArenaPopulationPolicy.OwnsEntirePopulation(mode);

    public static void Mutate(LocationBase location, ArenaMapConfig map, ArenaModeConfig config,
        string? mode = null, string? areaId = null, int? areaTeamSize = null, int? participants = null)
    {
        if (!ArenaParticipantPolicy.TryResolve(location.Id!, mode!,
                ArenaAreaCatalog.NormalizeId(areaId) != ArenaAreaCatalog.FullMapId, false,
                participants, out _, out var participantFailure))
            throw new InvalidOperationException(participantFailure);
        // Validate the complete retained set before changing any raid data.
        // A missing area preserves the legacy path, including its original list.
        ApplyAreaSpawns(location, mode, areaId, areaTeamSize);

        // This object is already the exact Arena raid's clone. Mirror the
        // client menu projection without globally enabling Labyrinth, touching
        // entry keys or importing EnableLabyrinth's optional Scav/exfil changes.
        if (string.Equals(location.Id, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase))
        {
            location.Enabled = true;
            location.ForceOnlineRaidInPVE = false;

            // Way Up has two independent timers: Min/MaxTime determine when
            // TimerRequirement becomes available; ExfiltrationTime is the
            // extraction countdown. Keep availability immediate, but use a
            // ten-second countdown instead of extracting on doorway entry.
            // Keep its native type/requirements and all
            // other exits, including EnableLabyrinth's optional Scav exit.
            // Copy the matching record so aliases never receive Arena timers.
            if (location.Exits != null)
                location.Exits = location.Exits.Select(exit => exit?.Name == "labir_exit"
                    ? exit with
                    {
                        MinTime = 0,
                        MaxTime = 0,
                        MinTimePVE = 0,
                        MaxTimePVE = 0,
                        ExfiltrationTime = 10,
                        ExfiltrationTimePVE = 10,
                    }
                    : exit!).ToList();
        }

        var raidSeconds = Math.Max(60, config.RaidTimeMinutes * 60);
        var quiet = IsQuietMap(mode) || participants.HasValue;
        var spawnZones = ArenaBotSpawnPolicy.FilterZones(location.Id, map.SpawnZones);

        if (participants.HasValue)
        {
            // Remove unowned schedules before zone filtering, which edits each
            // retained schedule record. Even an aliased source must stay intact.
            location.Waves = [];
            location.BossLocationSpawn = [];
        }

        ExcludeSealedLabsBotSpawns(location);

        // Raid timing: the clock must never pressure an arena session.
        location.EscapeTimeLimit = config.RaidTimeMinutes;
        location.EscapeTimeLimitPVE = config.RaidTimeMinutes;

        // Alive caps (BotMax feeds the maxCap route; the client-side cap patch is the
        // authority when ABPS is installed — its pump reads _maxCount, not the clone)
        // and spawn cadence. Note BotSpawnPeriodCheck is floored to 10-15s by both the
        // vanilla scheduler and ABPS's pump — the density beyond that comes from the
        // client burst spawner.
        location.BotMax = participants.HasValue ? participants.Value - 1 : map.BotMax;
        location.BotMaxPvE = participants.HasValue ? participants.Value - 1 : map.BotMax;
        location.MaxBotPerZone = participants.HasValue ? participants.Value - 1 : map.MaxBotPerZone;
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

            // An explicit total requires explicit zeroed pump limits even if a
            // custom map omitted the modifier. Do not mutate an aliased record.
            if (participants.HasValue)
                location.BotLocationModifier = (location.BotLocationModifier ?? new BotLocationModifier()) with { };
            if (location.BotLocationModifier != null)
            {
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer = 0;
                location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE = 0;
            }

            // No scheduled waves either — the mode owns every bot slot.
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
                location.Waves = GenerateAssaultWaves(map, raidSeconds, spawnZones, location.Id).ToList();
        }

        // Bosses are off in a quiet map for the same reason as the waves: a boss
        // arriving unbidden is a bot the round is not counting.
        // The client owns the optional Labyrinth boss slot too: selected raids
        // already removed every native boss/escort entry before zone filtering.
        if (!participants.HasValue)
            MutateBossSpawns(location, map, raidSeconds, quiet || map.DisableBosses, quiet, spawnZones);

        // Zombie-only: turn on the native infection event block and raise the
        // caps the native point-selection code reads, on top of the quiet
        // branch every OwnsEntirePopulation mode already took above. See
        // ZombieLocationSettings for why each field has the value it has.
        if (string.Equals(mode, ArenaPopulationPolicy.Zombie, StringComparison.OrdinalIgnoreCase))
            ZombieLocationSettings.Apply(location);

        if (config.StripLoot)
            location.Loot = [];

        if (participants.HasValue)
            ValidateParticipantPopulation(location, location.Id!, mode!, participants.Value);
    }

    /// <summary>Rechecked against the final response clone and again before client activation.</summary>
    internal static void ValidateParticipantPopulation(LocationBase location, string expectedMap, string mode, int participants)
    {
        if (!ArenaParticipantPolicy.TryResolve(expectedMap, mode, false, false, participants, out _, out var reason))
            throw new InvalidOperationException(reason);
        if (!string.Equals(location.Id, expectedMap, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The participant-managed Arena clone belongs to another map.");
        int bots = participants - 1;
        if (location.BotMax != bots || location.BotMaxPvE != bots || location.MaxBotPerZone != bots)
            throw new InvalidOperationException("The participant-managed Arena bot caps changed after negotiation.");
        if (location.Waves == null || location.Waves.Any()
            || location.BossLocationSpawn == null || location.BossLocationSpawn.Any())
            throw new InvalidOperationException("The participant-managed Arena clone contains native bot or boss waves.");
        if (location.NewSpawn != false || location.OfflineNewSpawn != false
            || location.BotSpawnCountStep != 0 || location.BotStart is not > 0 || location.BotStop != 0
            || location.BotSpawnTimeOnMin != 0 || location.BotSpawnTimeOnMax != 0
            || !(location.BotSpawnTimeOffMin >= location.BotStart) || !(location.BotSpawnTimeOffMax >= location.BotStart))
            throw new InvalidOperationException("The participant-managed Arena continuous spawn window is not closed.");
        if (location.BotLocationModifier == null
            || location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayer != 0
            || location.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE != 0)
            throw new InvalidOperationException("The participant-managed Arena non-wave spawn limits changed.");
    }

    private static void ApplyAreaSpawns(LocationBase location, string? mode, string? areaId, int? areaTeamSize)
    {
        var allowed = GetAreaSpawns(location, mode, areaId, areaTeamSize, requireAlreadyContained: false);
        if (allowed != null)
            location.SpawnPointParams = allowed;
    }

    /// <summary>Read-only final check after all native/other-mod location work has finished.</summary>
    internal static void ValidateAreaSpawns(LocationBase location, string? mode, string? areaId, int? areaTeamSize = null)
        => GetAreaSpawns(location, mode, areaId, areaTeamSize, requireAlreadyContained: true);

    private static List<SpawnPointParam>? GetAreaSpawns(
        LocationBase location, string? mode, string? areaId, int? areaTeamSize, bool requireAlreadyContained)
    {
        if (!ArenaAreaCatalog.TryResolve(areaId!, location.Id, mode!, false,
                out var area, out var failure)
            || !ArenaAreaCatalog.TryResolveTeamSize(area, mode, false, areaTeamSize, out int selectedSize, out failure))
            throw new InvalidOperationException(failure);
        if (area == null)
            return null;

        // Retain every category only within the area, including Player, Coop,
        // Group and unknown mod categories. Never alter the marker objects or
        // their flags: later ordinary raids may share those records upstream.
        var source = (location.SpawnPointParams ?? []).ToList();
        if (area.Id == ArenaAreaCatalog.CustomsYardId
            && source.Any(point => point != null && ArenaAreaCatalog.CustomsYardSpawns.Any(s => s.Id == point.Id)
                && (point.Position is not { } p || !area.AllowsSpawn(p.X, p.Y, p.Z, point.Id))))
            throw new InvalidOperationException("Customs Yard extra identity collides with an invalid marker.");
        var allowed = source.Where(point =>
            point != null && !string.IsNullOrWhiteSpace(point.Id)
            && point.Position is { } p && area.AllowsSpawn(p.X, p.Y, p.Z, point.Id)).ToList();
        if (requireAlreadyContained && allowed.Count != source.Count)
            throw new InvalidOperationException($"Arena area '{area.Id}' received an outside or invalid spawn after filtering.");
        if (allowed.Select(point => point.Id).Distinct(StringComparer.Ordinal).Count() != allowed.Count)
            throw new InvalidOperationException($"Arena area '{area.Id}' contains duplicate spawn identities.");

        if (area.Id == ArenaAreaCatalog.CustomsYardId)
            AddOrValidateYardStarts(area, allowed, requireAlreadyContained);

        if (area.TeamStarts != null)
        {
            ValidateTeamStarts(area, selectedSize == 0 ? area.MaxTeamSize : selectedSize, allowed);
            return allowed;
        }

        int PlayerCount(string side) => allowed.Where(point =>
                HasCategory(point, "Player")
                && point.Sides?.Any(value => string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Pmc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, side, StringComparison.OrdinalIgnoreCase)) == true)
            .Select(point => point.Position!.Value).Distinct().Count();
        int botCount = allowed.Where(point =>
                (HasCategory(point, "Bot") || HasCategory(point, "BotPmc") || HasCategory(point, "Boss"))
                && !ArenaBotSpawnPolicy.IsExcludedZone(location.Id, point.BotZoneName)
                && !ArenaBotSpawnPolicy.IsExcludedPoint(location.Id, point.Id))
            .Select(point => point.Position!.Value).Distinct().Count();

        // Two independent entry/respawn choices per PMC side and one marker per
        // AI participant are a static minimum, not proof of live navigation.
        // The client additionally validates distance, carving and connectivity.
        int usecCount = PlayerCount("Usec");
        int bearCount = PlayerCount("Bear");
        int requiredBots = area.MaxTeamSize * 2 - 1;
        if (usecCount < 2 || bearCount < 2 || botCount < requiredBots)
            throw new InvalidOperationException(
                $"Arena area '{area.Id}' has insufficient distinct spawn positions "
                + $"(USEC {usecCount}/2, BEAR {bearCount}/2, bots {botCount}/{requiredBots}).");

        return allowed;
    }

    private static void AddOrValidateYardStarts(ArenaAreaDefinition area, List<SpawnPointParam> points, bool validateOnly)
    {
        // New IDs make EFT create proper zone-owned markers; FixParams on an
        // existing scene PMC marker does NOT update its BotZone. Never edit a
        // native marker (or any nested collider data that may alias upstream).
        var additions = new List<SpawnPointParam>();
        foreach (var spec in ArenaAreaCatalog.CustomsYardSpawns)
        {
            var anchor = points.SingleOrDefault(p => p.Id == spec.SourceId);
            var source = spec.SourcePosition;
            if (anchor?.Position is not { } position
                || Math.Abs(position.X - source.X) > .01f || Math.Abs(position.Y - source.Y) > .01f
                || Math.Abs(position.Z - source.Z) > .01f || anchor.ColliderParams == null
                || anchor.CorePointId != 3 || anchor.BotZoneName != ArenaAreaSpawnAddition.Zone
                || !HasKnownCategories(anchor) || !(HasNativeCategory(anchor, "Bot") || HasNativeCategory(anchor, "BotPmc")))
                throw new InvalidOperationException("Customs Yard start anchor changed or disappeared: " + spec.SourceId);
            if (!area.AllowsSpawn(spec.Position.X, spec.Position.Y, spec.Position.Z, spec.Id))
                throw new InvalidOperationException("Customs Yard extra start is outside its contour: " + spec.Id);

            var expected = System.Text.Json.JsonSerializer.Deserialize<SpawnPointParam>(
                System.Text.Json.JsonSerializer.Serialize(anchor))! with
            {
                Id = spec.Id,
                Position = new Vector3(spec.Position.X, spec.Position.Y, spec.Position.Z),
                Categories = [spec.Player ? "Player" : "BotPmc"],
                Sides = ["Pmc"],
                BotZoneName = ArenaAreaSpawnAddition.Zone,
            };
            var existing = points.SingleOrDefault(p => p.Id == spec.Id);
            if (existing != null)
            {
                if (System.Text.Json.JsonSerializer.Serialize(existing) != System.Text.Json.JsonSerializer.Serialize(expected))
                    throw new InvalidOperationException("Customs Yard extra start changed: " + spec.Id);
            }
            else if (validateOnly)
                throw new InvalidOperationException("Customs Yard extra start missing: " + spec.Id);
            else additions.Add(expected);
        }
        points.AddRange(additions);
    }

    private static void ValidateTeamStarts(ArenaAreaDefinition area, int selectedSize, List<SpawnPointParam> allowed)
    {
        // Keep the full contour's native records for later movement/respawns.
        // Only initial placement uses the versioned exact west/east identities.
        // Either faction may be the local player: each side therefore needs a
        // full enemy team, not merely (size - 1) allied bots.
        var playerStarts = new List<SpawnPointParam>();
        var botStarts = new List<SpawnPointParam>();
        var sectors = new List<SpawnPointParam[]>();
        foreach (bool usec in new[] { true, false })
        {
            string side = usec ? "Usec" : "Bear";
            var eligible = allowed.Where(point =>
                !ArenaBotSpawnPolicy.IsExcludedPoint(area.MapId, point.Id)
                && !ArenaBotSpawnPolicy.IsExcludedZone(area.MapId, point.BotZoneName)
                && HasKnownCategories(point)).ToArray();
            var players = eligible.Where(point => area.TeamStarts!.Allows(usec, true, point.Id)
                && HasNativeCategory(point, "Player") && HasPmcSide(point, side)).ToArray();
            var bots = eligible.Where(point => area.TeamStarts!.Allows(usec, false, point.Id)
                && !string.IsNullOrWhiteSpace(point.BotZoneName)
                && (HasNativeCategory(point, "Bot") || HasNativeCategory(point, "BotPmc"))).ToArray();
            if (players.Length < 1 || bots.Length < selectedSize)
                throw new InvalidOperationException($"Arena area '{area.Id}' team starts have insufficient {side} capacity "
                    + $"(PMC {players.Length}/1, bots {bots.Length}/{selectedSize}).");
            playerStarts.AddRange(players);
            botStarts.AddRange(bots);
            sectors.Add(players.Concat(bots).ToArray());
        }

        var all = playerStarts.Concat(botStarts).ToArray();
        if (all.Select(point => point.Position!.Value).Distinct().Count() != all.Length)
            throw new InvalidOperationException($"Arena area '{area.Id}' team starts repeat a physical position.");
        // Alternative PMC markers do not coexist. Check every bot pair and
        // every possible local PMC against every bot, including the other side.
        for (int i = 0; i < botStarts.Count; i++)
            foreach (var other in botStarts.Skip(i + 1).Concat(playerStarts))
            {
                var a = botStarts[i].Position!.Value; var b = other.Position!.Value;
                double dx = (double)a.X - b.X, dz = (double)a.Z - b.Z;
                if (dx * dx + dz * dz < 1.5 * 1.5)
                    throw new InvalidOperationException($"Arena area '{area.Id}' team starts are less than 1.5m apart horizontally.");
            }
        foreach (var west in sectors[0])
            foreach (var east in sectors[1])
            {
                var a = west.Position!.Value; var b = east.Position!.Value;
                double dx = (double)a.X - b.X, dz = (double)a.Z - b.Z;
                if (dx * dx + dz * dz < 25 * 25)
                    throw new InvalidOperationException($"Arena area '{area.Id}' opposing start sectors are less than 25m apart horizontally.");
            }
    }

    private static bool HasNativeCategory(SpawnPointParam point, string category)
        => HasCategory(point, category) || HasCategory(point, "All");

    private static bool HasKnownCategories(SpawnPointParam point)
        => point.Categories?.Any() == true && point.Categories.All(value =>
            new[] { "None", "Player", "Bot", "Boss", "Coop", "Group", "Opposite", "BotPmc", "All" }
                .Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool HasPmcSide(SpawnPointParam point, string side)
        => point.Sides?.Any() == true
           && point.Sides.All(value => new[] { "None", "Usec", "Bear", "Savage", "Pmc", "All" }
               .Contains(value, StringComparer.OrdinalIgnoreCase))
           && point.Sides.Any(value => string.Equals(value, side, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "Pmc", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase));

    private static bool HasCategory(SpawnPointParam point, string category)
        => point.Categories?.Any(value => string.Equals(value, category, StringComparison.OrdinalIgnoreCase)) == true;

    private static IEnumerable<Wave> GenerateAssaultWaves(ArenaMapConfig map, int raidSeconds,
        string spawnZones, string mapId)
    {
        // ONE zone per wave, round-robin. Wave.SpawnPoints is matched against a
        // zone name by exact string, so writing the whole comma-separated list
        // into it matches no zone at all and the wave is silently dropped. That
        // did not show on Factory or Labyrinth, where the list is a single name;
        // the Lab has several, and every generated wave there would have vanished.
        var zones = spawnZones
            .Split([','], StringSplitOptions.RemoveEmptyEntries)
            .Select(zone => zone.Trim())
            .Where(zone => zone.Length > 0)
            .ToArray();

        if (zones.Length == 0)
        {
            // An old config may contain only the two excluded gate zones. Do not
            // turn the empty filtered result into an unrestricted random spawn.
            if (ArenaBotSpawnPolicy.IsRestrictedMap(mapId))
                yield break;
            zones = ["BotZone"];
        }

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
        bool disableBosses, bool quietMap, string spawnZones)
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

        // REMOVED, not merely silenced. A zero chance already means the spawn
        // cannot happen — ShallSpawn is IsTrue100(BossChance) and both
        // ActivateWave and ActivateWavesAfterGameStarted return on it — so
        // dropping these entries changes no bot that would have existed. What
        // it does change is the raid-start bill: BotProfileClient's constructor
        // walks BossLocationSpawn and orders GetUsingTypes() from every entry
        // WITHOUT asking ShallSpawn, and the server then rounds each of those
        // role/difficulty pairs up to its presetBatch. Fourteen silenced boss
        // and follower pairs were costing a hundred and forty generated
        // profiles per raid for bots that could never appear.
        //
        // It also closes a door: ABPS prefixes BossSpawnScenario.Create and can
        // raise a zeroed chance back up when its progressive-boss option is on.
        // It is off in the installed config, so that is a hole rather than a
        // leak — but an entry that is gone cannot be re-chanced.
        // Written as "not greater than zero" rather than "<= 0" because the
        // model's chance is a double?, and a lifted `null <= 0` is FALSE: a
        // null-chance entry would have survived the filter and gone on costing
        // a full batch of profiles. No shipped location has one, but SPT's own
        // custom-wave service and any server mod can append one. Same family as
        // the MongoId and EBoundItem traps: a default that a comparison quietly
        // reads as "does not match".
        spawns.RemoveAll(spawn => spawn.BossChance is not > 0);

        if (!quietMap && map.PmcWaveIntervalSeconds > 0
            && (!ArenaBotSpawnPolicy.IsRestrictedMap(location.Id)
                || spawnZones.Length > 0))
        {
            var slots = Math.Min(10, Math.Max(0, (raidSeconds - 120) / map.PmcWaveIntervalSeconds));
            for (var i = 0; i < slots; i++)
            {
                var side = i % 2 == 0 ? "pmcUSEC" : "pmcBEAR";
                spawns.Add(new BossLocationSpawn
                {
                    BossName = side,
                    BossChance = 100,
                    BossZone = spawnZones,
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

    private static void ExcludeSealedLabsBotSpawns(LocationBase location)
    {
        if (!ArenaBotSpawnPolicy.IsRestrictedMap(location.Id))
            return;

        // The response is the per-raid clone. Filter complete bot-only markers,
        // never Player/Coop/Group (or an unknown/shared category). No marker fields
        // are changed, so player spawn and respawn consumers retain their data.
        if (location.SpawnPointParams != null)
            location.SpawnPointParams = location.SpawnPointParams.Where(point =>
                !ArenaBotSpawnPolicy.IsExcludedZone(location.Id, point.BotZoneName)
                || !IsBotOnlySpawn(point)).ToList();

        // ABPS can retain the existing wave plan instead of our generated one.
        // Filter that plan too, without changing the normal-map wave behavior.
        if (location.Waves != null)
            location.Waves = location.Waves.Where(wave =>
            {
                var filtered = ArenaBotSpawnPolicy.FilterZones(location.Id, wave.SpawnPoints);
                if (filtered.Length == 0 && !string.IsNullOrWhiteSpace(wave.SpawnPoints))
                    return false;
                wave.SpawnPoints = filtered;
                return true;
            }).ToList();

        // This includes vanilla extract-triggered raiders and entries added by
        // other server mods. An entry naming only gates must be removed: a blank
        // BossZone tells EFT to pick a random marked zone, reopening the gates.
        if (location.BossLocationSpawn != null)
            location.BossLocationSpawn = location.BossLocationSpawn.Where(spawn =>
            {
                var filtered = ArenaBotSpawnPolicy.FilterZones(location.Id, spawn.BossZone);
                if (filtered.Length == 0 && !string.IsNullOrWhiteSpace(spawn.BossZone))
                    return false;
                spawn.BossZone = filtered;
                return true;
            }).ToList();
    }

    private static bool IsBotOnlySpawn(SpawnPointParam point)
    {
        var categories = point.Categories?.ToArray();
        return categories is { Length: > 0 } && categories.All(category =>
            string.Equals(category, "Bot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Boss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "BotPmc", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPmc(string? bossName)
        => string.Equals(bossName, "pmcUSEC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(bossName, "pmcBEAR", StringComparison.OrdinalIgnoreCase);
}
