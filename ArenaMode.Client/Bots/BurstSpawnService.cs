using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
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

    private static float _nextAttemptAt;
    private static bool _nextPmcIsBear;
    private static int _zoneCursor;

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

    public static void Reset()
    {
        _nextAttemptAt = 0f;
        _zoneCursor = 0;
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
                + $"— injecting {role} pair.");

            _nextAttemptAt = Time.time + ArenaClientConfig.GetBurstCooldownSeconds(map);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn failed: {ex.Message}");
            _nextAttemptAt = Time.time + 10f;
        }
    }

    /// <summary>
    /// Spawns a pair through the plain zone path instead of ActivateBotsByWave.
    ///
    /// ActivateBotsByWave routes through BotBossSpawn.TrySpawn — which ABPS
    /// prefix-patches and owns entirely for PMC roles. On factory it demands a
    /// spawn point far from every living bot AND from the player; with a dozen
    /// bots around it finds none, reports success anyway and drops the wave, so
    /// PMC top-ups silently never happened. This path never reaches that hook.
    /// withCheckMinMax:false + forcedSpawn:true keep the cap immunity the old
    /// IgnoreMaxBots gave us.
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

            spawner.TryToSpawnInZoneAndDelay(zone, data, withCheckMinMax: false, newWave: true,
                pointsToSpawn: null, forcedSpawn: true);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn of {role} failed: {ex.Message}");
        }
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
