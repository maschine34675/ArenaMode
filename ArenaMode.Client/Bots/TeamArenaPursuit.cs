using ArenaMode.Compat;
using ArenaMode.Session;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps a protected 5v5 moving after the local PMC has died. The caller owns
/// roster validity; this service only gives every living bot fresh sensed
/// knowledge of its nearest living opponent and lets EFT/SAIN choose how to act.
/// </summary>
internal static class TeamArenaPursuit
{
    private const float SweepSeconds = 1f;

    private static readonly HashSet<string> LoggedFailures = new HashSet<string>(StringComparer.Ordinal);
    private static int _epoch = -1;
    private static float _nextSweepAt;

    private static bool _pftBridgeResolved;
    private static PropertyInfo _pftCombatEnabled;
    private static PropertyInfo _pftRuntimeCallbacks;
    private static MethodInfo _pftSyncEnemy;

    private static bool _sainBridgeResolved;
    private static MethodInfo _sainGetBot;
    private static PropertyInfo _sainEnemyController;
    private static PropertyInfo _sainBotActivation;
    private static PropertyInfo _sainInCombat;
    private static MethodInfo _sainCheckAddEnemy;
    private static PropertyInfo _sainGoalEnemy;
    private static MethodInfo _sainUpdateLastSeen;
    private static MethodInfo _sainSetInCombat;

    public static void Reset()
    {
        _epoch = -1;
        _nextSweepAt = 0f;
        LoggedFailures.Clear();
        ResetOptionalBridges();
    }

    public static void BeginRound(int epoch)
    {
        _epoch = epoch;
        _nextSweepAt = 0f;
        LoggedFailures.Clear();
    }

    /// <summary>
    /// Called only after Tactical has validated both fixed rosters and their
    /// hostility matrix. No references are retained beyond this sweep.
    /// </summary>
    public static void Tick(
        int epoch,
        bool localPlayerDown,
        IReadOnlyList<BotOwner> allies,
        IReadOnlyList<BotOwner> opponents)
    {
        if (!localPlayerDown
            || epoch != _epoch
            || !ArenaSession.IsActive
            || !PitFireTeamCompat.TeamArenaActive
            || Time.time < _nextSweepAt)
        {
            return;
        }

        _nextSweepAt = Time.time + SweepSeconds;

        var liveAllies = Snapshot(allies);
        var liveOpponents = Snapshot(opponents);
        if (liveAllies.Count == 0 || liveOpponents.Count == 0)
            return;

        foreach (var ally in liveAllies)
            RefreshNearest(ally, liveOpponents, epoch, follower: true);

        foreach (var opponent in liveOpponents)
            RefreshNearest(opponent, liveAllies, epoch, follower: false);
    }

    private static List<BotOwner> Snapshot(IReadOnlyList<BotOwner> candidates)
    {
        var result = new List<BotOwner>();
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        if (candidates == null)
            return result;

        foreach (var candidate in candidates)
        {
            if (candidate == null
                || candidate.BotState != EBotState.Active
                || candidate.HealthController?.IsAlive != true
                || candidate.GetPlayer == null
                || candidate.GetPlayer.HealthController?.IsAlive != true)
            {
                continue;
            }

            string profileId = candidate.ProfileId?.Trim();
            if (string.IsNullOrEmpty(profileId) || !profiles.Add(profileId))
                continue;

            result.Add(candidate);
        }

        return result;
    }

    private static void RefreshNearest(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates,
        int epoch,
        bool follower)
    {
        BotOwner nearest = null;
        float bestDistance = float.MaxValue;
        string bestProfile = null;

        foreach (var candidate in candidates)
        {
            float distance = (candidate.Position - source.Position).sqrMagnitude;
            string profile = candidate.ProfileId ?? string.Empty;
            if (nearest == null
                || distance < bestDistance
                || (Math.Abs(distance - bestDistance) < 0.0001f
                    && string.CompareOrdinal(profile, bestProfile) < 0))
            {
                nearest = candidate;
                bestDistance = distance;
                bestProfile = profile;
            }
        }

        if (nearest == null)
            return;

        try
        {
            if (!RefreshNative(source, nearest.GetPlayer))
                return;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, "eft", ex);
            return;
        }

        // PFT's optional addon bridge is the authoritative SAIN path for a
        // follower. If it is disabled/absent, the native knowledge above stays
        // valid and the direct SAIN bridge below can still help other bots.
        if (follower && TryRefreshPft(source, nearest.GetPlayer, epoch))
            return;

