using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Areas;

internal sealed class ArenaAreaSpawnPointsPatch : ModulePatch
{
    private static ArenaAreaSpawnPointsPatch _patch;
    internal static bool IsReady { get; private set; }
    internal static void EnablePatch()
    {
        if (IsReady) return;
        try { _patch = new ArenaAreaSpawnPointsPatch(); _patch.Enable(); IsReady = true; }
        catch (Exception ex)
        {
            Shutdown();
            Plugin.ModLogger.LogWarning("Arena area scene-spawn guard unavailable: " + ex.Message);
        }
    }
    internal static void Shutdown() { IsReady = false; _patch?.Disable(); _patch = null; }
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(SpawnPointsCollection), nameof(SpawnPointsCollection.CreateFromScene),
            new[] { typeof(DateTime?), typeof(SpawnPointParams[]) });

    [PatchPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref SpawnPointsCollection __result)
        => __result = ArenaAreaRuntime.FilterCollection(__result);
}
