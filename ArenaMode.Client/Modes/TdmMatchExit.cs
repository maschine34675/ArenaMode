using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using System;

namespace ArenaMode.Modes;

/// <summary>Native terminal operation, separate from the pure match rules.</summary>
internal static class TdmMatchExit
{
    internal static bool PipelineRunning => RespawnEngineState.IsPipelineRunning;

    internal static void TryStop(LocalGame game, GameWorld world, string profileId, Action<string> reportProblem = null)
    {
        if (game == null || world == null || PipelineRunning || UnityEngine.Time.timeScale <= 0f || game.Status != GameStatus.Started
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !ArenaSession.IsExactActiveRaid(game, world)
            || !ArenaMatchResult.OwnsRaid(ArenaMatchService.Current?.Mode) || string.IsNullOrEmpty(profileId) || game.ProfileId != profileId) return;
        try
        {
            // BaseLocalGame.Stop dereferences this owner before it schedules
            // GameEnd. An ended/failed factory coroutine alone is not evidence
            // that a usable owner has been restored.
            var owner = LocalGameAccessor.GetPlayerOwner(game);
            var local = game.LocalPlayer;
            if (owner == null || local == null || !ReferenceEquals(owner.Player, local)
                || !ReferenceEquals(world.MainPlayer, local) || local.ProfileId != profileId)
            {
                reportProblem?.Invoke("The native player owner is not ready; safe exit pending.");
                return;
            }
            var status = local.HealthController?.IsAlive == true ? ExitStatus.Survived : ExitStatus.Killed;
            ArenaRespawnDriver.StandDown = true;
            game.Stop(profileId, status, "Arena", 0f);
            if (game.Status == GameStatus.Started)
            {
                ArenaRespawnDriver.StandDown = false;
                reportProblem?.Invoke("Native exit was not committed; retaining the terminal hold.");
            }
            else Plugin.ModLogger.LogDebug($"Arena match result exit committed ({status}); protected native end follows.");
        }
        catch (Exception ex)
        {
            bool committed = game.Status != GameStatus.Started;
            if (!committed) ArenaRespawnDriver.StandDown = false;
            Plugin.ModLogger.LogError("Arena match result exit failed "
                + (committed ? "after native teardown committed; no retry: " : "before commit; will retry: ") + ex);
        }
    }
}
