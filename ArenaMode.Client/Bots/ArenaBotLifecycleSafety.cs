using EFT;
using HarmonyLib;
using System;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Preserve the native enemy-list algorithm, including its null end marker.
/// SetInfo has no capacity check; Remove reads one slot beyond the used range.
/// Resize this controller's array BEFORE either native mutation, never catch
/// and retry a half-completed native SetInfo/Remove or silently discard enemies.
/// </summary>
internal static class ArenaBotLifecycleSafety
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".BotLifecycleSafety");
    private static bool _ready, _warned;

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            Bind(typeof(BotOwner), nameof(BotOwner.PreActivate), nameof(ObserveOwner));
            Bind(typeof(BotEnemiesController), nameof(BotEnemiesController.SetInfo), nameof(BeforeAdd));
            Bind(typeof(BotEnemiesController), nameof(BotEnemiesController.Remove), nameof(BeforeRemove));
            _ready = true;
        }
        catch (Exception exception)
        {
            Shutdown();
            Warn(exception);
        }
    }

    private static void Bind(Type type, string method, string prefix)
    {
        var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        if (target == null || target.ReturnType != typeof(void))
            throw new InvalidOperationException("native bot lifecycle contract changed: " + method);
        Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaBotLifecycleSafety)
            .GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First });
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        ArenaBotLifecycleScope.Clear();
    }

    private static void ObserveOwner(BotOwner __instance)
    {
        try { ArenaBotLifecycleScope.Owns(__instance); }
        catch (Exception exception) { Warn(exception); }
    }

    private static void BeforeAdd(BotEnemiesController __instance) => EnsureRoom(__instance, 2);
    private static void BeforeRemove(BotEnemiesController __instance) => EnsureRoom(__instance, 1);

    private static void EnsureRoom(BotEnemiesController controller, int extra)
    {
        try
        {
            var owner = controller?._owner;
            if (ReferenceEquals(owner, null) || !ReferenceEquals(owner.EnemiesController, controller)
                || !ArenaBotLifecycleScope.Owns(owner)) return;
            var entries = controller.SortedInfos;
            int count = controller._countEnemies;
            // Bad bookkeeping is a different fault, not permission to allocate
            // from an arbitrary count or reconstruct/dispose foreign entries.
            if (entries == null || count < 0 || count > entries.Length)
                throw new InvalidOperationException("invalid native enemy array/count");
            int required = checked(count + extra);
            if (required <= entries.Length) return;
            int capacity = checked((int)Math.Max(required, (long)entries.Length * 2));
            Array.Resize(ref controller.SortedInfos, capacity);
            if (ArenaBotLifecycleScope.LogGrowth(owner))
                Plugin.ModLogger.LogDebug($"Arena enemy capacity: {entries.Length} -> {capacity} (used {count}, reserve {extra}).");
        }
        catch (Exception exception) { Warn(exception); } // Original still runs; unrelated failures stay visible.
    }

    private static void Warn(Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot lifecycle safeguard unavailable: " + exception.GetBaseException().Message);
    }
}
