using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Pickups;

internal sealed class ArenaPickupExaminationPatch : ModulePatch
{
    private static bool _warned;

    internal static void EnablePatch()
    {
        try { new ArenaPickupExaminationPatch().Enable(); }
        catch (Exception ex) { Warn(ex); }
    }

    protected override MethodBase GetTargetMethod()
        => AccessTools.DeclaredMethod(typeof(InventoryController), nameof(InventoryController.Examined), new[] { typeof(Item) });

    [PatchPostfix]
    private static void Postfix(InventoryController __instance, Item item, ref bool __result)
    {
        try { ArenaPickupExamination.Apply(__instance, item, ref __result); }
        catch (Exception ex) { Warn(ex); } // Keep the native result on a failed scope check.
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena pickup examination unavailable; native result retained: " + ex.GetBaseException().Message);
    }
}
