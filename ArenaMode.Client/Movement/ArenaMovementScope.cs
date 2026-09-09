using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Movement;

/// <summary>Current live physical body only; never menu previews or an old respawn body.</summary>
internal static class ArenaMovementScope
{
    internal static bool IsLocal(Player player, bool requireInput = false)
    {
        if (!ArenaSession.IsActive || Settings.MovementEnabled?.Value != true
            || player == null || player.IsAI || !player.IsYourPlayer
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || ArenaRespawnDriver.StandDown || ArenaRespawnDriver.IsRespawnInProgress
            || ArenaSpectatorService.IsActive) return false;
        try
        {
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            return world != null && game != null && game.Status == GameStatus.Started
                && ArenaSession.IsExactActiveRaid(game, world)
                && ReferenceEquals(world.MainPlayer, player) && ReferenceEquals(game.LocalPlayer, player)
                && player.HealthController?.IsAlive == true && player.MovementContext != null
                && (!requireInput || (!player.IsInventoryOpened && !ArenaInputBlocker.Blocked));
        }
        catch (Exception) { return false; }
    }

    internal static bool TryGetPlayer(MovementContext context, out Player player, bool requireInput = false)
    {
        player = null;
        if (!ArenaSession.IsActive || context == null || !Singleton<GameWorld>.Instantiated) return false;
        var candidate = Singleton<GameWorld>.Instance?.MainPlayer;
        if (!IsLocal(candidate, requireInput) || !ReferenceEquals(candidate.MovementContext, context)) return false;
        player = candidate;
        return true;
    }
}
