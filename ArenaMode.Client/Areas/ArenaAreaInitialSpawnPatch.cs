using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ArenaMode.Areas;

/// <summary>Only the exact loading PMC/collection may replace native initial selection.</summary>
internal sealed class ArenaAreaInitialSpawnPatch : ModulePatch
{
    private static ArenaAreaInitialSpawnPatch _patch;
    internal static bool IsReady { get; private set; }
    internal static void EnablePatch()
    {
        try { _patch = new ArenaAreaInitialSpawnPatch(); _patch.Enable(); IsReady = true; }
        catch (Exception ex) { Shutdown(); Plugin.ModLogger.LogWarning("Arena team initial spawn hook unavailable: " + ex.Message); }
    }
    internal static void Shutdown() { IsReady = false; _patch?.Disable(); _patch = null; }
    protected override MethodBase GetTargetMethod()
    {
        var map = typeof(SpawnSystem).GetInterfaceMap(typeof(ISpawnSystem));
        int index = Array.FindIndex(map.InterfaceMethods, m => m.Name == nameof(ISpawnSystem.SelectSpawnPoint));
        if (index < 0) throw new MissingMethodException("Native spawn selection contract changed.");
        return map.TargetMethods[index];
    }
    [PatchPrefix, HarmonyPriority(Priority.Last)]
    private static bool Prefix(SpawnSystem __instance, ESpawnCategory category, EPlayerSide side,
        string profileId, ref ISpawnPoint __result)
    {
        if (!ArenaAreaRuntime.TryInitialTeamSpawn(__instance, category, side, profileId, out var point)) return true;
        __result = point;
        return false;
    }
}
