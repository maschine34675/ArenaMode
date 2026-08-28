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
        new InertiaValuesPatch().Enable();
        new InstantLeanPatch().Enable();
        new SlideAnimPatch().Enable();
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
    /// The ONE arena bracket on DirectApplyMotion: slide velocity injection
    /// (absorbed CombatSlide) plus displacement scale beyond the animator blend
    /// tree. Leaves motion.y alone so gravity and slope projection keep working.
    /// Merging both into a single save/set(-1)/restore avoids the two-mods-two-
    /// brackets SpeedLimit interleave permanently; the guard + Priority.Last stay
    /// as defense against any future third bracket.
    /// </summary>
    private sealed class MotionScalePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));

        [PatchPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(MovementContext __instance, ref Vector3 motion, float deltaTime, out float __state)
        {
            __state = float.NaN;
            if (!IsArenaMovement(__instance))
                return;

            var touched = false;

            if (Settings.SlideEnabled.Value)
            {
                var slide = PlayerField(__instance)?.GetComponent<ArenaSlideController>();
                if (slide != null && slide.IsSliding)
                {
                    motion += slide.SlideVelocity * deltaTime;
                    touched = true;
                }
            }

            var scale = Settings.ExtraMotionScale.Value;
            if (scale > 1f)
            {
                motion.x *= scale;
                motion.z *= scale;
                touched = true;
            }

            if (!touched)
                return;

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

    /// <summary>
    /// Animator blend-tree speed while sliding / fading out of a slide
    /// (absorbed CombatSlide AnimSpeedPatch, arena-gated).
    /// </summary>
    private sealed class SlideAnimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.PlayerAnimatorSetCharacterMovementSpeed));

        [PatchPrefix]
        private static void Prefix(MovementContext __instance, ref float smoothedCharacterMovementSpeed)
        {
            if (!Settings.SlideEnabled.Value || !IsArenaMovement(__instance))
                return;

            var slide = PlayerField(__instance)?.GetComponent<ArenaSlideController>();
            if (slide == null)
                return;

            if (slide.IsSliding)
            {
                var speed = slide.NormalizedSlideAnimSpeed;
                if (speed >= 0f)
                    smoothedCharacterMovementSpeed = speed;
                return;
            }

            if (slide.IsFadingOut)
            {
                var t = Mathf.Clamp01((Time.time - slide.FadeStartTime) / ArenaSlideController.FadeOutDuration);
                if (t >= 1f)
                {
                    slide.IsFadingOut = false;
                    return;
                }

                smoothedCharacterMovementSpeed = Mathf.Lerp(slide.FadeLastSpeed, smoothedCharacterMovementSpeed, t);
            }
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
    /// Inertia removal writes VALUES, not getter patches: the inertia getters are
    /// tiny non-virtual bodies (field loads) the Mono JIT inlines at the
    /// MovePlayerState call sites, so Harmony postfixes on them never fire there —
    /// the original getter approach only appeared to work while the per-frame state
    /// snap was still doing everything. WeightRelatedValuesUpdated IS virtual, runs
    /// after every PhysicalBase weight recompute, and consumers read the very
    /// fields/backing fields overwritten here — inlined call sites included.
    ///
    /// MovePlayerState blends with smoothTime = TransitionCoef * WalkInertia *
    /// (side/diagonal factor). WalkInertia gets floored to near-zero (instant
    /// accelerate/stop), the side factors are rescaled so StrafeSmoothing is a true
    /// fraction of the VANILLA direction blend independent of that floor, and
    /// Physical.Inertia = 0 kills the direction-reversal dead-stop
    /// (MoveTimeRange.Evaluate) plus the diagonal stay time. ProcessDirection's
    /// MinDirectionBlendTime floor keeps dial 0 from snapping the animation the way
    /// the removed state snap did. Every value is freshly recomputed from weight
    /// before this postfix runs, so the in-place scaling never compounds.
    /// </summary>
    private sealed class InertiaValuesPatch : ModulePatch
    {
        internal const float WalkInertiaFloor = 0.005f;

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.WeightRelatedValuesUpdated));

        [PatchPostfix]
        private static void Postfix(MovementContext __instance)
        {
            if (!Settings.NoInertia.Value || !IsArenaMovement(__instance))
                return;

            try
            {
                var physical = PlayerField(__instance).Physical;
                var vanillaWalk = __instance.WalkInertia;

                __instance.WalkInertia = WalkInertiaFloor;
                __instance.SprintBrakeInertia = 0f;

                var strafeScale = vanillaWalk * Mathf.Clamp01(Settings.StrafeSmoothing.Value) / WalkInertiaFloor;
                physical.MoveSideInertia *= strafeScale;
                physical.MoveDiagonalInertia *= strafeScale;

                physical.Inertia = 0f;
            }
            catch (Exception)
            {
                // Fail open — the vanilla values stay.
            }
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
