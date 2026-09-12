using EFT;
using HarmonyLib;
using System;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>Cancel an uncommitted native pickup before its bot services disappear.</summary>
internal static class ArenaPickupTeardownSafety
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".PickupTeardown");
    private static bool _ready, _warned;

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            var dispose = typeof(BotOwner).GetMethod(nameof(BotOwner.Dispose), Type.EmptyTypes);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException("BotOwner.Dispose contract changed");
            Patches.Patch(dispose, prefix: new HarmonyMethod(typeof(ArenaPickupTeardownSafety)
                .GetMethod(nameof(BeforeDispose), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
            _ready = true;
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }

    internal static void Shutdown() { _ready = false; Patches.UnpatchSelf(); }

    private static void BeforeDispose(BotOwner __instance)
    {
        try
        {
            // Native Dispose is a no-op for PreActive. Do not cancel work on
            // that path, or touch any different body/foreign callback.
            if (__instance == null || __instance.BotState == EBotState.PreActive
                || !ArenaBotLifecycleScope.Owns(__instance)) return;
            var player = __instance.GetPlayer;
            var context = player?.MovementContext;
            var action = context?.PickupAction;
            if (action == null || action.GetInvocationList().Length != 1
                || action.Target is not BotItemTaker.CG_PickupAction pending
                || !ReferenceEquals(pending.owner, player)
                || !ReferenceEquals(pending.botItemTaker_0, __instance.ItemTaker)
                || !ReferenceEquals(pending.botItemTaker_0?._owner, __instance)
                || action != new Action(pending.method_0)) return;

            // Both native callers only simulated inventory placement so far.
            // The animator normally clears this slot before invoking the action;
            // after Dispose its Medecine delegate receiver would already be null.
            // Do not invoke completion, move items, or alter the native disposal.
            context.PickupAction = null;
            Plugin.ModLogger.LogInfo("Arena bot teardown: cancelled pending native pickup before disposal.");
        }
        catch (Exception ex) { Warn(ex); } // Original Dispose always runs.
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena pickup teardown safeguard unavailable: " + ex.GetBaseException().Message);
    }
}
