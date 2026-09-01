using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>
/// EFT's RaidSettings.Clone omits transitionType, while Apply copies it. PFT's
/// synthetic local-ready route makes vanilla apply that clone immediately before
/// starting, turning Arena's Common into the clone's default None. Repair only
/// that proven synchronous Common-to-None rewrite on the exact selected object.
/// </summary>
internal sealed class ArenaRaidSettingsApplyPatch : ModulePatch
{
    public static void EnablePatch() => new ArenaRaidSettingsApplyPatch().Enable();

    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(RaidSettings), nameof(RaidSettings.Apply),
            new[] { typeof(RaidSettings) });

    [PatchPrefix]
    private static void Prefix(RaidSettings __instance, out ELocationTransition __state)
        => __state = __instance?.transitionType ?? ELocationTransition.None;

    [PatchPostfix]
    private static void Postfix(
        RaidSettings __instance,
        RaidSettings __0,
        ELocationTransition __state)
    {
        if (ArenaMenuSelection.RestoreArenaTransitionAfterCloneApply(
                __instance, __0, __state))
        {
            Plugin.ModLogger?.LogInfo(
                "Arena preserved the Common transition after EFT applied its local-ready settings clone.");
        }
    }
}
