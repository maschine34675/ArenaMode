using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
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
    private static float _nextAttemptAt;

    public static void Reset() => _nextAttemptAt = 0f;

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

            // Alive + loading + delayed (the counter ABPS's own pump uses): counting
            // only activated bots would keep injecting cap-immune pairs while earlier
            // spawns are still in flight and overshoot the target badly.
            var alive = botGame.BotsController?.AliveLoadingDelayedBotsCount ?? int.MaxValue;
            if (alive >= target)
                return;

            var spawner = botGame.BotsController?.BotSpawner;
            if (spawner == null)
                return;

            var zone = spawner.SpawnZones(false).FirstOrDefault();
            if (zone == null)
                return;

            var wave = new BossLocationSpawn
            {
                BossName = "assault",
                BossChance = 100f,
                BossZone = zone.NameZone,
                BossDifficult = "normal",
                BossEscortType = "assault",
                BossEscortDifficult = "normal",
                BossEscortAmount = "1",
                Time = -1f,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                TriggerId = string.Empty,
                TriggerName = string.Empty,
            };
            wave.BossType = WildSpawnType.assault;
            wave.EscortType = WildSpawnType.assault;
            wave.EscortCount = 1;
            wave.BornZone = zone.NameZone;
            wave.ShallSpawn = true;

            spawner.ActivateBotsByWave(wave);
            Plugin.ModLogger.LogInfo($"Arena burst spawn: {alive}/{target} bots alive — injecting assault pair.");

            _nextAttemptAt = Time.time + ArenaClientConfig.GetBurstCooldownSeconds(map);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena burst spawn failed: {ex.Message}");
            _nextAttemptAt = Time.time + 10f;
        }
    }
}
