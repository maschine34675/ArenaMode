using ArenaMode.Config;
using ArenaMode.Combat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Top-up burst spawner: whenever the alive population drops below the target,
/// inject bots directly on spawn points we choose ourselves — no wave, no boss
/// path, none of the engine's placement filters. The ABPS pump is the baseline
/// pressure; this closes the refill gap after a killing spree and holds the PMC
/// floor that makes the arena worth playing.
/// </summary>
internal static class BurstSpawnService
{
    private const float InFlightSeconds = 20f;
    private const int BotsPerBurst = 2;

    private static float _nextAttemptAt;
    private static bool _nextPmcIsBear;
    private static int _zoneCursor;
    private static int _pointCursor;

    /// <summary>
    /// Invalidates profile requests and activation callbacks independently of
    /// EFT's bot-spawn counters. Profile generation is asynchronous and may
    /// finish after GameEnd; a process-local generation keeps that continuation
    /// from ever dispatching into a later raid.
    /// </summary>
    private static int _operationGeneration;

    /// <summary>
    /// Our own in-flight ledger: injection timestamps, split by kind. The
    /// engine's counter for pending spawns can leak permanently, so we track what
    /// WE injected instead and let entries age out — a wrong entry costs at most
    /// one late burst. PMC and scav entries must stay apart: counting scav
    /// injections against the PMC floor would suppress the PMC top-ups that the
    /// floor exists to force.
    /// </summary>
    private static readonly List<float> RecentPmcInjections = new List<float>(8);

    private static readonly List<float> RecentScavInjections = new List<float>(8);

    /// <summary>Non-PMC roles alive, rebuilt per count — diagnostics for the burst log.</summary>
    /// <summary>How far a PMC shortfall may push the population past its target.</summary>
    private const int PmcOvershootAllowance = 2;

    private static readonly Dictionary<WildSpawnType, int> RoleTally = new Dictionary<WildSpawnType, int>();

    public static void Reset()
    {
        Interlocked.Increment(ref _operationGeneration);
        _nextAttemptAt = 0f;
        _zoneCursor = 0;
        _pointCursor = 0;
        RecentPmcInjections.Clear();
        RecentScavInjections.Clear();
    }

    /// <summary>
    /// Whether a mode has taken the population over, in which case this service
    /// stays out of the way. Tactical does: it tells the player how many enemies
    /// a round holds and ends the round when THOSE are dead, so an ambient
    /// top-up would put uncounted bots on the map — and, since ambient PMCs can
    /// wipe the squad themselves, could hand the player a round they never
    /// fought. The server silences the engine's own pumps for that mode; this
    /// silences ours.
    ///
    /// Derived from the running mode rather than latched: Reset() clears every
    /// flag in this class and runs from ArenaSession's raid-start handler, which
    /// fires in the same event as the one that starts the match. A latched flag
    /// would depend on those two subscriptions keeping their order.
    /// </summary>
    public static bool ManualControl =>
        Modes.ArenaMatchService.Current?.Mode?.ControlsOwnSpawns == true;

