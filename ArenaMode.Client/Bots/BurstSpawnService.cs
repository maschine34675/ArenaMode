using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Top-up burst spawner: whenever the alive-bot count drops below the target,
/// inject an immediate assault wave via BotSpawner.ActivateBotsByWave with
/// ForceSpawn + IgnoreMaxBots — cap-immune and without a server round trip.
/// The ABPS pump (or the generated wave plan) is the baseline pressure; this
/// closes the refill gap after a killing spree.
/// </summary>
internal static class BurstSpawnService
{
    private const float InFlightSeconds = 20f;
    private const int BotsPerBurst = 2;

    /// <summary>Spawn band around the player: far enough to not pop into view, near enough to matter.</summary>
    private const float MinSpawnDistance = 25f;
    private const float MaxSpawnDistance = 70f;

    private static float _nextAttemptAt;
    private static bool _nextPmcIsBear;
    private static int _zoneCursor;
    private static int _pointCursor;

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
    private static readonly Dictionary<WildSpawnType, int> RoleTally = new Dictionary<WildSpawnType, int>();

    public static void Reset()
    {
        _nextAttemptAt = 0f;
        _zoneCursor = 0;
        _pointCursor = 0;
        RecentPmcInjections.Clear();
        RecentScavInjections.Clear();
    }

    public static void Tick()
    {
        if (!ArenaSession.IsActive || Time.time < _nextAttemptAt)
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
            var pendingPmc = CountPending(RecentPmcInjections);
            var pendingScav = CountPending(RecentScavInjections);
            var projected = alive + (pendingPmc + pendingScav) * BotsPerBurst;

            // PMCs are the pressure that makes the arena fun; scavs only fill the
            // rest. Without a separate floor the single "alive" target is met by
            // whatever spawns fastest — which was scavs, every time.
            var pmcFloor = Mathf.Min(Settings.PmcFloor.Value, target);
            var wantPmc = alivePmc + pendingPmc * BotsPerBurst < pmcFloor;
            if (!wantPmc && projected >= target)
                return;

            var counterAlive = controller.AliveLoadingDelayedBotsCount;

            var zone = PickZone(spawner);
            if (zone == null)
                return;

            var role = wantPmc
                ? (_nextPmcIsBear ? WildSpawnType.pmcBEAR : WildSpawnType.pmcUSEC)
                : WildSpawnType.assault;

            var difficulty = wantPmc ? BotDifficulty.hard : BotDifficulty.normal;
            SpawnPair(spawner, zone, role, difficulty);

            if (wantPmc)
            {
                _nextPmcIsBear = !_nextPmcIsBear;
                RecentPmcInjections.Add(Time.time);
            }
            else
            {
                RecentScavInjections.Add(Time.time);
            }

            Plugin.ModLogger.LogInfo(
                $"Arena burst spawn: {alive} alive ({alivePmc} PMC) / target {target} (PMC floor {pmcFloor}), "
                + $"in flight {pendingPmc} PMC + {pendingScav} scav (spawner counter {counterAlive}) "
                + $"— injecting {role} pair. Others alive: {DescribeRoles()}");

            _nextAttemptAt = Time.time + ArenaClientConfig.GetBurstCooldownSeconds(map);
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
    private static async void SpawnPair(BotSpawner spawner, BotZone zone, WildSpawnType role, BotDifficulty difficulty)
    {
        try
        {
            var profileParams = new GetProfileDataParams(
                EPlayerSide.Savage, // SPT restores the real PMC side later — do not "fix" this
                role,
                difficulty,
                -1f,
                new BotSpawnParams());

            var data = await BotCreationData.Create(profileParams, spawner._botCreator, BotsPerBurst, spawner);
            if (data == null)
            {
                Plugin.ModLogger.LogWarning($"Arena burst spawn: no profile data for {role}.");
                return;
            }

            var points = PickSpawnPoints(zone, BotsPerBurst);
            if (points == null || points.Count == 0)
            {
                Plugin.ModLogger.LogWarning($"Arena burst spawn: no usable spawn point for {role}.");
                return;
            }

            spawner.SpawnBotsInZoneOnPositions(points, zone, data, OnBotSpawned);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn of {role} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Spawn points from the zone, preferring a band that is far enough away to
    /// not materialise in the player's face but close enough to reach the fight
    /// in seconds. PMCs appearing right next to the player was the engine's own
    /// point picking; ours is deliberate.
    /// </summary>
    private static List<ISpawnPoint> PickSpawnPoints(BotZone zone, int count)
    {
        var all = zone?.SpawnPoints;
        if (all == null || all.Length == 0)
            return null;

        var player = Singleton<GameWorld>.Instantiated
            ? Singleton<GameWorld>.Instance.MainPlayer
            : null;

        var result = new List<ISpawnPoint>(count);
        if (player == null)
        {
            for (var i = 0; i < all.Length && result.Count < count; i++)
            {
                if (all[i] != null && !all[i].SpawnBlocked)
                    result.Add(all[i]);
            }

            return result;
        }

        var position = player.Position;
        var candidates = new List<ISpawnPoint>(all.Length);
        var fallback = new List<ISpawnPoint>(all.Length);

        foreach (var point in all)
        {
            if (point == null || point.SpawnBlocked)
                continue;

            var distance = Vector3.Distance(point.Position, position);
            if (distance >= MinSpawnDistance && distance <= MaxSpawnDistance)
                candidates.Add(point);
            else if (distance >= MinSpawnDistance)
                fallback.Add(point);
        }

        var source = candidates.Count > 0 ? candidates : fallback;
        if (source.Count == 0)
            return null;

        // Spread the pairs around instead of stacking them on one point.
        for (var i = 0; i < count; i++)
        {
            _pointCursor = (_pointCursor + 1) % source.Count;
            result.Add(source[_pointCursor]);
        }

        return result;
    }

    /// <summary>Fires as each injected bot activates — point it at the player right away.</summary>
    private static void OnBotSpawned(BotOwner bot)
    {
        try
        {
            if (bot == null || !Settings.HuntPlayer.Value)
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
    private static BotZone PickZone(BotSpawner spawner)
    {
        var zones = spawner.SpawnZones(false)?.ToList();
        if (zones == null || zones.Count == 0)
            return null;

        _zoneCursor = (_zoneCursor + 1) % zones.Count;
        return zones[_zoneCursor];
    }

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

            spawner.AddToTargetBackup(BotDifficulty.hard, WildSpawnType.pmcUSEC, 6);
            spawner.AddToTargetBackup(BotDifficulty.hard, WildSpawnType.pmcBEAR, 6);
            Plugin.ModLogger.LogInfo("Arena: PMC profile backup pre-warmed.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena PMC pre-warm failed: {ex.Message}");
        }
    }
}
