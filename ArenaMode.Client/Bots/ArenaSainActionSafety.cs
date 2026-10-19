using EFT;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Optional, separate from personality selection. Missing SAIN components have
/// no movement/cover state to stop. Let BigBrain finish its normal transition
/// instead of repeatedly throwing inside the two observed SAIN 4.5.1 actions.
/// </summary>
internal static class ArenaSainActionSafety
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SainActionSafety");
    private static PropertyInfo _owner, _managerInstance;
    private static FieldInfo _cachedBot;
    private static MethodInfo _getSain;
    private static Type _component;
    private static bool _ready, _warned;

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
            ArenaSainVersion.Require(assembly);
            var action = assembly.GetType("SAIN.Layers.BotAction", true);
            _component = assembly.GetType("SAIN.Components.BotComponent", true);
            _owner = action.GetProperty("BotOwner");
            _cachedBot = action.GetField("_bot", BindingFlags.Instance | BindingFlags.NonPublic);
            var manager = assembly.GetType("SAIN.Components.BotManagerComponent", true);
            _managerInstance = manager.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _getSain = manager.GetMethod("GetSAIN", new[] { typeof(BotOwner), _component.MakeByRefType() });
            if (_owner?.PropertyType != typeof(BotOwner) || _cachedBot?.FieldType != _component
                || !typeof(Component).IsAssignableFrom(_component) || !typeof(Component).IsAssignableFrom(manager)
                || _managerInstance?.PropertyType != manager || _getSain?.ReturnType != typeof(bool))
                throw new InvalidOperationException("SAIN action/component lookup contract changed");

            foreach (var name in new[] { "SAIN.Layers.Combat.Solo.Cover.SeekCoverAction", "SAIN.Layers.Combat.Solo.DogFightAction" })
            {
                var type = assembly.GetType(name, true);
                var stop = type.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (!action.IsAssignableFrom(type) || stop?.ReturnType != typeof(void))
                    throw new InvalidOperationException("SAIN action Stop contract changed");
                Patches.Patch(stop, prefix: new HarmonyMethod(typeof(ArenaSainActionSafety)
                    .GetMethod(nameof(BeforeStop), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
            }
            _ready = true;
            Plugin.ModLogger.LogDebug($"Arena: optional SAIN {assembly.GetName().Version} retired-action safeguard ready.");
            return true;
        }
        catch (Exception exception)
        {
            Shutdown();
            Warn(exception);
            return false;
        }
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        _owner = _managerInstance = null;
        _cachedBot = null;
        _getSain = null;
        _component = null;
    }

    private static bool BeforeStop(object __instance)
    {
        try
        {
            if (!_ready) return true;
            var owner = _owner.GetValue(__instance) as BotOwner;
            if (!ArenaBotLifecycleScope.Owns(owner)) return true;
            // Do not invoke BotAction.Bot: it can itself dereference a missing
            // manager or EnemyController. Check all its lookup sources safely.
            if (_cachedBot.GetValue(__instance) is UnityEngine.Object cached && cached != null) return true;
            if (owner != null && owner.GetComponent(_component) != null) return true;
            var manager = _managerInstance.GetValue(null) as UnityEngine.Object;
            // The original calls this managed lookup even on a Unity-destroyed
            // manager wrapper; it may still hold a live registry component.
            if (!ReferenceEquals(manager, null))
            {
                object[] arguments = { owner, null };
                _getSain.Invoke(manager, arguments);
                if (arguments[1] is UnityEngine.Object registered && registered != null) return true;
            }
            if (ArenaBotLifecycleScope.LogStop(owner))
                Plugin.ModLogger.LogDebug("Arena SAIN action teardown: missing component; retired Stop completed without movement access.");
            return false;
        }
        catch (Exception exception)
        {
            Warn(exception);
            return true; // Unknown lookup failure is NOT proof that live cleanup can be skipped.
        }
    }

    private static void Warn(Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena SAIN action safeguard unavailable: " + exception.GetBaseException().Message);
    }
}
