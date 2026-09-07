using EFT;
using EFT.Bots;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace ArenaMode.Areas;

/// <summary>Observe the later scene/player loading task, not just the earlier HTTP start.</summary>
internal static class ArenaAreaLoadPatch
{
    private static readonly Harmony Patches = new Harmony(Plugin.PluginGuid + ".AreaLoad");
    internal static bool IsReady { get; private set; }
    internal static void Enable()
    {
        try
        {
            var target = AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "Run",
                new[] { typeof(BotControllerSettings), typeof(string), typeof(InventoryController) });
            if (target == null || target.ReturnType != typeof(Task))
                throw new MissingMethodException("BaseLocalGame.Run load task contract changed");
            Patches.Patch(target,
                prefix: new HarmonyMethod(typeof(ArenaAreaLoadPatch), nameof(Before)),
                postfix: new HarmonyMethod(typeof(ArenaAreaLoadPatch), nameof(After)),
                finalizer: new HarmonyMethod(typeof(ArenaAreaLoadPatch), nameof(FinalizeLoad)));
            IsReady = true;
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            Plugin.ModLogger.LogWarning("Arena area loading guard unavailable: " + ex.Message);
        }
    }
    internal static void Shutdown() { IsReady = false; Patches.UnpatchSelf(); }
    private static void Before(BaseLocalGame<EftGamePlayerOwner> __instance, out ArenaAreaRuntime.LoadingLease __state)
        => __state = ArenaAreaRuntime.CaptureLoading(__instance as LocalGame);
    private static void After(ArenaAreaRuntime.LoadingLease __state, ref Task __result)
    { if (__state != null && __result != null) __result = Observe(__result, __state); }
    private static Exception FinalizeLoad(Exception __exception, ArenaAreaRuntime.LoadingLease __state)
    {
        if (__exception != null) ArenaAreaRuntime.LoadingFailed(__state, __exception);
        return __exception;
    }
    private static async Task Observe(Task original, ArenaAreaRuntime.LoadingLease lease)
    {
        try { await original; }
        catch (Exception ex) { ArenaAreaRuntime.LoadingFailed(lease, ex); throw; }
    }
}
