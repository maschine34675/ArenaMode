using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using Redeploy.Core.Engine;
using System;

namespace ArenaMode.Combat;

/// <summary>
/// The single owner of arena-side invulnerability.
///
/// The engine's protection routine remembers the CURRENT damage coefficient
/// when it starts and restores it when it ends. Start a second protection while
/// a first one is running and the second remembers 0 — and restores 0 when it
/// expires. That is permanent invulnerability, and a round that ends shortly
/// after a death would hit it every time. Every arena request therefore comes
/// through here, and anything that smells like an overlap is refused.
/// </summary>
internal static class ArenaProtection
{
    /// <summary>
    /// Returns whether protection is actually running now — callers that freeze
    /// the player must not do so on a refusal.
    /// </summary>
    public static bool Begin(float seconds)
    {
        if (seconds <= 0f)
            return false;

        try
        {
            if (!(Singleton<AbstractGame>.Instance is LocalGame game) || game.Status != GameStatus.Started)
                return false;

            var player = Singleton<GameWorld>.Instantiated
                ? Singleton<GameWorld>.Instance.MainPlayer as LocalPlayer
                : null;

            if (player?.ActiveHealthController == null
                || player.HealthController == null
                || !player.HealthController.IsAlive)
            {
                return false;
            }

            // A respawn owns the coefficient for its own protection window.
            if (ArenaRespawnDriver.IsRespawnInProgress)
                return false;

            // Already protected by someone — layering would strand the zero.
            // Report success: the player IS invulnerable right now, which is
            // what the caller actually asked about.
            if (player.ActiveHealthController.DamageCoeff <= 0f)
                return true;

            RespawnEngine.ApplyProtection(game, player, new SpawnProtectionOptions
            {
                DurationSeconds = seconds,
                // A scoreboard is not a fight: drawing a weapon must not end it.
                EndEarlyOnWeapon = false,
                StowAutoDrawnWeapon = false,
                ShowToasts = false,
            });

            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena protection request failed: {ex.Message}");
            return false;
        }
    }
}
