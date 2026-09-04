using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Combat;

/// <summary>
/// Speeds loose-round magazine loading and unloading for the living local PMC
/// during an active Arena raid.
///
/// SPT 4.1 calculates the vanilla delay from the global base time, Mag Drills
/// skill and the individual magazine modifier before constructing one of these
/// processes. Scaling that final constructor argument preserves all three inputs
/// and keeps the UI event duration synchronized with the process cooldown. No
/// template, skill or global configuration value is changed.
///
/// SPTarkovSpeedLoader demonstrated this user-facing idea on SPT 4.0. Its source
/// is AGPL-3.0 and was not copied; these named 4.1 hooks, gates and timing policy
/// were implemented against the local SPT 4.1 decompile.
/// </summary>
internal static class ArenaMagazinePatches
{
    private const string LoadPatchId = Plugin.PluginGuid + ".Magazine.LoadProcess";
    private const string UnloadPatchId = Plugin.PluginGuid + ".Magazine.UnloadProcess";
    private static readonly LoadProcessPatch LoadPatch = new();
    private static readonly UnloadProcessPatch UnloadPatch = new();
    private static bool _loadPatchAttempted;
    private static bool _unloadPatchAttempted;
    private static bool _enabled;
    private static bool _reportedActive;
    private static bool _reportedFailure;

    internal static void Enable()
    {
        if (_enabled)
            return;

        try
        {
            EnablePatch(LoadPatch, ref _loadPatchAttempted);
            EnablePatch(UnloadPatch, ref _unloadPatchAttempted);

            _reportedActive = false;
            _reportedFailure = false;
            _enabled = true;
        }
        catch
        {
            // A partially installed pair must not survive as a second prefix on
            // a later enable attempt. The inactive gate is published first.
            _enabled = false;
            DisablePatch(UnloadPatch, ref _unloadPatchAttempted);
            DisablePatch(LoadPatch, ref _loadPatchAttempted);
            throw;
        }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        DisablePatch(UnloadPatch, ref _unloadPatchAttempted);
        DisablePatch(LoadPatch, ref _loadPatchAttempted);
        _reportedActive = false;
        _reportedFailure = false;
    }

    private static void EnablePatch(ModulePatch patch, ref bool attempted)
    {
        // A previous Disable can fail after Harmony has already removed the
        // owner but before ModulePatch clears IsActive. Retry owner-scoped
        // cleanup before trusting either lifecycle flag or installing again.
        if (attempted)
        {
            patch.Disable();
            attempted = false;
        }

        if (patch.IsActive)
        {
            attempted = true;
            return;
        }

        // ModulePatch publishes IsActive only after Harmony.Patch, logging and
        // cache registration. Remember the attempt first so a failure in that
        // gap still drives an owner-scoped Disable call.
        attempted = true;
        patch.Enable();
    }

    private static void DisablePatch(ModulePatch patch, ref bool attempted)
    {
        if (!attempted && !patch.IsActive)
            return;

        try
        {
            patch.Disable();
            attempted = false;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning(
                $"Arena magazine patch cleanup failed safely for {patch.GetType().Name}: {ex.Message}");
        }
    }

    private static void ScaleLocalArenaDelay(
        InventoryController inventoryController,
        ref float loadOneAmmoSpeed)
    {
        if (!_enabled)
            return;

        float originalDelay = loadOneAmmoSpeed;
        try
        {
            float multiplier = ArenaMagazineTiming.NormalizeMultiplier(
                Settings.MagazineHandlingSpeed.Value);
            if (!ArenaSession.IsActive
                || multiplier <= ArenaMagazineTiming.MinimumMultiplier
                || !Singleton<GameWorld>.Instantiated)
            {
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world?.MainPlayer;
            if (player == null
                || player.IsAI
                || player.HealthController?.IsAlive != true
                || inventoryController is not Player.PlayerInventoryController localController
                || !ReferenceEquals(localController.Player, player)
                || !ReferenceEquals(player.InventoryController, inventoryController))
            {
                return;
            }

            if (!ArenaMagazineTiming.TryScale(
                    originalDelay,
                    multiplier,
                    out float scaledDelay))
            {
                return;
            }

            loadOneAmmoSpeed = scaledDelay;
            if (!_reportedActive)
            {
                _reportedActive = true;
                Plugin.ModLogger.LogInfo(
                    $"Arena magazine handling speed active: {multiplier:0.##}x for the local PMC.");
            }
        }
        catch (Exception ex)
        {
            // Publish the scaled value only after every gate and calculation has
            // succeeded. A drifted runtime therefore retains the exact vanilla
            // delay instead of leaving a partially accelerated process behind.
            loadOneAmmoSpeed = originalDelay;
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena magazine handling speed failed safely; vanilla timing retained: {ex.Message}");
            }
        }
    }

    private sealed class LoadProcessPatch : ModulePatch
    {
        internal LoadProcessPatch() : base(LoadPatchId) { }

        protected override MethodBase GetTargetMethod()
            => AccessTools.Constructor(
                typeof(Player.PlayerInventoryController.LoadMagazineProcess),
                new[]
                {
                    typeof(InventoryController),
                    typeof(Magazine),
                    typeof(Ammo),
                    typeof(int),
                    typeof(bool),
                    typeof(float),
                });

        [PatchPrefix]
        private static void Prefix(
            InventoryController inventoryController,
            ref float loadOneAmmoSpeed)
            => ScaleLocalArenaDelay(inventoryController, ref loadOneAmmoSpeed);
    }

    private sealed class UnloadProcessPatch : ModulePatch
    {
        internal UnloadProcessPatch() : base(UnloadPatchId) { }

        protected override MethodBase GetTargetMethod()
            => AccessTools.Constructor(
                typeof(Player.PlayerInventoryController.UnloadMagazineProcess),
                new[]
                {
                    typeof(InventoryController),
                    typeof(Magazine),
                    typeof(float),
                    typeof(bool),
                });

        [PatchPrefix]
        private static void Prefix(
            InventoryController inventoryController,
            ref float loadOneAmmoSpeed)
            => ScaleLocalArenaDelay(inventoryController, ref loadOneAmmoSpeed);
    }
}
