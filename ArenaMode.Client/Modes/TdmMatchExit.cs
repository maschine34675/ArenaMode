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
    private sealed class ExitLease
    {
        internal readonly LocalGame Game;
        internal readonly GameWorld World;
        internal readonly string ProfileId;
        internal readonly int Epoch;

        internal ExitLease(LocalGame game, GameWorld world, string profileId)
        {
            Game = game;
            World = world;
            ProfileId = profileId;
            Epoch = ArenaSession.ArmEpoch;
        }
    }

    private static ExitLease _exit;
    internal static bool PipelineRunning => RespawnEngineState.IsPipelineRunning;

    internal static void Reset() => _exit = null;

    // ResultOwnsCurrentRaid deliberately stops matching once native Stop starts.
    // Preserve only the exact prior authorization, never a general StandDown.
    internal static bool OwnsTerminalTeardown(LocalGame game, GameWorld world, string profileId)
    {
        var lease = _exit;
        return lease != null && game != null && world != null
            && ReferenceEquals(lease.Game, game) && ReferenceEquals(lease.World, world)
            && lease.Epoch == ArenaSession.ArmEpoch
            && string.Equals(lease.ProfileId, profileId, StringComparison.Ordinal)
            && string.Equals(game.ProfileId, profileId, StringComparison.Ordinal)
            && ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            && ReferenceEquals(Singleton<GameWorld>.Instance, world)
            && ReferenceEquals(game.GameWorld, world)
            && ArenaSession.IsExactActiveRaid(game, world)
            && IsNativeTeardown(game);
    }

    private static bool IsNativeTeardown(LocalGame game)
        => game.Status == GameStatus.Stopping || game.Status == GameStatus.Stopped;

    internal static void TryStop(LocalGame game, GameWorld world, string profileId, Action<string> reportProblem = null)
    {
        if (game == null || world == null || PipelineRunning || UnityEngine.Time.timeScale <= 0f || game.Status != GameStatus.Started
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !ReferenceEquals(game.GameWorld, world)
            || !ArenaSession.IsExactActiveRaid(game, world)
            || !ArenaMatchResult.OwnsRaid(ArenaMatchService.Current?.Mode) || string.IsNullOrEmpty(profileId) || game.ProfileId != profileId) return;
        ExitLease lease = null;
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
            // Install before Stop: GameEnd may synchronously reset this lease.
            // Never publish it again after Stop returns or throws.
            lease = new ExitLease(game, world, profileId);
            _exit = lease;
            ArenaRespawnDriver.StandDown = true;
            game.Stop(profileId, status, "Arena", 0f);
            if (!IsNativeTeardown(game))
            {
                ArenaRespawnDriver.StandDown = false;
                reportProblem?.Invoke("Native exit was not committed; retaining the terminal hold.");
            }
            else Plugin.ModLogger.LogDebug($"Arena match result exit committed ({status}); protected native end follows.");
        }
        catch (Exception ex)
        {
            bool committed = IsNativeTeardown(game);
            if (!committed) ArenaRespawnDriver.StandDown = false;
            Plugin.ModLogger.LogError("Arena match result exit failed "
                + (committed ? "after native teardown committed; no retry: " : "before commit; will retry: ") + ex);
        }
        finally
        {
            if (lease != null && ReferenceEquals(_exit, lease) && !IsNativeTeardown(game))
                _exit = null;
        }
    }
}
