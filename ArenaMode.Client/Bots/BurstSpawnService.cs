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
        if (Modes.ArenaMatchService.TeamStartAbortPending) return;
        if (Areas.ArenaAreaRuntime.IsPending) return;
        if (ArenaSoloPopulation.IsSelected)
        {
            ArenaSoloPopulation.Tick();
            return;
        }
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

            Plugin.ModLogger.LogDebug(
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
        bool requireExactCount = false, Func<bool> requestStillCurrent = null,
        bool explicitFaction = false, Action<bool> dispatchFinished = null, Action arrivalRejected = null,
        Func<bool> beginNativeDispatch = null, int requestId = 0, float preflightSeconds = 0f)
    {
        // TDM owns keyed population tickets. Its requests must not release an
        // unrelated request's reservation through the ambient FIFO ledger.
        var ledger = explicitFaction ? new List<float>(1)
            : isPmc ? RecentPmcInjections : RecentScavInjections;
        var reserved = 0;
        var operationGeneration = Volatile.Read(ref _operationGeneration);
        BotCreationData data = null;
        bool handedToNative = false;
        // Only ZombieSpawnService opts into this single-request contract.
        // Ambient and existing faction call paths retain their own ownership.
        bool ownedInfected = role.IsInfected() && dispatchFinished != null && beginNativeDispatch != null;
        Areas.ArenaAreaRuntime.TeamSpawnLease areaSpawnLease = null;
        ArenaTeamBoxRuntime.Lease boxSpawnLease = null;

        try
        {
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

            var profileParams = new GetProfileDataParams(
                EPlayerSide.Savage, // SPT restores the real PMC side later — do not "fix" this
                role,
                difficulty,
                -1f,
                // MoreBotsAPI observes every native creation and reads this
                // marker without a null guard. Ours must not opt into hunts.
                new BotSpawnParams { Id_spawn = "arenamode-owned" });

            // Profile generation is best-effort: the request can come back with
            // fewer profiles than asked for, or with none at all.
            float profileStartedAt = Time.realtimeSinceStartup;
            data = await BotCreationData.Create(profileParams, spawner._botCreator, count, spawner);
            float profileSeconds = Time.realtimeSinceStartup - profileStartedAt;
            if (explicitFaction)
                Plugin.ModLogger.LogDebug($"Team Deathmatch preparation request {requestId} ({role}): "
                    + FormattableString.Invariant($"preflight={preflightSeconds:F3}s, BotCreationData.Create={profileSeconds:F3}s; profiles={data?.Count ?? 0}."));

            // GameEnd can run while profile generation is awaiting the server.
            // Stop the detached creation data before touching cursors, ledgers or
            // the spawner when any part of the exact raid lease changed.
            if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner))
            {
                data?.StopSpawn();
                Plugin.ModLogger.LogDebug(
                    $"Arena burst spawn: discarded stale {role} profiles after the raid lease ended.");
                return;
            }

            // Preparation has a separate terminal watchdog from native arrival.
            // Always recheck ownership after profiles/pools return; a completed
            // task is not permission to reopen a stopped/expired preparation.
            if (!IsRequestStillCurrent(requestStillCurrent, role))
            {
                data?.StopSpawn();
                Plugin.ModLogger.LogDebug(
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

            // SPT restores PMC Side during profile selection. The requested
            // role is already authoritative here; the actual body's side is
            // checked below after activation. Never repair a mismatched profile.
            if ((explicitFaction || ownedInfected) && data.Profiles.Any(profile => profile?.Info?.Settings?.Role != role))
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena {(ownedInfected ? "zombie" : "faction")} spawn: profile provider changed requested {role}; no bot dispatched.");
                data.StopSpawn();
                return;
            }

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
            bool areaTeamSpawn = explicitFaction && Areas.ArenaAreaRuntime.HasTeamStarts;
            bool boxTeamSpawn = explicitFaction && ArenaTeamBoxRuntime.IsRequired;
            if (requireExactCount && !areaTeamSpawn && !boxTeamSpawn)
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
            float placementStartedAt = Time.realtimeSinceStartup;
            List<ISpawnPoint> points;
            int preferredAvailable, distantAvailable;
            if (boxTeamSpawn)
            {
                if (profileCount != 1 || !ArenaTeamBoxRuntime.TryPlanBot(spawner.SpawnZones(false),
                    role == WildSpawnType.pmcUSEC, true, out zone, out var boxPoint, out boxSpawnLease))
                {
                    data.StopSpawn();
                    Plugin.ModLogger.LogDebug($"Arena Arena team spawn deferred: no safe unoccupied team-box point for {role}.");
                    return;
                }
                points = new List<ISpawnPoint> { boxPoint };
                preferredAvailable = 1; distantAvailable = 0;
            }
            else if (areaTeamSpawn)
            {
                if (profileCount != 1 || !Areas.ArenaAreaRuntime.TryPlanTeamBot(spawner.SpawnZones(false),
                    role == WildSpawnType.pmcUSEC, true, out zone, out var teamPoint, out areaSpawnLease))
                {
                    data.StopSpawn();
                    Plugin.ModLogger.LogDebug($"Arena team spawn deferred: no unoccupied connected point for {role}.");
                    Plugin.ModLogger.LogDebug($"Team Deathmatch placement request {requestId} ({role}) deferred: "
                        + FormattableString.Invariant($"fresh plan={Time.realtimeSinceStartup - placementStartedAt:F3}s."));
                    return;
                }
                points = new List<ISpawnPoint> { teamPoint };
                preferredAvailable = 1; distantAvailable = 0;
            }
            // An exact multi-bot squad is one team and starts as one: group it.
            // A single-profile exact request (Team Deathmatch) keeps the ambient
            // round-robin, where grouping would mean nothing anyway.
            else points = BurstSpawnPointSelector.PickPoints(zone, playerPosition, profileCount,
                ref _pointCursor, out preferredAvailable, out distantAvailable,
                grouped: requireExactCount && profileCount > 1);

            if (explicitFaction)
                Plugin.ModLogger.LogDebug($"Team Deathmatch placement request {requestId} ({role}): "
                    + FormattableString.Invariant($"fresh plan={Time.realtimeSinceStartup - placementStartedAt:F3}s."));

            Plugin.ModLogger.LogDebug(
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

            BotOwner observedFactionBody = null;
            bool factionArrivalSeen = false;
            Action<BotOwner> onArrival = bot =>
            {
                // Native/modded activation can repeat a callback. Once the root
                // settled its ticket its predicate is false, so check the exact
                // same body BEFORE that predicate or a duplicate would kill it.
                if ((explicitFaction || ownedInfected) && factionArrivalSeen)
                {
                    if (ReferenceEquals(observedFactionBody, bot))
                        return;

                    RetireStaleSpawn(bot, role);
                    NotifyArrivalRejected(arrivalRejected);
                    return;
                }

                if (explicitFaction || ownedInfected)
                {
                    factionArrivalSeen = true;
                    observedFactionBody = bot;
                }

                if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner))
                {
                    areaSpawnLease?.Dispose();
                    boxSpawnLease?.Dispose();
                    ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                    RetireStaleSpawn(bot, role);
                    if (explicitFaction || ownedInfected)
                        NotifyArrivalRejected(arrivalRejected);
                    return;
                }

                // Request cancellation after native dispatch cannot StopSpawn:
                // EFT may have charged more points than it will decrement.
                // Retire just this late/wrong body before hostility or scoring.
                if (role.IsInfected() && !IsRequestStillCurrent(requestStillCurrent, role))
                {
                    areaSpawnLease?.Dispose();
                    boxSpawnLease?.Dispose();
                    ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                    RetireStaleSpawn(bot, role);
                    if (ownedInfected) NotifyArrivalRejected(arrivalRejected);
                    return;
                }
                if (ownedInfected && (bot == null || bot.GetPlayer == null || bot.HealthController == null
                    || bot.Profile?.Info?.Settings?.Role != role
                    || string.IsNullOrEmpty(bot.ProfileId)
                    || !ReferenceEquals(bot.GetPlayer.AIData?.BotOwner, bot)
                    || !string.Equals(bot.ProfileId, bot.GetPlayer.ProfileId, StringComparison.Ordinal)))
                {
                    ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                    RetireStaleSpawn(bot, role);
                    NotifyArrivalRejected(arrivalRejected);
                    return;
                }
                if (explicitFaction && (!IsRequestStillCurrent(requestStillCurrent, role)
                    || !HasRequestedFaction(bot, role)))
                {
                    areaSpawnLease?.Dispose();
                    boxSpawnLease?.Dispose();
                    ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                    RetireStaleSpawn(bot, role);
                    NotifyArrivalRejected(arrivalRejected);
                    return;
                }

                if (areaSpawnLease != null)
                {
                    bool accepted = areaSpawnLease.Accept(bot?.GetPlayer);
                    areaSpawnLease.Dispose();
                    if (!accepted)
                    {
                        ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                        Plugin.ModLogger.LogWarning($"Arena team arrival rejected: {role}, marker {areaSpawnLease.PointId}, "
                            + $"reserved {areaSpawnLease.Position}, actual {bot?.GetPlayer?.Position}; "
                            + "reservation, faction, distance, occupancy or navigation no longer valid.");
                        RetireStaleSpawn(bot, role);
                        NotifyArrivalRejected(arrivalRejected);
                        return;
                    }
                }

                if (boxSpawnLease != null)
                {
                    bool accepted = boxSpawnLease.Accept(bot?.GetPlayer);
                    boxSpawnLease.Dispose();
                    if (!accepted)
                    {
                        ReleaseReservedIfCurrent(ledger, 1, operationGeneration);
                        Plugin.ModLogger.LogWarning($"Arena Arena team arrival rejected: {role}, marker {boxSpawnLease.PointId}, "
                            + $"reserved {boxSpawnLease.Position}, actual {bot?.GetPlayer?.Position}; "
                            + "reservation, faction, occupancy or navigation no longer valid.");
                        RetireStaleSpawn(bot, role);
                        NotifyArrivalRejected(arrivalRejected);
                        return;
                    }
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
                    if (explicitFaction || ownedInfected)
                    {
                        RetireStaleSpawn(bot, role);
                        NotifyArrivalRejected(arrivalRejected);
                    }
                }
            };

            if (explicitFaction)
            {
                // This is the only preparation -> native transition. Do not
                // move it before the await or after submission: a warmed native
                // call may synchronously deliver its observer. The repeated
                // request predicate cannot reset this one-shot arrival clock.
                if (!IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner)
                    || !IsRequestStillCurrent(requestStillCurrent, role)
                    || beginNativeDispatch == null
                    || !IsRequestStillCurrent(beginNativeDispatch, role)
                    || !IsSpawnLeaseCurrent(operationGeneration, game, world, raidEpoch, controller, spawner)
                    || !IsRequestStillCurrent(requestStillCurrent, role))
                {
                    ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
                    reserved = 0;
                    return;
                }
                // The ownership scope must NEVER span an await: only this exact
                // spawner/data dispatch may pass the TDM prefix. A callback may
                // arrive synchronously before the terminal dispatch notification.
                using (var scope = TeamDeathmatchSpawnDispatch.Enter(spawner, data))
                {
                    handedToNative = true;
                    try
                    {
                        spawner.SpawnBotsInZoneOnPositions(points, zone, data, onArrival);
                    }
                    finally
                    {
                        // Only our prefix can prove rejection before counters.
                        // A throw or another mod's denial is otherwise uncertain.
                        if (scope.WasRejected)
                            handedToNative = false;
                    }
                }

                if (!handedToNative)
                {
                    ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
                    reserved = 0;
                    return;
                }
            }
            else
            {
                if (ownedInfected && (!IsRequestStillCurrent(beginNativeDispatch, role)
                    || !IsRequestStillCurrent(requestStillCurrent, role)))
                {
                    ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
                    reserved = 0;
                    return;
                }
                // Set before the call: warmed activation may callback inline;
                // a thrown native call is uncertain, never a free retry slot.
                if (ownedInfected) handedToNative = true;
                spawner.SpawnBotsInZoneOnPositions(points, zone, data, onArrival);
            }

            Plugin.ModLogger.LogDebug($"Arena burst spawn: dispatched {reserved}x {role}.");
        }
        catch (OperationCanceledException) when (operationGeneration != Volatile.Read(ref _operationGeneration))
        {
            // The raid ended while this dispatch was in flight. Reset bumped the
            // generation from the GameEnd postfix, in Stop's own frame, and
            // cleared the ledgers with it; the cancellation itself arrives later,
            // from the bundle-pool token that TryUnloadGame cancels once GameEnd's
            // backend round trip is through (r2 saw it 6 s after the stop). An
            // expected end of a late request, not a failure — and the GPU log
            // rules count every "failed:" as one. Nothing to release or retry.
            Plugin.ModLogger.LogDebug($"Arena burst spawn of {role} cancelled with the raid.");
        }
        catch (Exception ex)
        {
            ReleaseReservedIfCurrent(ledger, reserved, operationGeneration);
            Plugin.ModLogger.LogWarning($"Arena burst spawn of {role} failed: {ex.Message}");
            if (operationGeneration == Volatile.Read(ref _operationGeneration))
                RetrySoon();
        }
        finally
        {
            if (!handedToNative) areaSpawnLease?.Dispose();
            if (!handedToNative) boxSpawnLease?.Dispose();
            if (explicitFaction && !handedToNative)
            {
                try
                {
                    data?.StopSpawn();
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning($"Arena faction spawn cancellation failed: {ex.Message}");
                }
            }

            if (ownedInfected && !handedToNative)
            {
                try { data?.StopSpawn(); }
                catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena zombie spawn cancellation failed: {ex.Message}"); }
            }

            // True is NOT an activation acknowledgement: native activation is
            // detached and may never callback. The mode keeps this ticket in its
            // bounded uncertain set until an arrival/rejection proves completion.
            NotifyDispatchFinished(dispatchFinished, handedToNative);
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
            || Modes.ArenaMatchService.TeamStartAbortPending
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
           && !Modes.ArenaMatchService.TeamStartAbortPending
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
            if (bot.HealthController?.IsAlive == true)
                throw new InvalidOperationException("the late body is still alive after administrative retirement");
            Plugin.ModLogger.LogDebug(
                $"Arena burst spawn: retired a late or rejected {role} body.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena burst spawn: could not retire a late {role} body: {ex.Message}");
        }
    }

    /// <summary>
    /// Negotiated solo single-bot path. It has its own raid lifetime, independent
    /// of the ambient reset used during player replacement, and never assumes
    /// that an ordinary scav or a Labyrinth boss has a PMC faction.
    /// </summary>
    internal static async void SpawnManagedBot(
        WildSpawnType role, BotDifficulty difficulty, Func<BotOwner, bool> observer,
        Func<bool> requestStillCurrent, Func<bool> beginNativeDispatch,
        Action<bool> dispatchFinished, Action<BotOwner, bool, bool> arrivalRejected, int requestId)
    {
        BotCreationData data = null;
        bool handedToNative = false;
        int generation = ArenaSoloPopulation.Generation;
        try
        {
            if (observer == null || requestStillCurrent == null || beginNativeDispatch == null
                || !IsManagedRole(role) || !ArenaSoloPopulation.IsLeaseCurrent(generation)
                || !IsRequestStillCurrent(requestStillCurrent, role)) return;
            var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
            if (spawner == null || PickZone(spawner, 1) == null) return;
            bool Current() => ArenaSoloPopulation.IsLeaseCurrent(generation)
                && ReferenceEquals(Singleton<IBotGame>.Instance?.BotsController?.BotSpawner, spawner)
                && IsRequestStillCurrent(requestStillCurrent, role);

            var profileParams = new GetProfileDataParams(EPlayerSide.Savage, role, difficulty, -1f,
                new BotSpawnParams { Id_spawn = "arenamode-owned" });
            data = await BotCreationData.Create(profileParams, spawner._botCreator, 1, spawner);
            if (!Current() || data == null || data.Count != 1 || data.Profiles[0]?.Info?.Settings?.Role != role)
                return;
            // Re-plan after profile/pool preparation. The ordinary selector's
            // distance, enabled-zone and area filters remain authoritative.
            var zone = PickZone(spawner, 1);
            if (zone == null) return;
            var points = BurstSpawnPointSelector.PickPoints(zone, GetPlayerPosition(), 1, ref _pointCursor,
                out _, out _, grouped: false);
            if (points == null || points.Count != 1 || !Current()) return;

            bool arrivalSeen = false;
            BotOwner observedBody = null;
            Action<BotOwner> onArrival = bot =>
            {
                if (arrivalSeen && ReferenceEquals(observedBody, bot)) return;
                bool duplicate = arrivalSeen;
                arrivalSeen = true;
                if (!duplicate) observedBody = bot;
                try
                {
                    if (!duplicate && Current() && HasManagedIdentity(bot, role))
                    {
                        // Direct creation does not run BotBossSpawn's callback.
                        // That native callback initializes the real boss logic;
                        // match it explicitly with zero followers inside our slot.
                        if (role == WildSpawnType.bossTagillaAgro || role == WildSpawnType.bossKillaAgro)
                            bot.Boss.SetBoss(0);
                        if (observer(bot))
                        {
                            OnBotSpawned(bot, new List<float>(0), role, includeLifecycleIdentity: true);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning($"Arena solo arrival {requestId} rejected: {ex.Message}");
                }

                RetireStaleSpawn(bot, role);
                // Null/missing health or a failed kill is not evidence that the
                // native slot is free. Keep the reservation in those cases.
                bool retired = false;
                try { retired = !ReferenceEquals(bot, null) && bot != null
                    && bot.HealthController != null && !bot.HealthController.IsAlive; }
                catch { }
                // Retiring an extra callback body proves nothing about the
                // first body's unresolved capacity (its kill may have failed).
                try { arrivalRejected?.Invoke(bot, retired, !duplicate); }
                catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena solo rejection notification failed: {ex.Message}"); }
            };

            if (!Current() || !IsRequestStillCurrent(beginNativeDispatch, role) || !Current()) return;
            using (var scope = ArenaSoloSpawnDispatch.Enter(spawner, data))
            {
                // Set before invoking native: completion can be synchronous, and
                // an exception after entry still leaves activation uncertain.
                handedToNative = true;
                try { spawner.SpawnBotsInZoneOnPositions(points, zone, data, onArrival); }
                finally { if (scope.WasRejected) handedToNative = false; }
            }
            Plugin.ModLogger.LogDebug($"Arena solo population: request {requestId}, {role}, native={handedToNative}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena solo preparation {requestId}: {ex.Message}");
        }
        finally
        {
            if (!handedToNative)
            {
                try { data?.StopSpawn(); }
                catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena solo cancellation failed: {ex.Message}"); }
            }
            NotifyDispatchFinished(dispatchFinished, handedToNative);
        }
    }

    private static bool IsManagedRole(WildSpawnType role)
        => role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR || role == WildSpawnType.assault
            || role == WildSpawnType.bossTagillaAgro || role == WildSpawnType.bossKillaAgro;

    private static bool HasManagedIdentity(BotOwner bot, WildSpawnType role)
    {
        if (bot == null || bot.Profile?.Info?.Settings?.Role != role || bot.GetPlayer == null
            || bot.HealthController == null || !bot.HealthController.IsAlive
            || bot.GetPlayer.HealthController == null || !bot.GetPlayer.HealthController.IsAlive
            || string.IsNullOrWhiteSpace(bot.ProfileId)
            || !string.Equals(bot.ProfileId, bot.GetPlayer.ProfileId, StringComparison.Ordinal)) return false;
        var side = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec
            : role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage;
        return bot.Profile.Info.Side == side && bot.GetPlayer.Side == side;
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

            Plugin.ModLogger.LogDebug($"Arena squad spawn: requesting {count}x {role} ({difficulty}).");
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

    /// <summary>
    /// One native Savage bot of an arbitrary infected role/difficulty, for Zombie
    /// mode's endless wave spawner (see ZombieSpawnService). Built on SpawnPair's
    /// internals exactly like the ambient burst and SpawnSquad do: the same
    /// 25-70 m point ring (<see cref="BurstSpawnPointSelector"/>), the same
    /// SpawnBotsInZoneOnPositions dispatch, and — because <c>isPmc</c> is false —
    /// the same scav in-flight ledger (RecentScavInjections) SpawnPair already
    /// uses for its own correctness. Sharing that ledger with the ambient scav
    /// path costs nothing: Zombie sets ControlsOwnSpawns, which makes Tick()
    /// (the ledger's only OTHER reader/writer) return before it ever touches the
    /// spawner while a Zombie raid is live (see ManualControl above), so nothing
    /// else observes or races these entries. ZombieSpawnService keeps its own,
    /// separate "pending" count for the wave policy — see its doc comment.
    ///
    /// The placement this method hands to the engine is the actual resting spot
    /// only because ZombieNativeEventBridge's fifth Harmony hook suppresses
    /// InfectedLayersStrategy's own ctor-time teleport outright. That native
    /// brain constructor runs unconditionally the moment the engine activates
    /// the body this call dispatches, and — decompile-verified — collapses to a
    /// semi-random relocation near the bot's OWN spawn whenever no pursuit is
    /// active yet (true for every bot this method ever spawns before its first
    /// sighting), which would otherwise defeat the ring above. See that hook's
    /// own doc comment for the full chain.
    ///
    /// Rejects at the door, with a logged reason, rather than letting a bad draw
    /// reach the native brain:
    /// - Only the four GENERIC infected roles ZombieRoleMix ever draws are
    ///   accepted. WildSpawnTypeExtension.IsInfected(role) is ALSO true for a
    ///   fifth role, infectedTagilla, but that one is a pure BOSS (orchestrator's
    ///   native facts, decompile-verified) that needs BotBossSpawn's SetBoss
    ///   initialization the way SpawnManagedBot's bossTagillaAgro/bossKillaAgro
    ///   branch provides; this entry point does not, so it must never accept it.
    /// - Only normal and hard difficulties are accepted. InfectedLayersStrategy's
    ///   constructor (decompile-verified, InfectedLayersStrategy.cs:149-158)
    ///   throws ArgumentOutOfRangeException for BotDifficulty.impossible; easy is
    ///   technically safe (GetZombieModeByDifficulty returns Slow) but outside
    ///   the v0 role mix by design — see ZombieRoleMix's own doc comment.
    /// </summary>
    internal static bool SpawnInfected(WildSpawnType role, BotDifficulty difficulty, Action<BotOwner> onArrival,
        Func<bool> requestCurrent = null, Func<bool> beginNativeDispatch = null,
        Action<bool> dispatchFinished = null, Action arrivalRejected = null)
    {
        if (role != WildSpawnType.infectedAssault && role != WildSpawnType.infectedPmc
            && role != WildSpawnType.infectedCivil && role != WildSpawnType.infectedLaborant)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena zombie spawn: {role} is not one of the four generic infected roles Zombie mode drives — request rejected.");
            return false;
        }

        if (difficulty != BotDifficulty.normal && difficulty != BotDifficulty.hard)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena zombie spawn: {difficulty} is not a supported infected difficulty in this mode (only normal/hard) — request rejected.");
            return false;
        }

        try
        {
            if (!Singleton<GameWorld>.Instantiated || !(Singleton<IBotGame>.Instance is { } botGame))
                return false;

            var spawner = botGame.BotsController?.BotSpawner;
            if (spawner == null)
                return false;

            var zone = PickZone(spawner, 1);
            if (zone == null)
            {
                Plugin.ModLogger.LogWarning("Arena zombie spawn: no bot zone — request dropped.");
                return false;
            }

            var match = Modes.ArenaMatchService.Current;
            if (match?.Mode is not Modes.ZombieMode zombie || !zombie.IsSpawnAdmissionOpen)
                return false;
            SpawnPair(spawner, zone, role, difficulty, isPmc: false, count: 1, observer: onArrival,
                requestStillCurrent: () => ReferenceEquals(Modes.ArenaMatchService.Current, match)
                    && zombie.IsSpawnAdmissionOpen && (requestCurrent == null || requestCurrent()),
                beginNativeDispatch: beginNativeDispatch, dispatchFinished: dispatchFinished,
                arrivalRejected: arrivalRejected);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie spawn of {role} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// One explicit PMC faction request, with no impact on the ambient/Tactical
    /// alternation. The caller reserves its keyed ticket BEFORE calling this:
    /// warmed profile/pool tasks may deliver a body synchronously. A positive
    /// return is planning only; dispatchFinished(true) still leaves native
    /// activation uncertain until an observer/rejection notification arrives.
    /// </summary>
    internal static int SpawnFactionBot(
        WildSpawnType role,
        BotDifficulty difficulty,
        Action<BotOwner> observer,
        Func<bool> requestStillCurrent,
        Func<bool> beginNativeDispatch,
        Action<bool> dispatchFinished,
        Action arrivalRejected,
        int requestId)
    {
        try
        {
            if ((role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR)
                || observer == null || requestStillCurrent == null || beginNativeDispatch == null
                || !TeamDeathmatchSpawnDispatch.IsActive
                || !IsRequestStillCurrent(requestStillCurrent, role)
                || !Singleton<IBotGame>.Instantiated)
            {
                NotifyDispatchFinished(dispatchFinished, false);
                return 0;
            }

            var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
            if (spawner == null || !TryCaptureSpawnLease(
                    spawner, Volatile.Read(ref _operationGeneration),
                    out _, out _, out _, out _))
            {
                NotifyDispatchFinished(dispatchFinished, false);
                return 0;
            }

            BotZone zone;
            float preflightStartedAt = Time.realtimeSinceStartup;
            bool available = ArenaTeamBoxRuntime.IsRequired
                ? ArenaTeamBoxRuntime.TryPlanBot(spawner.SpawnZones(false), role == WildSpawnType.pmcUSEC,
                    false, out zone, out _, out _)
                : Areas.ArenaAreaRuntime.HasTeamStarts
                ? Areas.ArenaAreaRuntime.TryPlanTeamBot(spawner.SpawnZones(false), role == WildSpawnType.pmcUSEC,
                    false, out zone, out _, out _)
                : (zone = PickZone(spawner, 1)) != null && BurstSpawnPointSelector.CountUsable(zone, GetPlayerPosition()) >= 1;
            if (!available)
            {
                Plugin.ModLogger.LogDebug($"Team Deathmatch preflight request {requestId} ({role}) deferred: no usable point; "
                    + FormattableString.Invariant($"preflight={Time.realtimeSinceStartup - preflightStartedAt:F3}s."));
                NotifyDispatchFinished(dispatchFinished, false);
                return 0;
            }

            SpawnPair(spawner, zone, role, difficulty, true, 1, observer,
                requireExactCount: true,
                requestStillCurrent: requestStillCurrent,
                explicitFaction: true,
                dispatchFinished: dispatchFinished,
                arrivalRejected: arrivalRejected,
                beginNativeDispatch: beginNativeDispatch,
                requestId: requestId,
                preflightSeconds: Time.realtimeSinceStartup - preflightStartedAt);
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena faction spawn planning failed: {ex.Message}");
            NotifyDispatchFinished(dispatchFinished, false);
            return 0;
        }
    }

    private static bool HasRequestedFaction(BotOwner bot, WildSpawnType role)
    {
        try
        {
            if (bot == null || bot.Profile?.Info?.Settings?.Role != role)
                return false;

            var player = bot.GetPlayer;
            EPlayerSide expected = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear;
            return player != null
                   && bot.Profile.Info.Side == expected
                   && player.Side == expected
                   && player.HealthController != null
                   && player.HealthController.IsAlive
                   && !string.IsNullOrWhiteSpace(bot.ProfileId)
                   && string.Equals(bot.ProfileId, player.ProfileId, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena faction arrival identity unreadable: {ex.Message}");
            return false;
        }
    }

    private static void NotifyDispatchFinished(Action<bool> callback, bool handedToNative)
    {
        try
        {
            callback?.Invoke(handedToNative);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spawn dispatch notification failed: {ex.Message}");
        }
    }

    private static void NotifyArrivalRejected(Action callback)
    {
        try
        {
            callback?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spawn rejection notification failed: {ex.Message}");
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
                Plugin.ModLogger.LogDebug(
                    $"Arena burst spawn: {role} arrived (profile {profileId}, state {bot.BotState}).");
            }
            else
            {
                Plugin.ModLogger.LogDebug($"Arena burst spawn: {role} arrived.");
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
    {
        var local = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        if (Modes.ArenaMatchService.Current?.Mode is Modes.ZombieMode { IsTeamArena: true } zombie
            && (local == null || local.HealthController?.IsAlive != true))
        {
            foreach (var member in zombie.LiveTeamMembers)
                if (member != null && member.HealthController?.IsAlive == true) return member.Position;
            return null;
        }
        return local != null ? (Vector3?)local.Position : null;
    }

    /// <summary>
    /// Ask the backup loader to keep PMC profiles cached, so the first PMC burst
    /// does not stall on a server-side batch generation.
    /// </summary>
    public static void PrewarmPmcProfiles()
    {
        // This runs before ArenaMatch is constructed. Finite TDM freezes these
        // same settings at OnMatchStart; do not preload its peak difficulty with
        // opening-tier equipment. Its per-wave server batch is request-sized.
        if (ArenaSession.IsActive && ArenaBotDifficultyClockPatch.IsReady && ArenaSession.ActiveMode == "teamdeathmatch"
            && Settings.TeamDeathmatchTimeLimitSeconds.Value > 0) return;
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
            Plugin.ModLogger.LogDebug("Arena: PMC profile backup pre-warmed.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena PMC pre-warm failed: {ex.Message}");
        }
    }
}