    public static void Tick()
    {
        if (ManualControl || !ArenaSession.IsActive || Time.time < _nextAttemptAt)
            return;

        // Re-check at most every 2s even when nothing needs spawning.
        _nextAttemptAt = Time.time + 2f;

        try
        {
            if (!Singleton<GameWorld>.Instantiated || !(Singleton<IBotGame>.Instance is { } botGame))
                return;

            var world = Singleton<GameWorld>.Instance;
            var map = ArenaSession.ArmedMap ?? world.LocationId;
            var target = ArenaClientConfig.GetBurstMinAliveBots(map);
            if (target <= 0)
                return;

            var controller = botGame.BotsController;
            var spawner = controller?.BotSpawner;
            if (spawner == null)
                return;

            // Decide on REAL alive bots, not on AliveLoadingDelayedBotsCount:
            // that counter sums loading + delayed + in-spawn terms which have no
            // in-raid recovery path, so a single leaked spawn pins it above the
            // target and silently starves every refill for the rest of the raid
            // (the "no bots after ~10 minutes" playtest). Overshoot is paced by
            // the post-burst cooldown instead. Both numbers are logged so a
            // drifting counter stays visible.
            if (!TryCountAliveBots(spawner, out var alive, out var alivePmc))
                return;

            // Bots we injected seconds ago are not registered yet (profile
            // generation is a server round trip) — without this the gap looks
            // unchanged for several ticks and we over-inject.
            // The ledger counts expected BOTS, not injections: a request can come
            // back with fewer profiles than asked for, and multiplying a pair
            // count would then invent bots that never arrive.
            var pendingPmc = CountPending(RecentPmcInjections);
            var pendingScav = CountPending(RecentScavInjections);
            var projected = alive + pendingPmc + pendingScav;

            // PMCs are the pressure that makes the arena fun; scavs only fill the
            // rest. Without a separate floor the single "alive" target is met by
            // whatever spawns fastest — which was scavs, every time.
            var pmcFloor = Mathf.Min(ArenaPressure.PmcFloor(Settings.PmcFloor.Value), target);
            var wantPmc = alivePmc + pendingPmc < pmcFloor;

            // The target bounds the POPULATION; the PMC floor only decides what
            // the next spawn should be. Skipping the bound whenever PMCs were
            // short worked while the floor sat safely below the target — but
            // pressure raises the floor, and once it CATCHES the target the
            // bound never fires again: a map asking for ten was carrying
            // fourteen, on a corridor map, with SAIN thinking for all of them.
            // A PMC deficit may still overshoot a little, so a scav-filled map
            // can be corrected, but it can no longer run away.
            var ceiling = wantPmc ? target + PmcOvershootAllowance : target;
            if (projected >= ceiling)
                return;

            var counterAlive = controller.AliveLoadingDelayedBotsCount;

            var zone = PickZone(spawner, BotsPerBurst);
            if (zone == null)
                return;

            var role = wantPmc
                ? (_nextPmcIsBear ? WildSpawnType.pmcBEAR : WildSpawnType.pmcUSEC)
                : WildSpawnType.assault;

            // Baselines are what this line used to hardcode; pressure only
            // ever raises them, so a raid never starts easier than it did.
            var difficulty = ArenaPressure.DifficultyFor(wantPmc);

            Plugin.ModLogger.LogInfo(
                $"Arena burst spawn: {alive} alive ({alivePmc} PMC) / target {target} (PMC floor {pmcFloor}), "
                + $"in flight {pendingPmc} PMC + {pendingScav} scav (spawner counter {counterAlive}) "
                + $"— requesting {role} ({difficulty}, pressure {ArenaPressure.Describe()}). "
                + $"Others alive: {DescribeRoles()}");

            if (wantPmc)
                _nextPmcIsBear = !_nextPmcIsBear;

            // The ledger and the outcome log live in SpawnPair, which is the only
            // place that knows how many bots were actually dispatched.
            SpawnPair(spawner, zone, role, difficulty, wantPmc);

            _nextAttemptAt = Time.time
                + ArenaPressure.BurstCooldown(ArenaClientConfig.GetBurstCooldownSeconds(map));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn failed: {ex.Message}");
            _nextAttemptAt = Time.time + 10f;
        }
    }

    /// <summary>
    /// Spawns a pair on spawn points WE pick, bypassing every placement filter.
    ///
    /// Two filters had to go. ActivateBotsByWave routes through
    /// BotBossSpawn.TrySpawn, which ABPS prefix-patches and owns for PMC roles:
    /// on factory it demands a point far from every living bot and from the
    /// player, finds none with a dozen bots around, reports success anyway and
    /// drops the wave. And letting the engine choose points (pointsToSpawn null)
    /// runs its own category and proximity filters, which fail for the same
    /// reason — that is why dozens of logged PMC injections produced 16 PMCs.
    /// Handing over explicit points skips both: the engine just adds them to the
    /// creation data and spawns.
    /// </summary>
    private static async void SpawnPair(BotSpawner spawner, BotZone zone, WildSpawnType role,
        BotDifficulty difficulty, bool isPmc, int count = BotsPerBurst, Action<BotOwner> observer = null,
        bool requireExactCount = false, Func<bool> requestStillCurrent = null)
    {
        var ledger = isPmc ? RecentPmcInjections : RecentScavInjections;
        var reserved = 0;
        var operationGeneration = Volatile.Read(ref _operationGeneration);

        if (!TryCaptureSpawnLease(
                spawner,
                operationGeneration,
                out LocalGame game,
                out GameWorld world,
                out BotsController controller,
                out int raidEpoch))
        {
            return;
        }

        try
        {
            var profileParams = new GetProfileDataParams(
                EPlayerSide.Savage, // SPT restores the real PMC side later — do not "fix" this
                role,
                difficulty,
                -1f,
                new BotSpawnParams());

            // Profile generation is best-effort: the request can come back with
            // fewer profiles than asked for, or with none at all.
            var data = await BotCreationData.Create(profileParams, spawner._botCreator, count, spawner);

            // GameEnd can run while profile generation is awaiting the server.
            // Stop the detached creation data before touching cursors, ledgers or
            // the spawner when any part of the exact raid lease changed.
            if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner))
            {
                data?.StopSpawn();
                Plugin.ModLogger.LogInfo(
                    $"Arena burst spawn: discarded stale {role} profiles after the raid lease ended.");
                return;
            }

