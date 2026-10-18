using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Movement;

/// <summary>
/// Raises the local PMC's per-instance carry-weight thresholds during Arena.
/// GlobalConfiguration is deliberately untouched because bots share it.
///
/// SPT 4.1 rebuilds all five limits in PhysicalBase.UpdateWeightLimits whenever
/// strength, health or encumbrance state changes. A postfix therefore scales a
/// fresh vanilla result every time and cannot compound. MovementRuntime invokes
/// OnWeightLimitsUpdated once when Arena becomes active, when the setting changes,
/// and again after the gate closes so the same body immediately returns to vanilla.
/// </summary>
internal static class ArenaWeightPatches
{
    public static void Enable() => new ArenaWeightLimitsPatch().Enable();

    private static bool IsLocalArenaPhysical(PhysicalBase physical)
    {
        if (!ArenaSession.IsActive
            || !Settings.MovementEnabled.Value
            || physical == null
            || physical.EncumberDisabled
            || !Singleton<GameWorld>.Instantiated)
        {
            return false;
        }

        try
        {
            Player mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
            return mainPlayer != null
                   && !mainPlayer.IsAI
                   && ReferenceEquals(mainPlayer.Physical, physical);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class ArenaWeightLimitsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(PhysicalBase), nameof(PhysicalBase.UpdateWeightLimits));

        [PatchPostfix]
        private static void Postfix(PhysicalBase __instance)
        {
            if (!IsLocalArenaPhysical(__instance))
                return;

            float multiplier = ArenaWeightTuning.BoundMultiplier(
                Settings.CarryWeightLimitMultiplier.Value);
            if (multiplier <= ArenaWeightTuning.MinimumMultiplier)
                return;

            __instance.WalkOverweightLimits = ArenaWeightTuning.ScaleLimits(
                __instance.WalkOverweightLimits, multiplier);
            __instance.BaseOverweightLimits = ArenaWeightTuning.ScaleLimits(
                __instance.BaseOverweightLimits, multiplier);
            __instance.SprintOverweightLimits = ArenaWeightTuning.ScaleLimits(
                __instance.SprintOverweightLimits, multiplier);
            __instance.WalkSpeedOverweightLimits = ArenaWeightTuning.ScaleLimits(
                __instance.WalkSpeedOverweightLimits, multiplier);
            __instance.BaseInertiaLimits = ArenaWeightTuning.ScaleInertiaLimits(
                __instance.BaseInertiaLimits, multiplier);
        }
    }
}
