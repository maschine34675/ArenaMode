using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Arena movement overhaul — all per-player (never GlobalConfiguration/EFTHardSettings:
/// bots share those), every callback leads with the IsActive+IsYourPlayer gate and
/// fails open to vanilla. Hooks proven by CombatSlide/ClassicMovement/FieldKit analysis:
/// speed ceilings are property getters, inertia is SIX getters (not one field),
/// acceleration is a separate ManualUpdate ramp, lean has a legacy fast path shipped
/// in the binary, and foliage slowdown lives in the obstacle list.
/// </summary>
internal static class ArenaMovementPatches
{
    private static readonly AccessTools.FieldRef<MovementContext, Player> PlayerField =
        AccessTools.FieldRefAccess<MovementContext, Player>("_player");

    private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, float> AimingSpeedField =
        AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_aimingSpeed");

    private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, Player.ValueBlender> AimSwayBlenderField =
        AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Player.ValueBlender>("_aimSwayBlender");

    private static readonly FieldInfo FirearmAnimationDataField =
        AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmAnimationData");

    public static void Enable()
    {
        new MaxSpeedPatch().Enable();
        new SprintSpeedPatch().Enable();
        new MotionScalePatch().Enable();
        new AdsSlowdownPatch().Enable();
        new AdsTransitionPatch().Enable();
        new InertiaGetterPatch("Inertia").Enable();
        new InertiaGetterPatch("MoveSideInertia").Enable();
        new InertiaGetterPatch("MoveDiagonalInertia").Enable();
        new InertiaGetterPatch("PoseInertia").Enable();
        new InertiaGetterPatch("WalkInertia").Enable();
        new InertiaGetterPatch("SprintBrakeInertia").Enable();
        new InstantLeanPatch().Enable();
        new BushSlowdownPatch().Enable();
        new JumpHeightPatch().Enable();
        new FallDamagePatch().Enable();
    }

    private static bool IsArenaMovement(MovementContext context)
    {
        if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || context == null)
            return false;

        try
        {
            var player = PlayerField(context);
            return player != null && player.IsYourPlayer;
        }
        catch
        {
            return false;
        }
    }

    private sealed class MaxSpeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.MaxSpeed));

        [PatchPostfix]
        private static void Postfix(MovementContext __instance, ref float __result)
        {
            if (IsArenaMovement(__instance))
                __result *= Settings.WalkSpeedMult.Value;
        }
    }

    private sealed class SprintSpeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.SprintingSpeed));

        [PatchPostfix]
        private static void Postfix(MovementContext __instance, ref float __result)
        {
            if (IsArenaMovement(__instance))
                __result *= Settings.SprintSpeedMult.Value;
        }
    }

    /// <summary>
    /// Displacement beyond what the animator blend tree expresses (CombatSlide's
    /// proven hook incl. the SpeedLimit bypass). Leaves motion.y alone so gravity
    /// and slope projection keep working.
    /// </summary>
    private sealed class MotionScalePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));

        // Priority.Last on BOTH callbacks: CombatSlide brackets the same method with
        // the same save/set(-1)/restore pattern, and Harmony runs postfixes in the
        // SAME priority order as prefixes (not reversed). Running strictly inside
        // CombatSlide's bracket plus the skip-when-already-bypassed guard keeps the
        // two restores from stranding the controller cap at -1.
        [PatchPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(MovementContext __instance, ref Vector3 motion, out float __state)
        {
            __state = float.NaN;
            var scale = Settings.ExtraMotionScale.Value;
            if (scale <= 1f || !IsArenaMovement(__instance))
                return;

            motion.x *= scale;
            motion.z *= scale;

            var controller = __instance.CharacterController;
            if (controller != null && controller.SpeedLimit >= 0f)
            {
                __state = controller.SpeedLimit;
                controller.SpeedLimit = -1f;
            }
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MovementContext __instance, float __state)
        {
            if (float.IsNaN(__state))
                return;

            var controller = __instance.CharacterController;
            if (controller != null)
                controller.SpeedLimit = __state;
        }
    }

    private sealed class AdsSlowdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.SetAimingSlowdown));

        [PatchPrefix]
        private static void Prefix(MovementContext __instance, ref float slow)
        {
            if (IsArenaMovement(__instance))
                slow = Mathf.Lerp(slow, 1f, Settings.AdsMoveFactor.Value);
        }
    }

    private sealed class AdsTransitionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateWeaponVariables));

        [PatchPostfix]
        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value)
                return;

            try
            {
                var mainPlayer = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (mainPlayer == null || !ReferenceEquals(mainPlayer.ProceduralWeaponAnimation, __instance))
                    return;

                // The game only reassigns _aimingSpeed when firearm data is present —
                // multiplying on other invocations (grenade/melee/empty hands) would
                // compound the factor call after call.
                if (FirearmAnimationDataField?.GetValue(__instance) == null)
                    return;

                ref var aimingSpeed = ref AimingSpeedField(__instance);
                aimingSpeed *= Settings.AdsTransitionMult.Value;

                var blender = AimSwayBlenderField(__instance);
                if (blender != null && aimingSpeed > 0f)
                    blender.Speed = __instance.SwayFalloff / aimingSpeed;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena ADS transition patch failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Inertia is SIX MovementContext getters, not one value — patching the getters
    /// wins over every weight-driven recompute because all consumers read through them.
    /// </summary>
    private sealed class InertiaGetterPatch : ModulePatch
    {
        private readonly string _property;

        public InertiaGetterPatch(string property)
        {
            _property = property;
        }

        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertyGetter(typeof(MovementContext), _property);

        [PatchPostfix]
        private static void Postfix(MovementContext __instance, ref float __result)
        {
            if (Settings.NoInertia.Value && IsArenaMovement(__instance))
                __result = 0f;
        }
    }

    /// <summary>BSG left the pre-inertia fast lean in the binary — redirect to it.</summary>
    private sealed class InstantLeanPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.InertiaSmoothTilt));

        [PatchPrefix]
        private static bool Prefix(MovementContext __instance, float smoothDiff, float deltaTime)
        {
            if (!Settings.InstantLean.Value || !IsArenaMovement(__instance))
                return true;

            __instance.LegacySmoothTilt(smoothDiff, deltaTime);
            return false;
        }
    }

    // NOTE: no separate acceleration patch. The reviewed ClassicMovement hook
    // (ManualUpdate + CHARACTER_SPEED_CHANGING_SPEED) has ZERO consumers in the 4.1
    // client — the real ramp is MovePlayerState.UpdateMovementDirection's SmoothDamp,
    // whose time constant is WalkInertia: the NoInertia getter patch already makes
    // ramp-up effectively instant. An extra lerp here would fight the state machine
    // (walk-bob while standing still, defeated landing deceleration).

    /// <summary>Foliage slowdown lives in the obstacle list, not in any speed getter.</summary>
    private sealed class BushSlowdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.OnEnterObstacle));

        [PatchPrefix]
        private static bool Prefix(MovementContext __instance, ObstacleCollider obstacle)
        {
            if (!Settings.NoBushSlowdown.Value || obstacle == null || !obstacle.HasSwampSpeedLimit)
                return true;

            return !IsArenaMovement(__instance);
        }
    }

    /// <summary>
    /// sqrt so the config value is a true HEIGHT multiplier (apex scales with v²).
    /// </summary>
    private sealed class JumpHeightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter));

        [PatchPostfix]
        private static void Postfix(JumpPlayerState __instance)
        {
            var mult = Settings.JumpHeightMult.Value;
            if (mult <= 1f || !IsArenaMovement(__instance.MovementContext))
                return;

            __instance._liftForce *= Mathf.Sqrt(mult);
        }
    }

    private sealed class FallDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.HandleFall));

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, ref float __result)
        {
            if (!ArenaSession.IsActive || !Settings.MovementEnabled.Value || !Settings.NoFallDamage.Value)
                return true;

            var mainPlayer = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (mainPlayer == null || !ReferenceEquals(mainPlayer.ActiveHealthController, __instance))
                return true;

            __result = 0f;
            return false;
        }
    }
}
