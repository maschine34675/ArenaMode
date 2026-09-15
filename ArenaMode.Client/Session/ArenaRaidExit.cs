using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>Skip only the active Arena raid's misleading item-loss confirmation.</summary>
internal static class ArenaRaidExit
{
    private static LocalGame _game;
    private static GameWorld _world;
    private static string _profileId;
    private static string _waitingFor;

    public static void Enable() => new LeavePatch().Enable();

    // Return true only when this service owns the native leave request.
    internal static bool Request(LocalGame game)
    {
        if (!ArenaSession.IsActive || game == null) return false;
        var world = Singleton<GameWorld>.Instance;
        if (!IsCurrent(game, world) || string.IsNullOrEmpty(game.ProfileId)) return false;
        if (game.Status == GameStatus.Stopping || game.Status == GameStatus.Stopped) return true;
        if (game.Status != GameStatus.Started) return false;
        if (ReferenceEquals(_game, game) && ReferenceEquals(_world, world)) return true;
        _game = game;
        _world = world;
        _profileId = game.ProfileId;
        _waitingFor = null;
        Plugin.ModLogger.LogInfo("Arena leave requested; skipping the item-loss confirmation.");
        Tick();
        return true;
    }

    public static void Tick()
    {
        if (ReferenceEquals(_game, null)) return;
        var game = _game;
        if (!IsCurrent(game, _world) || game.Status != GameStatus.Started || game.ProfileId != _profileId)
        {
            Clear();
            return;
        }

        bool standDown = ArenaRespawnDriver.StandDown;
        try
        {
            // Native Stop dereferences _playerOwner before starting its fade.
            // A death hold is not evidence of a running factory; wait on the
            // actual Core pipeline, then revalidate the newly rebound owner.
            if (RespawnEngineState.IsPipelineRunning) { WaitFor("respawn completion"); return; }
            if (Time.timeScale <= 0f) { WaitFor("the game to be unpaused"); return; }
            var owner = LocalGameAccessor.GetPlayerOwner(game);
            var local = game.LocalPlayer;
            if (owner == null || local == null || !ReferenceEquals(owner.Player, local)
                || !ReferenceEquals(_world.MainPlayer, local) || local.ProfileId != _profileId)
            {
                WaitFor("the native player owner");
                return;
            }

            ArenaRespawnDriver.StandDown = true;
            // These are the two native ReconnectionScreen.Leave effects: hide
            // the taskbar, then invoke its original Stop(Left) callback. The
            // LocalGame override and protected post-raid pipeline still run.
            MonoBehaviourSingleton<PreloaderUI>.Instance.SetMenuTaskBarVisibility(false);
            game.CG_method_18();
            Clear(); // Never retry a native continuation automatically.
            if (game.Status == GameStatus.Started)
                throw new InvalidOperationException("the native leave callback did not commit Stop");
            Plugin.ModLogger.LogInfo("Arena leave committed through native Stop(Left); protected raid end follows.");
        }
        catch (Exception ex)
        {
            Clear();
            bool committed = game.Status != GameStatus.Started;
            if (!committed) ArenaRespawnDriver.StandDown = standDown;
            Plugin.ModLogger.LogError($"Arena leave failed {(committed ? "after" : "before")} native commit: {ex}");
            if (!committed)
                Notify("Arena could not leave safely. Please try leaving again.");
        }
    }

    private static bool IsCurrent(LocalGame game, GameWorld world)
        => ArenaSession.IsActive && game != null && world != null
            && ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            && ReferenceEquals(Singleton<GameWorld>.Instance, world)
            && ArenaSession.IsExactActiveRaid(game, world);

    private static void WaitFor(string reason)
    {
        if (_waitingFor == reason) return;
        bool first = _waitingFor == null;
        _waitingFor = reason;
        Plugin.ModLogger.LogInfo($"Arena leave waiting for {reason}; native teardown has not started.");
        if (first) Notify("Leaving Arena as soon as the player is ready and the game is unpaused.");
    }

    private static void Notify(string message)
    {
        try { NotificationManager.DisplayMessageNotification(message); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena leave notification unavailable: {ex.Message}"); }
    }

    private static void Clear()
    {
        _game = null;
        _world = null;
        _profileId = null;
        _waitingFor = null;
    }

    private sealed class LeavePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(BaseLocalGame<EftGamePlayerOwner>), "vmethod_4", Type.EmptyTypes);

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
            => !ArenaSession.IsActive || !Request(__instance as LocalGame);
    }
}
