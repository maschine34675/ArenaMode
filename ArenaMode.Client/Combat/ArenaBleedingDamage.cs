using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using System;

namespace ArenaMode.Combat;

/// <summary>Scale the native bleed HP transaction, not shared damage descriptors or effect timers.</summary>
internal static class ArenaBleedingDamage
{
    internal static float Adjust(ActiveHealthController health, float damage, EDamageType type)
    {
        if (!ArenaSession.IsActive || (type != EDamageType.LightBleeding && type != EDamageType.HeavyBleeding)
            || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0) return damage;
        try
        {
            float multiplier = Settings.BleedingDamageMultiplier?.Value ?? 1f;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 1f) return damage;
            multiplier = Math.Min(10f, multiplier);
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            var mode = ArenaMatchService.Current?.Mode;
            var player = health?.Player;
            if (!ArenaSession.IsExactActiveRaid(game, world) || game?.Status != GameStatus.Started
                || mode == null || mode.IsComplete || ArenaRespawnDriver.StandDown
                || health?.IsAlive != true || health.DamageCoeff <= 0f || player == null
                || !ReferenceEquals(player.ActiveHealthController, health) || string.IsNullOrEmpty(player.ProfileId)
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)) return damage;
            if (player.IsAI)
            {
                var owner = player.AIData?.BotOwner;
                if (owner == null || owner.BotState != EBotState.Active || owner.IsDead
                    || !ReferenceEquals(owner.GetPlayer, player)) return damage;
            }
            else if (!player.IsYourPlayer || !ReferenceEquals(world.MainPlayer, player)
                || !ReferenceEquals(game.LocalPlayer, player) || ArenaRespawnDriver.IsRespawnInProgress) return damage;
            float result = damage * multiplier;
            return float.IsInfinity(result) ? damage : result;
        }
        catch { return damage; }
    }
}
