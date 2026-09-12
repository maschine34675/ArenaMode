using EFT;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>Optional null activation guard for SAIN components attached before Init.</summary>
internal static class ArenaSainDisableSafety
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SainDisableSafety");
    private static PropertyInfo _activation, _owner;
    private static bool _ready, _warned;
    private static int _reports;

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
        if (assembly != null) TryBind(assembly);
    }

    internal static bool TryBind(Assembly assembly)
    {
        try
        {
            if (assembly?.GetName().Name != "SAIN" || assembly.GetName().Version != new System.Version(4, 5, 1, 0))
                throw new InvalidOperationException("expected SAIN 4.5.1");
            var type = assembly.GetType("SAIN.Components.BotComponent", true);
            _activation = type.GetProperty("BotActivation");
            _owner = type.GetProperty("BotOwner");
            var disable = type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var setActive = _activation?.PropertyType.GetMethod("SetActive", new[] { typeof(bool) });
            var stop = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StopAllCoroutines), Type.EmptyTypes);
            if (!typeof(MonoBehaviour).IsAssignableFrom(type) || _owner?.PropertyType != typeof(BotOwner)
                || _activation?.PropertyType.FullName != "SAIN.SAINComponent.Classes.SAINActivationClass"
                || setActive?.ReturnType != typeof(void) || !MatchesDisableBody(disable, _activation.GetMethod, setActive, stop))
                throw new InvalidOperationException("SAIN OnDisable contract changed");
            Patches.Patch(disable, prefix: new HarmonyMethod(typeof(ArenaSainDisableSafety)
                .GetMethod(nameof(BeforeDisable), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
            _ready = true;
            Plugin.ModLogger.LogInfo("Arena: optional SAIN 4.5.1 uninitialized-component teardown safeguard ready.");
            return true;
        }
        catch (Exception ex) { Shutdown(); Warn(ex); return false; }
    }

    // Validate the entire reviewed two-call method, resolving metadata tokens.
    // A signature/version check alone could silently omit new cleanup work in
    // a different build of SAIN. No external method is copied or replaced.
    internal static bool MatchesDisableBody(MethodInfo method, MethodInfo getter, MethodInfo setActive, MethodInfo stop)
    {
        if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0) return false;
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        return il?.Length == 19 && body.ExceptionHandlingClauses.Count == 0 && body.LocalVariables.Count == 0
            && il[0] == 0x02 && il[1] == 0x28 && il[6] == 0x16 && il[7] == 0x6f
            && il[12] == 0x02 && il[13] == 0x28 && il[18] == 0x2a
            && method.Module.ResolveMethod(BitConverter.ToInt32(il, 2)) == getter
            && method.Module.ResolveMethod(BitConverter.ToInt32(il, 8)) == setActive
            && method.Module.ResolveMethod(BitConverter.ToInt32(il, 14)) == stop;
    }

    private static bool BeforeDisable(MonoBehaviour __instance)
    {
        try
        {
            if (!_ready || ReferenceEquals(__instance, null) || _activation.GetValue(__instance) != null) return true;
            // SAIN sets its own BotOwner only in Init. Before Init, use the
            // native owner on the SAME GameObject, stamped during PreActivate.
            var owner = __instance.GetComponent(typeof(BotOwner)) as BotOwner;
            if (ReferenceEquals(owner, null) || !ArenaBotLifecycleScope.Owns(owner)) return true;
            var assigned = _owner.GetValue(__instance) as BotOwner;
            if (!ReferenceEquals(assigned, null) && !ReferenceEquals(assigned, owner)) return true;
            __instance.StopAllCoroutines();
            if (_reports++ < 8)
                Plugin.ModLogger.LogInfo("Arena SAIN teardown: uninitialized Arena component; coroutine cleanup retained.");
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        _activation = _owner = null;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena SAIN disable safeguard unavailable: " + ex.GetBaseException().Message);
    }
}
