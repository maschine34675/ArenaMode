using ArenaMode.Bots;
using ArenaMode.Session;
using EFT;
using System;
using System.Collections.Generic;

namespace ArenaMode.BigBrainCompat;

/// <summary>Per-tick fail-closed validation shared by the layer and logic.</summary>
internal static class ArenaPursuitRuntime
{
    internal static bool TryGetUsableLease(
        BotOwner bot,
        out TeamArenaPursuitLease lease)
    {
        lease = default;
        try
        {
            if (!ArenaSession.IsActive
                || !IsLive(bot)
                || !TeamArenaPursuitLeaseBridge.TryGet(bot.ProfileId, out TeamArenaPursuitLease current)
                || HasImmediateDanger(bot)
                || IsBusy(bot)
                || !TryGetExactEnemy(bot, current.TargetProfileId, out EnemyInfo enemy)
                || enemy.Person?.HealthController?.IsAlive != true
                || !IsExactGoalEnemy(bot, enemy)
                || (enemy.IsVisible && enemy.CanShoot))
            {
                return false;
            }

            lease = current;
            return true;
        }
        catch (Exception)
        {
            lease = default;
            return false;
        }
    }

    internal static bool HasExactTeamArenaContext()
        => TeamArenaPursuitLeaseBridge.HasExactTeamArenaContext();

    internal static bool TryGetUsableLease(
        BotOwner bot,
        int epoch,
        long generation,
        out TeamArenaPursuitLease lease)
    {
        if (!TryGetUsableLease(bot, out lease)
            || lease.Epoch != epoch
            || lease.Generation != generation)
        {
            lease = default;
            return false;
        }

        return true;
    }

    private static bool IsLive(BotOwner bot)
        => bot != null
           && !bot.IsDead
           && bot.BotState == EBotState.Active
           && bot.GetPlayer != null
           && bot.GetPlayer.HealthController?.IsAlive == true
           && bot.Mover != null;

    private static bool IsBusy(BotOwner bot)
        => IsMedicalActionActive(bot)
           || bot.WeaponManager?.Reload?.Reloading == true
           || bot.WeaponManager?.Grenades?.ThrowindNow == true
           || bot.DoorOpener?.Interacting == true;

    private static bool HasImmediateDanger(BotOwner bot)
        => bot.BewareGrenade?.ShallRunAway() == true
           || bot.BewareBTR?.ShallRunAway() == true
           || bot.ArtilleryDangerPlace?.ShallRunAway() == true
           || bot.FlashGrenade?.IsFlashed == true;

    // Child Using means a medical hands operation is active. Aggregate Using,
    // the available-work flags and the last brain decision can all remain stale
    // after cancellation and must not strand a surviving bot.
    private static bool IsMedicalActionActive(BotOwner bot)
        => bot?.Medecine?.FirstAid?.Using == true
           || bot?.Medecine?.SurgicalKit?.Using == true
           || bot?.Medecine?.Stimulators?.Using == true;

    private static bool IsExactGoalEnemy(BotOwner bot, EnemyInfo enemy)
        => enemy != null && ReferenceEquals(bot?.Memory?.GoalEnemy, enemy);

    private static bool TryGetExactEnemy(
        BotOwner bot,
        string targetProfileId,
        out EnemyInfo enemy)
    {
        enemy = null;
        Dictionary<IPlayer, EnemyInfo> enemies = bot.EnemiesController?.EnemyInfos;
        BotsGroup group = bot.BotsGroup;
        EnemyInfo goalEnemy = bot.Memory?.GoalEnemy;
        string expectedProfile = targetProfileId?.Trim();
        if (enemies == null
            || group?.Enemies == null
            || goalEnemy == null
            || string.IsNullOrEmpty(expectedProfile))
            return false;

        foreach (KeyValuePair<IPlayer, EnemyInfo> pair in enemies)
        {
            EnemyInfo candidate = pair.Value;
            if (pair.Key != null
                && candidate != null
                && ReferenceEquals(candidate, goalEnemy)
                && ReferenceEquals(pair.Key, candidate.Person)
                && ReferenceEquals(candidate.Owner, bot)
                && ReferenceEquals(candidate.GroupOwner, group)
                && ReferenceEquals(candidate.GroupInfo?._botGroup, group)
                && HasCanonicalGroupPlayer(pair.Key, candidate.GroupInfo?.Player, expectedProfile)
                && HasExactGroupEntry(group, pair.Key, candidate.GroupInfo)
                && string.Equals(
                    pair.Key.ProfileId?.Trim(),
                    expectedProfile,
                    StringComparison.Ordinal))
            {
                enemy = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool HasCanonicalGroupPlayer(
        IPlayer person,
        Player groupPlayer,
        string expectedProfile)
    {
        if (person == null
            || groupPlayer?.HealthController?.IsAlive != true
            || !string.Equals(
                groupPlayer.ProfileId?.Trim(),
                expectedProfile,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (person is Player player)
            return ReferenceEquals(player, groupPlayer);

        return person is BotOwner owner
               && ReferenceEquals(owner.GetPlayer, groupPlayer);
    }

    private static bool HasExactGroupEntry(
        BotsGroup group,
        IPlayer person,
        BotGroupEnemyInfo expected)
    {
        if (group?.Enemies == null || person == null || expected == null)
            return false;

        foreach (KeyValuePair<IPlayer, BotGroupEnemyInfo> pair in group.Enemies)
        {
            if (ReferenceEquals(pair.Key, person)
                && ReferenceEquals(pair.Value, expected))
            {
                return true;
            }
        }

        return false;
    }
}
