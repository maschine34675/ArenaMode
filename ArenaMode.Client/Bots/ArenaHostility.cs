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
    private const float SweepIntervalSeconds = 15f;

    /// <summary>
    /// Only groups with a bot inside this radius get told. Dragging distant
    /// groups into the fight keeps more bots simultaneously in the state that
    /// switches on the expensive AI machinery (cover finding, path-vision
    /// raycasts) — and on factory 60m already covers most of the map.
    /// </summary>
    private const float SweepRadius = 60f;

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

        // A round break is a pause, so stop handing out the player's position.
        // Old sightings still stand — the bots go on searching where the round
        // ended, which is exactly where the player no longer is. Deliberately
        // without touching _nextSweepAt: the sweep resumes the moment the next
        // round starts.
        if (Modes.ArenaMatchService.Current?.Mode is Modes.RoundsMode rounds && rounds.InIntermission)
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

            var playerPosition = player.Position;
            var radiusSqr = SweepRadius * SweepRadius;

            SweepGroups.Clear();
            foreach (var bot in owners)
            {
                if (bot == null || bot.BotState != EBotState.Active)
                    continue;

                if (bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                if (bot.BotsGroup == null)
                    continue;

                if ((bot.Position - playerPosition).sqrMagnitude > radiusSqr)
                    continue;

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

    /// <summary>
    /// Whether this group counts the player as one of its own.
    ///
    /// The arena makes every bot hostile to the human, which is right until the
    /// human brings friends: a teammate mod (Pit Fire Team is the one this was
    /// written against) puts the player into its followers' group as an ALLY,
    /// and forcing hostility over the top of that turns your own squad on you.
    ///
    /// Asked through BSG's own Allies list rather than through anything the
    /// teammate mod exposes, so this needs no reference to it and holds for any
    /// friendly-bot mod that uses the game's concept of an ally.
    ///
    /// One definition, called from all three places that force hostility — the
    /// IsPlayerEnemy postfix, the raid-start sweep and this service's own tick.
    /// Three copies of a rule like this drift, and the drift would show up as
    /// teammates that shoot you only sometimes.
    /// </summary>
    public static bool CountsAsAlly(BotsGroup group, IPlayer player)
    {
        var allies = group?.Allies;
        if (allies == null || player == null)
            return false;

        // Reference equality by hand: IPlayer implementations are free to define
        // their own Equals, and "is this the same body" is the question here.
        for (var i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], player))
                return true;
        }

        return false;
    }

    /// <summary>Public entry for freshly spawned bots (their group may be brand new).</summary>
    public static void ReportPlayerTo(BotsGroup group, Player player)
    {
        if (group != null && player != null)
            ReportPlayer(group, player);
    }

    private static void ReportPlayer(BotsGroup group, Player player)
    {
        try
        {
            if (group.Enemies == null || CountsAsAlly(group, player))
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
