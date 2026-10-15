using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using GPUInstancer;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// Two narrow lifecycle repairs observed during GPU acceptance. Neither changes
/// ordinary raids, graphics preferences, PFT squad state or the native menu flow.
/// </summary>
internal static class ArenaGpuAcceptanceCompat
{
    private static readonly Harmony Patches = new Harmony(Plugin.PluginGuid + ".GpuAcceptanceCompat");
    private static bool _enabled;
    private static WeakReference<Camera> _lastReboundCamera;
    private static bool _reportedInactiveMenu, _reportedRenderFailure;

    internal static void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        try
        {
            // Update normally reacquires a destroyed camera only AFTER its
            // motion-vector size check. Bind first so that SAME native check
            // sees the replacement SSAA rather than last life's texture size.
            MethodInfo update = ExactMethod(typeof(GPUInstancerManager), "Update", true);
            ExactMethod(typeof(GPUInstancerManager), "InitializeCameraData", true);
            Patches.Patch(update, prefix: new HarmonyMethod(typeof(ArenaGpuAcceptanceCompat), nameof(BeforeGpuUpdate))
                { priority = Priority.Last });
            Plugin.ModLogger.LogDebug("Arena GPU acceptance: replacement grass-camera ordering guard ready.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("Arena replacement grass-camera guard unavailable: " + ex.GetBaseException().Message);
        }

        // This is optional and independent of protected PFT team integration.
        // Resolve only the installed assembly's exact small scheduling method;
        // no source is copied and no PFT private state is changed.
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "pitFireTeam");
        if (assembly == null) return;
        try
        {
            Type type = assembly.GetType("pitTeam.Components.SquadControlMenuUi", false);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                throw new MissingMemberException("PFT SquadControlMenuUi host changed");
            MethodInfo warmup = ExactMethod(type, "QueueRaidSettingsWarmup", false);
            Patches.Patch(warmup, prefix: new HarmonyMethod(typeof(ArenaGpuAcceptanceCompat), nameof(BeforePftWarmup))
                { priority = Priority.Last });
            Plugin.ModLogger.LogDebug("Arena GPU acceptance: optional PFT inactive-menu warmup guard ready.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("Arena optional PFT menu guard unavailable: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        Patches.UnpatchSelf();
        _enabled = false;
        _lastReboundCamera = null;
        _reportedInactiveMenu = _reportedRenderFailure = false;
    }

    private static MethodInfo ExactMethod(Type type, string name, bool isPublic)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.DeclaredOnly
            | (isPublic ? BindingFlags.Public : BindingFlags.NonPublic), null, Type.EmptyTypes, null);
        if (method == null || method.ReturnType != typeof(void) || method.IsStatic || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name + "(): void");
        return method;
    }

    private static bool TryCurrentRaid(out GameWorld world)
    {
        world = null;
        if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame game || game.Status != GameStatus.Started)
            return false;
        world = Singleton<GameWorld>.Instance;
        return world != null && ReferenceEquals(game.GameWorld, world)
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    internal static void BeforeGpuUpdate(GPUInstancerManager __instance)
    {
        if (__instance == null || __instance.IsOptic || !TryCurrentRaid(out var world)) return;
        Camera previous = __instance.Camera;
        // Unity null means the previously registered native camera was destroyed.
        // A never-bound manager and a still-live foreign camera keep vanilla flow.
        if (ReferenceEquals(previous, null) || previous != null) return;
        if (!CameraManager.Exist || !Singleton<PlayerCameraController>.Instantiated
            || world.MainPlayer == null || !ReferenceEquals(world.MainPlayer, Singleton<PlayerCameraController>.Instance.Player))
            return;
        Camera current = CameraManager.Instance.Camera;
        if (current == null || !current.isActiveAndEnabled) return;
        try
        {
            __instance.InitializeCameraData();
            // Do not clear the shared command buffer here: several managers
            // append their own grass/tree draws during the same frame. Native
            // Update owns its single clear, resolution check and draw ordering.
            if (ReferenceEquals(__instance.Camera, current)
                && (_lastReboundCamera == null || !_lastReboundCamera.TryGetTarget(out var last)
                    || !ReferenceEquals(last, current)))
            {
                _lastReboundCamera = new WeakReference<Camera>(current);
                Plugin.ModLogger.LogDebug("Arena grass renderer: rebound replacement camera before native resolution validation.");
            }
        }
        catch (Exception ex)
        {
            if (_reportedRenderFailure) return;
            _reportedRenderFailure = true;
            Plugin.ModLogger.LogWarning("Arena grass-camera preflight failed; native update remains enabled: " + ex.GetBaseException().Message);
        }
    }

    internal static bool BeforePftWarmup(object __instance)
    {
        if (!TryCurrentRaid(out _) || __instance is not MonoBehaviour host || host == null || host.isActiveAndEnabled)
            return true;
        // Native OnShowStart calls the PFT-patched menu visibility methods while
        // the menu is still hidden. PFT's later Show postfix retries this warmup
        // after ShowGameObject; opening settings also builds them synchronously.
        // Leave the pending-coroutine field untouched so that retry is possible.
        if (!_reportedInactiveMenu)
        {
            _reportedInactiveMenu = true;
            Plugin.ModLogger.LogDebug("Arena PFT menu: deferred settings warmup until the menu host is active.");
        }
        return false;
    }
}