            // A mode-local request can expire while BotCreationData.Create is
            // awaiting profiles or pools even though the raid itself remains
            // current. Stop before EFT receives bodies: cancelling after batch
            // dispatch can leak its per-point in-spawn counter.
            if (!IsRequestStillCurrent(requestStillCurrent, role))
            {
                data?.StopSpawn();
                Plugin.ModLogger.LogInfo(
                    $"Arena burst spawn: discarded superseded {role} profiles before dispatch.");
                return;
            }

            if (data == null || data.Count == 0)
            {
                Plugin.ModLogger.LogWarning($"Arena burst spawn: no profiles for {role} — request dropped.");
                RetrySoon();
                return;
            }

            // Never dispatch more than the caller requested even if a modified
            // profile provider returns a larger batch.
            if (data.Count > count)
                data.Profiles.RemoveRange(count, data.Count - count);

            if (requireExactCount && data.Count != count)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena exact squad spawn: profile provider returned {data.Count}/{count} {role}; no partial squad dispatched.");
                data.StopSpawn();
                return;
            }

            // Profile and bundle loading is asynchronous. The player can cross
            // a 25 m boundary while it runs, so an exact squad must choose its
            // zone again from the current scene state instead of trusting the
            // synchronous preflight performed by SpawnSquad.
            if (requireExactCount)
            {
                zone = PickZone(spawner, count);
                if (zone == null)
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena exact squad spawn: no allowed zone remained after profile loading; no squad dispatched.");
                    data.StopSpawn();
                    return;
                }
            }

            // EXACTLY one point per profile. Handing over more charges the
            // engine's in-spawn counter per POINT while it only ever decrements
            // per created bot — every surplus point permanently shrinks the
            // headroom the engine grants its own spawns. (A stray point would
            // also hit an unguarded Profiles[0] in another mod's hook.)
            var playerPosition = GetPlayerPosition();
            var profileCount = data.Count;
            var points = BurstSpawnPointSelector.PickPoints(
                zone,
                playerPosition,
                profileCount,
                ref _pointCursor,
                out var preferredAvailable,
                out var distantAvailable);

            Plugin.ModLogger.LogInfo(
                $"Arena burst spawn plan: zone {zone.NameZone}, requested {count}, profiles {profileCount}, "
                + $"points {preferredAvailable} preferred + {distantAvailable} distant, "
                + $"selected {points?.Count ?? 0}.");
            if (points == null || points.Count == 0)
            {
                Plugin.ModLogger.LogWarning($"Arena burst spawn: no usable spawn point for {role} — request dropped.");
                if (requireExactCount)
                    data.StopSpawn();
                RetrySoon();
                return;
            }

            if (requireExactCount && points.Count != count)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena exact squad spawn: zone {zone.NameZone} supplied {points.Count}/{count} points; no partial squad dispatched.");
                data.StopSpawn();
                return;
            }

            // Trimmed in BOTH directions, because the two are counted differently
            // by the engine: it charges its in-spawn counter per POINT and
            // decrements per created BOT. Surplus points permanently shrink the
            // headroom it grants its own spawns; surplus profiles get a random
            // position from the list instead — re-stacking exactly what the
            // point picker just took care to avoid — and drive that counter
            // negative on the way.
            if (points.Count > data.Count)
                points.RemoveRange(data.Count, points.Count - data.Count);
            else if (data.Count > points.Count)
                data.Profiles.RemoveRange(points.Count, data.Count - points.Count);

            // Point selection is synchronous today, but keep the mode lease at
            // the last safe boundary as well. Once SpawnBotsInZoneOnPositions is
            // called, only the observer may reject individual late callbacks.
            if (!IsRequestStillCurrent(requestStillCurrent, role))
            {
                data.StopSpawn();
                return;
            }

            reserved = points.Count;
            for (var i = 0; i < reserved; i++)
                ledger.Add(Time.time);

            // This is the last mutation boundary before EFT owns the request.
            // A callback can still arrive after GameEnd, so it repeats the same
            // lease check and retires the resulting body instead of observing it.
            if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner))
            {
                data.StopSpawn();
                ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
                return;
            }

            spawner.SpawnBotsInZoneOnPositions(points, zone, data, bot =>
            {
                if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner))
                {
                    ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                    RetireStaleSpawn(bot, role);
                    return;
                }

                OnBotSpawned(bot, ledger, role, includeLifecycleIdentity: requireExactCount);

                // The observer is how a mode learns WHICH bots are its squad.
                // Wrapped so a throw in someone else's callback cannot take the
                // ledger bookkeeping down with it.
                try
                {
                    observer?.Invoke(bot);
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning($"Arena spawn observer failed: {ex.Message}");
                }
            });

            Plugin.ModLogger.LogInfo($"Arena burst spawn: dispatched {reserved}x {role}.");
        }
        catch (Exception ex)
        {
            ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
            Plugin.ModLogger.LogWarning($"Arena burst spawn of {role} failed: {ex.Message}");
            if (operationGeneration == Volatile.Read(ref _operationGeneration))
                RetrySoon();
        }
    }

    private static bool TryCaptureSpawnLease(
        BotSpawner spawner,
        int operationGeneration,
        out LocalGame game,
        out GameWorld world,
        out BotsController controller,
        out int raidEpoch)
    {
        game = null;
        world = null;
        controller = null;
        raidEpoch = Respawn.ArenaRespawnDriver.CaptureRaidEpoch();

        if (operationGeneration != Volatile.Read(ref _operationGeneration)
            || !ArenaSession.IsActive
            || !Singleton<AbstractGame>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame localGame
            || localGame.Status != GameStatus.Started
            || !Singleton<GameWorld>.Instantiated
            || Singleton<GameWorld>.Instance is not GameWorld currentWorld
            || !ArenaSession.IsExactActiveRaid(localGame, currentWorld)
            || !Singleton<IBotGame>.Instantiated)
        {
            return false;
        }

        BotsController currentController = Singleton<IBotGame>.Instance?.BotsController;
        if (currentController == null
            || !ReferenceEquals(currentController.BotSpawner, spawner))
        {
            return false;
        }

        game = localGame;
        world = currentWorld;
        controller = currentController;
        return Respawn.ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch);
    }

    private static bool IsSpawnLeaseCurrent(
        int operationGeneration,
        LocalGame game,
        GameWorld world,
        int raidEpoch,
        BotsController controller,
        BotSpawner spawner)
        => operationGeneration == Volatile.Read(ref _operationGeneration)
           && Respawn.ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
           && Singleton<AbstractGame>.Instantiated
           && ReferenceEquals(Singleton<AbstractGame>.Instance, game)
           && Singleton<GameWorld>.Instantiated
           && ReferenceEquals(Singleton<GameWorld>.Instance, world)
           && ArenaSession.IsExactActiveRaid(game, world)
           && Singleton<IBotGame>.Instantiated
           && ReferenceEquals(Singleton<IBotGame>.Instance?.BotsController, controller)
           && ReferenceEquals(controller?.BotSpawner, spawner);

    private static void ReleaseReservedIfCurrent(
        List<float> ledger,
        int count,
        int operationGeneration)
    {
        // Reset already cleared the old ledger. Never let its late continuation
        // consume reservations belonging to the next raid.
        if (operationGeneration == Volatile.Read(ref _operationGeneration))
            ReleaseReserved(ledger, count);
    }

    private static void RetireStaleSpawn(BotOwner bot, WildSpawnType role)
    {
        if (ReferenceEquals(bot, null) || bot == null)
            return;

        try
        {
            using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                bot.GetPlayer?.ActiveHealthController?.Kill(EDamageType.Undefined);
            Plugin.ModLogger.LogWarning(
                $"Arena burst spawn: retired a late {role} body from an expired raid lease.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena burst spawn: could not retire a late {role} body: {ex.Message}");
        }
    }

    /// <summary>
    /// Spawns one squad now and reports each bot as it arrives.
    ///
    /// Deliberately built on the same path as the ambient burst: that path
    /// already knows the two placement filters it has to bypass, and a second
    /// implementation would have to learn them again the hard way. Returns how
    /// many bots were REQUESTED — profile generation is a server round trip and
    /// can come back short, so the caller counts arrivals through the observer
    /// rather than trusting this number.
    /// </summary>
    public static int SpawnSquad(
        int count,
        bool asPmc,
        BotDifficulty difficulty,
        Action<BotOwner> observer,
        bool requireExactCount = false,
        Func<bool> requestStillCurrent = null)
    {
        if (count <= 0)
            return 0;

        try
        {
            if (!Singleton<GameWorld>.Instantiated || !(Singleton<IBotGame>.Instance is { } botGame))
                return 0;

            var spawner = botGame.BotsController?.BotSpawner;
            if (spawner == null)
                return 0;

            var zone = PickZone(spawner, count);
            if (zone == null)
            {
                Plugin.ModLogger.LogWarning("Arena squad spawn: no bot zone — squad not dispatched.");
                return 0;
            }

            if (requireExactCount)
            {
                var capacity = BurstSpawnPointSelector.CountUsable(zone, GetPlayerPosition());
                if (capacity < count)
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena exact squad spawn: no allowed zone can supply {count} points "
                        + $"(best {zone.NameZone}: {capacity}); squad not dispatched.");
                    return 0;
                }
            }

            var role = asPmc
                ? (_nextPmcIsBear ? WildSpawnType.pmcBEAR : WildSpawnType.pmcUSEC)
                : WildSpawnType.assault;

            if (asPmc)
                _nextPmcIsBear = !_nextPmcIsBear;

            Plugin.ModLogger.LogInfo($"Arena squad spawn: requesting {count}x {role} ({difficulty}).");
            SpawnPair(
                spawner,
                zone,
                role,
                difficulty,
                asPmc,
                count,
                observer,
                requireExactCount,
                requestStillCurrent);
            return count;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena squad spawn failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>A dropped request must not cost a full cooldown.</summary>
    private static void RetrySoon()
    {
        var soon = Time.time + 3f;
        if (_nextAttemptAt > soon)
            _nextAttemptAt = soon;
    }

    private static void ReleaseReserved(List<float> ledger, int count)
    {
        for (var i = 0; i < count && ledger.Count > 0; i++)
            ledger.RemoveAt(ledger.Count - 1);
    }

    /// <summary>
    /// Fires when EFT has created and pre-activated each injected bot: clears its
    /// reservation and points it at the player. The callback normally observes
    /// PreActive; the bot becomes Active in a later EFT bot update. This is the
    /// ONLY place that knows a bot body really arrived — the request log above is
    /// intent, this is outcome, and the gap between them is the diagnostic.
    /// </summary>
    private static void OnBotSpawned(
        BotOwner bot,
        List<float> ledger,
        WildSpawnType role,
        bool includeLifecycleIdentity)
    {
        try
        {
            if (ledger.Count > 0)
                ledger.RemoveAt(0);

            if (bot == null)
                return;

            if (includeLifecycleIdentity)
            {
                string profileId = string.IsNullOrWhiteSpace(bot.ProfileId)
                    ? "<missing>"
                    : bot.ProfileId.Trim();
                Plugin.ModLogger.LogInfo(
                    $"Arena burst spawn: {role} arrived (profile {profileId}, state {bot.BotState}).");
            }
            else
            {
                Plugin.ModLogger.LogInfo($"Arena burst spawn: {role} arrived.");
            }

            if (!Settings.HuntPlayer.Value)
                return;

            var player = Singleton<GameWorld>.Instantiated
                ? Singleton<GameWorld>.Instance.MainPlayer
                : null;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            ArenaHostility.ReportPlayerTo(bot.BotsGroup, player);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn callback failed: {ex.Message}");
        }
    }

    private static string DescribeRoles()
    {
        if (RoleTally.Count == 0)
            return "none";

        return string.Join(", ", RoleTally.Select(pair => $"{pair.Key} x{pair.Value}"));
    }

    /// <summary>Injections young enough that their bots may not be registered yet.</summary>
    private static int CountPending(List<float> injections)
    {
        var cutoff = Time.time - InFlightSeconds;
        for (var i = injections.Count - 1; i >= 0; i--)
        {
            if (injections[i] < cutoff)
                injections.RemoveAt(i);
        }

        return injections.Count;
    }

    /// <summary>
    /// Living bot owners only. Returns false when the registry is unreadable —
    /// the caller must then do nothing at all. (A sentinel count would take part
    /// in the projection arithmetic and wrap around into a spawn loop.)
    /// </summary>
    private static bool TryCountAliveBots(BotSpawner spawner, out int aliveTotal, out int alivePmc)
    {
        aliveTotal = 0;
        alivePmc = 0;

        var owners = spawner?._bots?.BotOwners;
        if (owners == null)
            return false;

        RoleTally.Clear();
        var alive = 0;
        var pmc = 0;
        foreach (var bot in owners)
        {
            if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                continue;

            // PitFireTeam followers occupy BotOwner slots but are the local
            // player's four teammates. Counting them toward Arena pressure
            // would silently reduce the five requested opponents to one.
            if (Compat.PitFireTeamCompat.IsTeamMember(bot))
                continue;

            alive++;

            var role = bot.Profile?.Info?.Settings?.Role;
            if (role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR)
                pmc++;
            else if (role.HasValue)
                RoleTally[role.Value] = RoleTally.TryGetValue(role.Value, out var seen) ? seen + 1 : 1;
        }

        aliveTotal = alive;
        alivePmc = pmc;
        return true;
    }

    /// <summary>
    /// A different zone per burst. Always taking the first one funnels every
    /// injection into the same corner of the map.
    /// </summary>
    private static BotZone PickZone(BotSpawner spawner, int requiredCount)
    {
        return BurstSpawnPointSelector.PickZone(
            spawner.SpawnZones(false),
            GetPlayerPosition(),
            requiredCount,
            ref _zoneCursor);
    }

    private static bool IsRequestStillCurrent(Func<bool> predicate, WildSpawnType role)
    {
        if (predicate == null)
            return true;

        try
        {
            return predicate();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena burst spawn: {role} request lease threw ({ex.Message}) — cancelling before dispatch.");
            return false;
        }
    }

    private static Vector3? GetPlayerPosition()
        => Singleton<GameWorld>.Instantiated
           && Singleton<GameWorld>.Instance.MainPlayer != null
            ? (Vector3?)Singleton<GameWorld>.Instance.MainPlayer.Position
            : null;

    /// <summary>
    /// Ask the backup loader to keep PMC profiles cached, so the first PMC burst
    /// does not stall on a server-side batch generation.
    /// </summary>
    public static void PrewarmPmcProfiles()
    {
        try
        {
            var spawner = (Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            if (spawner == null)
                return;

            // Keyed by (role, difficulty) with exact equality, so every difficulty
            // we might ASK for needs its own backup — otherwise the pre-warm
            // stops covering PMC refills at exactly the moment pressure peaks
            // and the refills matter most.
            foreach (var difficulty in new[] { BotDifficulty.hard, BotDifficulty.impossible })
            {
                spawner.AddToTargetBackup(difficulty, WildSpawnType.pmcUSEC, 6);
                spawner.AddToTargetBackup(difficulty, WildSpawnType.pmcBEAR, 6);
            }
            Plugin.ModLogger.LogInfo("Arena: PMC profile backup pre-warmed.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena PMC pre-warm failed: {ex.Message}");
        }
    }
}
