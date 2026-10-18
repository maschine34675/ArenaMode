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
/// Keeps leg injuries meaningful while softening their severe walking penalty
/// for the local PMC in Arena raids.
///
/// SPT 4.1 funnels both fractured and destroyed legs through
/// Player.UpdateSpeedLimitByHealth. Without painkillers it adds the sole
/// HealthCondition speed limit: 0.3 for one damaged leg and 0.2 for both. This
/// prefix changes only that value and leaves the physical damaged-leg flags,
/// fractures, destroyed body parts, sprint lock and self-damage untouched.
/// </summary>
internal static class ArenaLegInjuryPatches
{
    public static void Enable() => new ArenaLegInjuryHealthConditionSpeedPatch().Enable();

    private static bool IsLocalArenaContext(MovementContext context)
    {
        if (!ArenaSession.IsActive || context == null || !Singleton<GameWorld>.Instantiated)
            return false;

        Player mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
        return mainPlayer != null
               && !mainPlayer.IsAI
               && ReferenceEquals(mainPlayer.MovementContext, context);
    }

    private sealed class ArenaLegInjuryHealthConditionSpeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(MovementContext),
                nameof(MovementContext.AddStateSpeedLimit),
                new[] { typeof(float), typeof(Player.ESpeedLimit) });

        [PatchPrefix]
        private static void Prefix(
            MovementContext __instance,
            ref float speedLimit,
            Player.ESpeedLimit cause)
        {
            if (cause != Player.ESpeedLimit.HealthCondition
                || Settings.LegInjurySlowdownReductionPercent.Value <= 0
                || !IsLocalArenaContext(__instance))
            {
                return;
            }

            speedLimit = ArenaLegInjuryTuning.ReducePenalty(
                speedLimit,
                Settings.LegInjurySlowdownReductionPercent.Value);
        }
    }
}
