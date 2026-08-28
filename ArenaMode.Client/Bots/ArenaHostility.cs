using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps every bot group aware of where the player is, so bots close in instead
/// of patrolling until they happen to walk into you.
///
/// Deliberately minimal: it reports a SIGHTING to the group and lets the vanilla
/// brain draw its own conclusions. Writing Memory.GoalEnemy directly (the
/// obvious approach) is worse than useless — the engine's own goal calculation
/// clears a GoalEnemy for any enemy that was never seen, and SAIN clears it on
/// every decision tick. Feeding the group's last-seen position instead flips
/// EnemyInfo.HaveSeen, which is the precondition BOTH brains actually read.
///
/// One write per GROUP per sweep, no pathfinding, no search points: search
/// points are shared per group, so one per bot would pile up duplicates and
/// (above 20 bots) drag synchronous navmesh queries into a single frame.
/// </summary>
internal static class ArenaHostility
{
    private const float SweepIntervalSeconds = 6f;

    private static readonly HashSet<BotsGroup> SweepGroups = new HashSet<BotsGroup>();

    private static float _nextSweepAt;
    private static bool _loggedFailure;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        _loggedFailure = false;
        SweepGroups.Clear();
    }

    /// <summary>Per-frame from the controller; does its own pacing.</summary>
    public static void Tick()
    {
        if (!ArenaSession.IsActive || !Config.Settings.HuntPlayer.Value || Time.time < _nextSweepAt)
            return;

        _nextSweepAt = Time.time + SweepIntervalSeconds;

        try
        {
            if (!Singleton<GameWorld>.Instantiated)
                return;

            var player = Singleton<GameWorld>.Instance.MainPlayer;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            var spawner = (Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            var owners = spawner?._bots?.BotOwners;
            if (owners == null)
                return;

            SweepGroups.Clear();
            foreach (var bot in owners)
            {
                if (bot == null || bot.BotState != EBotState.Active)
                    continue;

                if (bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                if (bot.BotsGroup != null)
                    SweepGroups.Add(bot.BotsGroup);
            }

            foreach (var group in SweepGroups)
                ReportPlayer(group, player);
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena hostility sweep failed (silenced for this raid): {ex}");
            }
        }
    }

    private static void ReportPlayer(BotsGroup group, Player player)
    {
        try
        {
            if (group.Enemies == null)
                return;

            if (!group.Enemies.ContainsKey(player))
            {
                group.SetAggressiveToAllNewPlayers(true);
                group.AddEnemy(player, EBotEnemyCause.addPlayer);
            }

            if (!group.Enemies.TryGetValue(player, out var enemyInfo) || enemyInfo == null)
                return;

            // The setter stamps the sighting time, notifies the group and raises
            // IsHaveSeen — everything the brains need to start hunting.
            enemyInfo.EnemyLastPosition = player.Position;
            enemyInfo.IsLastPositionChecked = false;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena hostility report failed (silenced for this raid): {ex}");
            }
        }
    }
}
