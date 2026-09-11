using Comfort.Common;
using EFT;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels
{
    internal static class RaidPlayerContext
    {
        public static bool TryGetLocalPlayer(out Player player)
        {
            player = null;

            if (!Session.ArenaSession.IsActive || Respawn.ArenaRespawnDriver.IsRespawnInProgress
                || Spectator.ArenaSpectatorService.IsPending || Spectator.ArenaSpectatorService.IsActive)
                return false;

            if (!Singleton<GameWorld>.Instantiated)
            {
                return false;
            }

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                return false;
            }

            // Wait for the raid to actually begin (GameStatus.Started is set together with
            // GameTimer.Start and the player spawn, right after the deploy countdown) so the
            // panels don't already pop up over the loading/deploy screens. Also hides them
            // again once the raid is stopping.
            AbstractGame game = Singleton<AbstractGame>.Instance;
            if (game == null || game.Status != GameStatus.Started
                || !Session.ArenaSession.IsExactActiveRaid(game, gameWorld))
            {
                return false;
            }

            player = gameWorld.MainPlayer;
            if (!IsPlayerUsable(player, gameWorld))
            {
                player = null;
                return false;
            }

            return true;
        }

        private static bool IsPlayerUsable(Player player, GameWorld gameWorld)
        {
            if (player == null || !player.IsYourPlayer)
            {
                return false;
            }

            if (player.PlayerBody == null || player.HandsController == null)
            {
                return false;
            }

            // During the raid loading transition the player object can already pass the checks
            // above while health/inventory haven't synced in yet, which used to show empty panel
            // boxes for a moment. Gate on the panels' actual data sources too so the HUD only
            // appears once there's something real to display.
            if (player.HealthController?.IsAlive != true || player.Inventory?.Equipment == null)
            {
                return false;
            }

            if (!gameWorld.AllAlivePlayersList.Contains(player))
            {
                return false;
            }

            return player.PointOfView == EPointOfView.FirstPerson;
        }
    }
}