        TryRefreshSain(source, nearest.GetPlayer, epoch);
    }

    private static bool RefreshNative(BotOwner source, Player target)
    {
        var group = source?.BotsGroup;
        if (group == null || target == null || target.HealthController?.IsAlive != true)
            return false;

        if (!group.IsEnemy(target))
            group.AddEnemy(target, EBotEnemyCause.bossKillArena);

        if (!group.IsEnemy(target))
            return false;

        group.ReportAboutEnemy(target, EEnemyPartVisibleType.Sence, source);
        if (group.Enemies != null
            && group.Enemies.TryGetValue(target, out var info)
            && info != null)
        {
            info.IsLastPositionChecked = false;
        }

        return true;
    }

    private static bool TryRefreshPft(BotOwner source, Player target, int epoch)
    {
        try
        {
            ResolvePftBridge();
            if (_pftSyncEnemy == null
                || _pftCombatEnabled?.GetValue(null) is not bool enabled
                || !enabled
                || _pftRuntimeCallbacks?.GetValue(null) is not bool callbacks
                || !callbacks)
            {
                return false;
            }

            return _pftSyncEnemy.Invoke(null, new object[] { source, target, true }) is bool synced
                   && synced;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, "pft-sain", ex);
            return false;
        }
    }

    private static void TryRefreshSain(BotOwner source, Player target, int epoch)
    {
        try
        {
            ResolveSainBridge();
            if (_sainGetBot == null)
                return;

            object[] getArgs = { source.ProfileId, null };
            if (_sainGetBot.Invoke(null, getArgs) is not bool found || !found || getArgs[1] == null)
                return;

            object sainBot = getArgs[1];
            object enemyController = _sainEnemyController.GetValue(sainBot);
            if (enemyController == null)
                return;

            object enemy = _sainCheckAddEnemy.Invoke(enemyController, new object[] { target });
            if (enemy == null)
                return;

            _sainUpdateLastSeen.Invoke(enemy, new object[] { target.Position, Time.time });
            _sainGoalEnemy.GetSetMethod(nonPublic: true)?.Invoke(enemyController, new[] { enemy });

            object activation = _sainBotActivation.GetValue(sainBot);
            if (activation != null
                && (_sainInCombat.GetValue(sainBot) is not bool inCombat || !inCombat))
            {
                _sainSetInCombat.Invoke(activation, new object[] { true });
            }
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, "sain", ex);
        }
    }

    private static void ResolvePftBridge()
    {
        if (_pftBridgeResolved)
            return;

        _pftBridgeResolved = true;
        var type = FindType("pitTeam.Modules.SainAddonBridge", "pitFireTeam");
        if (type == null)
            return;

        _pftCombatEnabled = type.GetProperty("IsFollowerCombatEnabled", BindingFlags.Public | BindingFlags.Static);
        _pftRuntimeCallbacks = type.GetProperty("HasRuntimeCallbacks", BindingFlags.Public | BindingFlags.Static);
        _pftSyncEnemy = type.GetMethod(
            "TrySyncEnemyState",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(BotOwner), typeof(Player), typeof(bool) },
            null);
    }

    private static void ResolveSainBridge()
    {
        if (_sainBridgeResolved)
            return;

        _sainBridgeResolved = true;
        var enableType = FindType("SAIN.SAINEnableClass", "SAIN");
        var botType = FindType("SAIN.Components.BotComponent", "SAIN");
        if (enableType == null || botType == null)
            return;

        _sainGetBot = enableType.GetMethod(
            "GetSAIN",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), botType.MakeByRefType() },
            null);
        _sainEnemyController = botType.GetProperty("EnemyController", BindingFlags.Public | BindingFlags.Instance);
        _sainBotActivation = botType.GetProperty("BotActivation", BindingFlags.Public | BindingFlags.Instance);
        _sainInCombat = botType.GetProperty("IsInCombat", BindingFlags.Public | BindingFlags.Instance);

        var controllerType = _sainEnemyController?.PropertyType;
        _sainCheckAddEnemy = controllerType?.GetMethod("CheckAddEnemy", new[] { typeof(IPlayer) });
        _sainGoalEnemy = controllerType?.GetProperty("GoalEnemy", BindingFlags.Public | BindingFlags.Instance);
        var enemyType = _sainCheckAddEnemy?.ReturnType;
        _sainUpdateLastSeen = enemyType?.GetMethod(
            "UpdateLastSeenPosition",
            new[] { typeof(Vector3), typeof(float) });

        var activationType = _sainBotActivation?.PropertyType;
        _sainSetInCombat = activationType?.GetMethod("SetInCombat", new[] { typeof(bool) });

        if (_sainGetBot == null
            || _sainEnemyController == null
            || _sainBotActivation == null
            || _sainInCombat == null
            || _sainCheckAddEnemy == null
            || _sainGoalEnemy?.GetSetMethod(nonPublic: true) == null
            || _sainUpdateLastSeen == null
            || _sainSetInCombat == null)
        {
            ResetSainMembers();
        }
    }

    private static Type FindType(string fullName, string assemblyName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                continue;

            return assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        }

        return null;
    }

    private static void LogOnce(int epoch, BotOwner source, string adapter, Exception exception)
    {
        string profile = source?.ProfileId ?? "?";
        string key = epoch + ":" + profile + ":" + adapter;
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit {adapter} refresh failed for {Describe(source)} "
            + $"in round epoch {epoch}; native combat continues: {exception.GetBaseException()}");
    }

    private static string Describe(BotOwner source)
    {
        string name = source?.Profile?.Nickname ?? "?";
        string profile = source?.ProfileId ?? "?";
        string side = PitFireTeamCompat.IsTeamMember(source) ? "ally" : "opponent";
        return $"{side} '{name}' ({profile}, state={source?.BotState.ToString() ?? "missing"})";
    }

    private static void ResetOptionalBridges()
    {
        _pftBridgeResolved = false;
        _pftCombatEnabled = null;
        _pftRuntimeCallbacks = null;
        _pftSyncEnemy = null;
        _sainBridgeResolved = false;
        ResetSainMembers();
    }

    private static void ResetSainMembers()
    {
        _sainGetBot = null;
        _sainEnemyController = null;
        _sainBotActivation = null;
        _sainInCombat = null;
        _sainCheckAddEnemy = null;
        _sainGoalEnemy = null;
        _sainUpdateLastSeen = null;
        _sainSetInCombat = null;
    }
}
