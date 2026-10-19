using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>
/// Leave Smoothie's rendering/physics/AI optimizations alone. Only its optional
/// GC-pause routine yields to Arena's collector policy in the exact active raid.
/// No configuration or temporary foreign fields are changed, so no restoration
/// finalizer or cross-raid lease is needed.
/// </summary>
internal static class ArenaSmoothieCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SmoothieCompat");
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static MethodInfo _cleanup;
    private static bool _initialized, _attempted, _failed, _warned, _warnedLayer, _warnedNewer, _warnedUnpatch;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Refresh();
    }

    /// <summary>Refresh optional late plugin discovery; an unavailable optimization adapter never blocks Arena.</summary>
    internal static string GetRejectionReason()
    {
        if (!_initialized) Initialize();
        else Refresh();
        return null;
    }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaSmoothieContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failed = false;
            return;
        }
        if (_failed) return;
        try
        {
            // Chainloader may publish metadata before the optional plugin's
            // Awake has completed. Admission refresh may bind it later.
            if (info?.Instance == null) return;
            if (_plugin != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaSmoothieContract.Normalize(info.Metadata?.Version))
            {
                if (!OwnsPatch()) throw new InvalidOperationException("Smoothie GC compatibility hook was removed");
                return;
            }
            ClearBinding();
            var version = ArenaSmoothieContract.ValidateIdentity(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            var target = ArenaSmoothieContract.ResolveCleanup(info.Instance.GetType());
            var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
            if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
                throw new MissingFieldException("LocalGame._raidSettings changed");
            _attempted = true;
            Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaSmoothieCompat), nameof(BeforeCleanup)) { priority = Priority.First });
            _plugin = info.Instance;
            _version = version;
            _cleanup = target;
            if (!OwnsPatch()) throw new InvalidOperationException("Smoothie GC compatibility hook did not install");
            Plugin.ModLogger.LogDebug("Arena Smoothie: exact-raid optional GC-pause guard and camera-checked pickup layers ready.");
            if (version > ArenaSmoothieContract.ReviewedVersion && !_warnedNewer)
            {
                _warnedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Smoothie " + version
                    + " is newer than reviewed 1.0.8; the GC entry contract matches, but rendering exclusions and native compatibility are unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            _failed = true;
            Warn(ex);
        }
    }

    private static bool OwnsPatch()
        => _cleanup != null && Harmony.GetPatchInfo(_cleanup)?.Prefixes.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaSmoothieCompat).GetMethod(nameof(BeforeCleanup), BindingFlags.Static | BindingFlags.NonPublic)) == true;

    private static bool BeforeCleanup()
    {
        if (_cleanup == null || _failed) return true;
        try
        {
            if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated || !Singleton<IBotGame>.Instantiated
                || Singleton<AbstractGame>.Instance is not LocalGame game) return true;
            var world = Singleton<GameWorld>.Instance;
            bool exactWorld = game != null && world != null && ReferenceEquals(game.GameWorld, world)
                && ReferenceEquals(Singleton<IBotGame>.Instance, game);
            if (!exactWorld) return true;
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            return !ArenaSmoothieScope.SuppressCleanup(exactWorld, ArenaSession.IsExactActiveRaid(game, world),
                game.Status == GameStatus.Started, settings?.playerSide == ESideType.Pmc, world.LocationId, settings?.location);
        }
        catch (Exception ex)
        {
            // Fail open for the foreign routine, fail closed for our authority.
            // Surviving prefixes are inert until restart, including in normal raids.
            _failed = true;
            Warn(ex);
            return true;
        }
    }

    /// <summary>Used only by the privately owned pickup marker tree, never by pooled weapons.</summary>
    internal static int PickupMarkerLayer(Camera camera)
    {
        bool available = _plugin != null && !_failed;
        var optic = CameraManager.Exist ? CameraManager.Instance.OpticCameraManager?.Camera : null;
        int layer = ArenaSmoothieScope.MarkerLayer(available, camera != null, camera != null ? camera.cullingMask : 0,
            optic != null ? optic.cullingMask : (int?)null, LayerMask.NameToLayer("Loot"), LayerMask.NameToLayer("Interactive"));
        if (available && camera != null && layer < 0 && !_warnedLayer)
        {
            _warnedLayer = true;
            Plugin.ModLogger.LogWarning("Arena Smoothie: no excluded pickup layer is visible to the current main/optic cameras; markers retain their ordinary layer. Static-culling exclusion could not be applied.");
        }
        return layer;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Smoothie optional adapter unavailable: " + ex.GetBaseException().Message
            + ". Arena remains available; keep Smoothie's 'Prevent memory cleanup stutters in raids' disabled (its default) to avoid conflicting GC policies. Other Smoothie behavior is unchanged.");
    }

    private static void ClearBinding()
    {
        _cleanup = null; // Make a surviving prefix inert BEFORE unpatching.
        _plugin = null;
        _version = null;
        if (!_attempted) return;
        _attempted = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex)
        {
            if (_warnedUnpatch) return;
            _warnedUnpatch = true;
            Plugin.ModLogger.LogWarning("Arena Smoothie hook cleanup failed; the remaining Arena prefix is inert: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        ClearBinding();
        _initialized = false;
        _failed = false;
    }
}
