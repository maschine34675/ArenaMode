using ArenaMode.Compat;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Bots;

/// <summary>One checked synchronous goal assignment, shared by producer and optional movement consumer.</summary>
public static class TeamArenaGoalHandoff
{
    public static bool TrySetLeaseGoal(BotOwner source, TeamArenaPursuitLease lease, EnemyInfo enemy, out string problem)
    {
        problem = "stale-pursuit-lease";
        if (source == null || !TeamArenaPursuitLeaseBridge.TryGet(source.ProfileId, lease.Epoch, lease.Generation, out var current)
            || current.SourceProfileId != lease.SourceProfileId
            || current.TargetProfileId != lease.TargetProfileId || enemy?.Person?.ProfileId != current.TargetProfileId) return false;
        if (!TrySetGoal(source, enemy, current.Epoch, out problem)) return false;
        // Goal-change callbacks may synchronously revoke or replace the lease.
        if (!TeamArenaPursuitLeaseBridge.TryGet(source.ProfileId, lease.Epoch, lease.Generation, out var after)
            || after.TargetProfileId != lease.TargetProfileId)
        { problem = "pursuit-lease-ended-during-setter"; return false; }
        return true;
    }

    internal static bool TrySetGoal(BotOwner source, EnemyInfo enemy, int epoch, out string problem)
    {
        problem = "pursuit-context-invalid";
        try
        {
            var target = enemy?.GroupInfo?.Player?.AIData?.BotOwner;
            if (ArenaMatchService.Current?.Mode is not TacticalMode mode
                || !mode.AllowsPursuitGoal(epoch, source, target, out bool follower)) return false;
            var world = Singleton<GameWorld>.Instance;
            if (!IsCurrentBody(world, source) || !IsCurrentBody(world, target))
            { problem = "pursuit-body-not-current"; return false; }
            if (!HasExactRelation(source, target, enemy))
            { problem = "pursuit-enemy-relation-invalid"; return false; }

            var previous = source.Memory.GoalEnemy;
            if (ReferenceEquals(previous, enemy)) { problem = null; return true; }
            // Native unsubscribes the previous target through its persistent
            // registry. A dead previous target is valid; a removed/reused body is not.
            if (previous != null)
            {
                var previousBody = previous.Person is BotOwner owner ? owner.GetPlayer : previous.Person as Player;
                if (previousBody == null || string.IsNullOrEmpty(previous.Person?.ProfileId)
                    || !ReferenceEquals(world.GetEverExistedPlayerByID(previous.Person.ProfileId), previousBody))
                { problem = "previous-goal-body-not-in-world"; return false; }
            }

            IDisposable scope = null;
            try
            {
                if (follower && !PitFireTeamGoalContext.TryEnter(out scope, out problem)) return false;
                source.Memory.GoalEnemy = enemy;
            }
            finally { scope?.Dispose(); }
            if (!mode.AllowsPursuitGoal(epoch, source, target, out _)
                || !IsCurrentBody(world, source) || !IsCurrentBody(world, target))
            { problem = "pursuit-context-ended-during-setter"; return false; }
            if (!HasExactRelation(source, target, enemy))
            { problem = "pursuit-relation-ended-during-setter"; return false; }
            // Do not retry or clear the field: PFT missions and other native
            // decision owners retain their veto, including after entering scope.
            if (!ReferenceEquals(source.Memory.GoalEnemy, enemy))
            { problem = "native-goal-setter-refused"; return false; }
            problem = null;
            return true;
        }
        catch (Exception ex)
        { problem = "goal-handoff-exception:" + ex.GetBaseException().GetType().Name; return false; }
    }

    private static bool HasExactRelation(BotOwner source, BotOwner target, EnemyInfo enemy)
    {
        var group = source.BotsGroup;
        return source.Memory != null && group != null && enemy?.Person != null
            && (ReferenceEquals(enemy.Person, target) || ReferenceEquals(enemy.Person, target.GetPlayer))
            && ReferenceEquals(enemy.GroupInfo?.Player, target.GetPlayer)
            && ReferenceEquals(enemy.Owner, source) && ReferenceEquals(enemy.GroupOwner, group)
            && ReferenceEquals(enemy.GroupInfo?._botGroup, group)
            && TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(group.Enemies, enemy.Person)
            && group.Enemies.TryGetValue(enemy.Person, out var groupInfo) && ReferenceEquals(groupInfo, enemy.GroupInfo)
            && source.EnemiesController?.EnemyInfos != null
            && TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(source.EnemiesController.EnemyInfos, enemy.Person)
            && source.EnemiesController.EnemyInfos.TryGetValue(enemy.Person, out var registered) && ReferenceEquals(registered, enemy);
    }

    private static bool IsCurrentBody(GameWorld world, BotOwner bot)
    {
        var player = bot?.GetPlayer;
        return bot != null && bot.BotState == EBotState.Active && !bot.IsDead
            && bot.HealthController?.IsAlive == true && player?.HealthController?.IsAlive == true
            && ReferenceEquals(player.AIData?.BotOwner, bot) && !string.IsNullOrEmpty(bot.ProfileId)
            && bot.ProfileId == player.ProfileId
            && ReferenceEquals(world.GetAlivePlayerByProfileID(bot.ProfileId), player)
            && ReferenceEquals(world.GetEverExistedPlayerByID(bot.ProfileId), player);
    }
}
