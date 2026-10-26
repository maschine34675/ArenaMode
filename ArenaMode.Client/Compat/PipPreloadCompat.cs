using System;
using System.Reflection;
using System.Linq;
using ArenaMode.Kits;
using BepInEx.Bootstrap;
using EFT.CameraControl;
using HarmonyLib;

namespace ArenaMode.Compat;

/// <summary>Only Arena's explicitly withheld cold-pool role models. Runtime body ownership lives in Core.</summary>
internal static class PipPreloadCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".PipPreload");
    private static object _installed;
    private static MethodInfo[] _methods;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue("com.fiodor.pipdisabler", out var info)) return null;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (!ReferenceEquals(_installed, info.Instance))
            {
                Patches.UnpatchSelf();
                _installed = null;
                var scope = info.Instance.GetType().Assembly.GetType("PiPDisabler.ScopeLifecycle", true);
                _methods = new[]
                {
                    Find(scope, "OnOpticEnabled", typeof(OpticSight)),
                    Find(scope, "OnOpticDisabled", typeof(OpticSight)),
                    Find(scope, "RestoreBypassedOpticState", typeof(OpticSight), typeof(string), typeof(bool))
                };
                foreach (var method in _methods)
                    Patches.Patch(method, prefix: new HarmonyMethod(typeof(PipPreloadCompat), nameof(BeforeCallback)) { priority = Priority.First });
                _installed = info.Instance;
            }
            foreach (var method in _methods)
                if (Harmony.GetPatchInfo(method)?.Owners.Contains(Patches.Id) != true)
                    throw new InvalidOperationException("preload callback guard was removed");
            return null;
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            _installed = null;
            return "PiP Disabler Arena preload integration is unavailable: " + ex.GetBaseException().Message + ".";
        }
    }

    private static MethodInfo Find(Type type, string name, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static bool BeforeCallback(OpticSight __0) => !KitOpticPreloadGuard.IsWithheldCallback(__0);
}
