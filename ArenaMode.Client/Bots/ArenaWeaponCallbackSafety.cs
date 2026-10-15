using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArenaMode.Bots;

/// <summary>Complete a retired Arena selector without reviving its disposed services.</summary>
internal static class ArenaWeaponCallbackSafety
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".WeaponCallback");
    private static ConditionalWeakTable<BotWeaponSelector, object> _logged = new();
    private static bool _ready, _warned;

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            var target = typeof(BotWeaponSelector).GetMethod(nameof(BotWeaponSelector.OnWeaponTaken),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null, new[] { typeof(Result<IHandsController>) }, null);
            if (target == null || target.ReturnType != typeof(void)) throw new MissingMethodException("BotWeaponSelector.OnWeaponTaken");
            Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaWeaponCallbackSafety)
                .GetMethod(nameof(BeforeTaken), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
            _ready = true;
            Plugin.ModLogger.LogDebug("Arena: retired bot weapon callback safeguard ready.");
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }

    internal static void Shutdown()
    {
        _ready = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex) { Warn(ex); }
        finally { _logged = new(); }
    }

    private static bool BeforeTaken(BotWeaponSelector __instance)
    {
        try
        {
            var owner = __instance?._owner;
            if (!_ready || ReferenceEquals(owner, null) || !ArenaBotLifecycleScope.Owns(owner)) return true;
            var manager = owner.WeaponManager;
            bool retired = owner.IsDead || owner.BotState == EBotState.Disposed
                || (manager != null && ReferenceEquals(manager._owner, owner) && manager._disposed);
            // NonActive alone also describes temporary native Disable/Deactivate.
            // A failed Result on a retired owner would schedule another task too.
            if (!retired) return true;
            __instance.IsChanging = false; // Preserve the native completion flag only.
            if (!_logged.TryGetValue(__instance, out _))
            {
                _logged.Add(__instance, new object());
                Plugin.ModLogger.LogDebug($"Arena bot weapon callback: ignored retired selector; state={owner.BotState}, dead={owner.IsDead}, managerDisposed={manager?._disposed}.");
            }
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; } // Native failures remain visible.
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena weapon callback safeguard unavailable: " + ex.GetBaseException().Message);
    }
}
