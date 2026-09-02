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
        => HasPendingMedicalWork(bot)
           || bot.WeaponManager?.Reload?.Reloading == true
           || bot.WeaponManager?.Grenades?.ThrowindNow == true
           || bot.DoorOpener?.Interacting == true;

    private static bool HasImmediateDanger(BotOwner bot)
        => bot.BewareGrenade?.ShallRunAway() == true
           || bot.BewareBTR?.ShallRunAway() == true
           || bot.ArtilleryDangerPlace?.ShallRunAway() == true
           || bot.FlashGrenade?.IsFlashed == true;

    private static bool HasPendingMedicalWork(BotOwner bot)
    {
        if (bot?.Medecine == null)
            return false;

        BotLogicDecision decision = bot.Brain?.Agent?.LastResult().Action
            ?? BotLogicDecision.holdPosition;
        return bot.Medecine.Using
               || bot.Medecine.FirstAid?.Have2Do == true
               || bot.Medecine.FirstAid?.Using == true
               || bot.Medecine.SurgicalKit?.HaveWork == true
               || bot.Medecine.SurgicalKit?.Using == true
               || bot.Medecine.Stimulators?.Using == true
               || decision == BotLogicDecision.heal
               || decision == BotLogicDecision.healStimulators;
    }

    private static bool IsExactGoalEnemy(BotOwner bot, EnemyInfo enemy)
    {
        EnemyInfo goalEnemy = bot?.Memory?.GoalEnemy;
        return goalEnemy != null
               && string.Equals(
                   goalEnemy.ProfileId,
                   enemy?.ProfileId,
                   StringComparison.Ordinal);
    }

    private static bool TryGetExactEnemy(
        BotOwner bot,
        string targetProfileId,
        out EnemyInfo enemy)
    {
        enemy = null;
        Dictionary<IPlayer, EnemyInfo> enemies = bot.EnemiesController?.EnemyInfos;
        if (enemies == null || string.IsNullOrEmpty(targetProfileId))
            return false;

        foreach (KeyValuePair<IPlayer, EnemyInfo> pair in enemies)
        {
            EnemyInfo candidate = pair.Value;
            if (candidate != null
                && string.Equals(candidate.ProfileId, targetProfileId, StringComparison.Ordinal))
            {
                enemy = candidate;
                return true;
            }
        }

        return false;
    }
}
