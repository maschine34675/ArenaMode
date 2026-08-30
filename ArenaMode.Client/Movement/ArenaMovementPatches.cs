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
        new MotionScalePatch().Enable();
        new AdsSlowdownPatch().Enable();
        new AdsTransitionPatch().Enable();
        new InertiaValuesPatch().Enable();
        new StrafeBlendPatch().Enable();
        new InstantLeanPatch().Enable();
        new SlideAnimPatch().Enable();
        new BushSlowdownPatch().Enable();
        new JumpHeightPatch().Enable();
        new FallDamagePatch().Enable();
        new VaultSpeedPatch().Enable();
        new VaultingReachPatch().Enable();
    }

    internal static void ResetPerRaidState() => InertiaValuesPatch.LoggedThisRaid = false;

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

    // NOTE: no animator-target sprint patch. Bracketing StateSprintSpeedLimit in
    // SprintAcceleration to raise the animator 'SprintSpeed' root-motion target
    // was mechanically sound (verified: setter unclamped, ramp reaches the raised
    // target, m/s cap lifted) but produced ZERO visible speed change in playtest —
    // the sprint blend's root motion saturates near its vanilla range, which no
    // C#-side value can push past. Sprint speed therefore scales displacement in
    // MotionScalePatch like walk does.

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
        private static MovementContext _limitRestoreContext;
        private static float _limitRestoreValue;

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));

        [PatchPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(MovementContext __instance, ref Vector3 motion, float deltaTime, out float __state)
        {
            __state = float.NaN;

            // Self-heal: a plain postfix is skipped when the original throws — a
            // stuck SpeedLimit of -1 would disable the m/s clamp until the next
            // state transition. Restore any missed bracket before opening a new one.
            if (ReferenceEquals(_limitRestoreContext, __instance))
            {
                var healController = __instance.CharacterController;
                if (healController != null)
                    healController.SpeedLimit = _limitRestoreValue;
                _limitRestoreContext = null;
            }

            if (!IsArenaMovement(__instance))
                return;

            var touched = false;

            // Displacement is animator root motion and the walk chain is triple-
            // clamped at a normalized 1.0 (MovePlayerState constant clamp,
            // StateSpeedLimit, animator 'Speed' param) — speed multipliers can
            // ONLY exist here, post-clamp. Sprint uses the same displacement route:
            // raising the animator 'SprintSpeed' target instead (the elegant
            // variant) proved invisible in playtest — the sprint blend saturates —
            // so both multipliers scale the actual motion and the m/s SpeedLimit
            // bracket below lifts the CharacterController cap.
            // Jump/fall frames inherit the multiplier of the state they launched
            // from so airborne momentum stays continuous (no mid-air accelerate
            // or brake on sprint jumps).
            var stateName = __instance.CurrentState != null
                ? __instance.CurrentState.Name
                : EPlayerState.Idle;
            var airborne = stateName == EPlayerState.Jump || stateName == EPlayerState.FallDown;
            var sprinting = stateName == EPlayerState.Sprint
                || (airborne
                    && __instance.PreviousState != null
                    && __instance.PreviousState.Name == EPlayerState.Sprint);
            var scale = Settings.ExtraMotionScale.Value
                * (sprinting ? Settings.SprintSpeedMult.Value : Settings.WalkSpeedMult.Value);
            if (scale > 1f)
            {
                motion.x *= scale;
                motion.z *= scale;
                touched = true;
            }

            // The slide controller's velocities are tuned in real m/s — add them
            // AFTER the root-motion scaling so they are not stretched by it.
            if (Settings.SlideEnabled.Value)
            {
                var slide = PlayerField(__instance)?.GetComponent<ArenaSlideController>();
                if (slide != null && slide.IsSliding)
                {
                    motion += slide.SlideVelocity * deltaTime;
                    touched = true;
                }
            }

            if (!touched)
                return;

            var controller = __instance.CharacterController;
            if (controller != null && controller.SpeedLimit >= 0f)
            {
                __state = controller.SpeedLimit;
                _limitRestoreContext = __instance;
                _limitRestoreValue = __state;
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
            _limitRestoreContext = null;
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

    /// <summary>
    /// The ADS movement penalty lands as a state speed limit. The former
    /// SetAimingSlowdown prefix sat right at Mono's ~20-byte inline threshold and
    /// could silently never fire; AddStateSpeedLimit is safely above it and the
    /// Aiming cause uniquely identifies the path.
    /// </summary>
    private sealed class AdsSlowdownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.AddStateSpeedLimit),
                new[] { typeof(float), typeof(Player.ESpeedLimit) });

        [PatchPrefix]
        private static void Prefix(MovementContext __instance, ref float speedLimit, Player.ESpeedLimit cause)
        {
            if (cause != Player.ESpeedLimit.Aiming || !IsArenaMovement(__instance))
                return;

            speedLimit = Mathf.Lerp(speedLimit, 1f, Settings.AdsMoveFactor.Value);
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

                // The game only reassigns _aimingSpeed when firearm data AND its
                // Weapon are present — multiplying on any other invocation
                // (grenade/melee/empty hands, weaponless data) would compound the
                // factor call after call.
                var animationData = FirearmAnimationDataField?.GetValue(__instance);
                if (animationData == null)
                    return;

                if (AccessTools.Property(animationData.GetType(), "Weapon")?.GetValue(animationData) == null)
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
    /// (side/diagonal factor). WalkInertia gets floored to its zero-inertia vanilla
    /// value (instant accelerate/stop), and Physical.Inertia = 0 kills the
    /// direction-reversal dead-stop (MoveTimeRange.Evaluate) plus the diagonal stay
    /// time. The strafe dial does NOT live here: the direction blend is dominated
    /// by the MinDirectionBlendTime floor, which only StrafeBlendPatch can scale.
    /// Every value is freshly recomputed from weight before this postfix runs, so
    /// the writes never compound.
    /// </summary>
    private sealed class InertiaValuesPatch : ModulePatch
    {
        internal const float WalkInertiaFloor = 0.005f;

        internal static bool LoggedThisRaid;

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
                physical.Inertia = 0f;

                if (!LoggedThisRaid)
                {
                    LoggedThisRaid = true;
                    Plugin.ModLogger.LogInfo(
                        $"Arena inertia values applied (WalkInertia {vanillaWalk:0.###} -> {WalkInertiaFloor}, SprintBrakeInertia -> 0, Inertia -> 0).");
                }
            }
            catch (Exception)
            {
                // Fail open — the vanilla values stay.
            }
        }
    }

    private static readonly AccessTools.FieldRef<MovePlayerState, float> DirectionSpeedAccelField =
        AccessTools.FieldRefAccess<MovePlayerState, float>("_directionSpeedAcceleration");

    private static readonly AccessTools.FieldRef<MovePlayerState, Vector2> DirectionBlendVelocityField =
        AccessTools.FieldRefAccess<MovePlayerState, Vector2>("_velocity2");

    /// <summary>
    /// The strafe dial has to scale the game's own FLOOR: the walking direction
    /// blend is SmoothDamp with time Max(MinDirectionBlendTime = 0.15s, smoothTime)
    /// and with NoInertia the incoming smoothTime is already ~0 — every value-level
    /// dial lands below the floor and changes nothing (the earlier playtests).
    /// Reimplements the five-line ProcessDirection body with the floor scaled by
    /// StrafeSmoothing: 1 = the vanilla path bit-for-bit (prefix falls through),
    /// 0 = instant flip. Prone falls through to vanilla entirely.
    /// </summary>
    private sealed class StrafeBlendPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovePlayerState), nameof(MovePlayerState.ProcessDirection));

        [PatchPrefix]
        private static bool Prefix(MovePlayerState __instance, float deltaTime, float smoothTime)
        {
            if (!Settings.NoInertia.Value)
                return true;

            var context = __instance.MovementContext;
            if (context == null || context.IsInPronePose || !IsArenaMovement(context))
                return true;

            var dial = Mathf.Clamp01(Settings.StrafeSmoothing.Value);
            if (dial >= 1f)
                return true;

            try
            {
                var direction = __instance.Direction;
                __instance.LastDirectionInput = direction;
                if (direction.sqrMagnitude > 1e-8f)
                    __instance.LastNonZeroDirectionInput = direction;

                var floor = Singleton<GlobalConfiguration>.Instance.Inertia.MinDirectionBlendTime * dial;
                __instance.InertiaDirection = Vector2.SmoothDamp(
                    __instance.InertiaDirection,
                    direction,
                    ref DirectionBlendVelocityField(__instance),
                    Mathf.Max(floor, smoothTime) / DirectionSpeedAccelField(__instance),
                    EFTHardSettings.Instance.InertiaInputMaxSpeed,
                    deltaTime);
                __instance.InverseTransformDirection(__instance.InertiaDirection);

                // InverseTransformDirection just restored Direction to the player-
                // LOCAL input; the discrete animator direction must be built from
                // THAT (the capture above is world-space — using it would make the
                // strafe animation facing-dependent).
                var localDirection = __instance.Direction;
                var movementDirection = localDirection.sqrMagnitude > 1e-8f
                    ? localDirection
                    : context.MovementDirection;
                __instance.SetSmoothDiscreteDirection(
                    MovementDirectionExtension.ConvertToMovementDirection(movementDirection), deltaTime);
                return false;
            }
            catch (Exception)
            {
                return true; // fail open — the vanilla path runs
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

    /// <summary>
    /// Vaulting and climbing, faster.
    ///
    /// SetVaultingSpeed is the single choke point: whatever range it is handed
    /// feeds BOTH the animator's Vaulting_speed float (how fast the motion plays)
    /// and MovementContext.VaultingSpeed. Scaling the range rather than the
    /// result keeps the shape of it — Evaluate lerps from x (healthy) to y
    /// (overloaded or injured), so a hurt vault stays slower than a healthy one
    /// instead of every vault collapsing onto one speed.
    /// </summary>
    private sealed class VaultSpeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.SetVaultingSpeed));

        [PatchPrefix]
        private static void Prefix(MovementContext __instance, ref Vector2 speedRange)
        {
            var mult = Settings.VaultSpeedMult.Value;
            if (mult <= 1f || !IsArenaMovement(__instance))
                return;

            speedRange *= mult;
        }
    }

    /// <summary>
    /// Widens the vaulting limits before the component that snapshots them exists.
    ///
    /// The timing is the whole point. VaultingMovesModel's constructor copies the
    /// height and length bounds into the obstacle SEARCH, so applying this on the
    /// first frame of the raid would leave the search narrow and only the verdict
    /// wide. Every respawn builds a new player and runs this again, which is also
    /// how the setting reaches a player who died before it was changed.
    /// </summary>
    private sealed class VaultingReachPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.InitVaultingComponent));

        [PatchPrefix]
        private static void Prefix(bool aiControlled)
        {
            // Bots return out of this method immediately, so they never read the
            // values at all — but gating here as well keeps the exception to the
            // no-globals rule narrow enough to state in one line.
            if (aiControlled || !VaultingTuning.Wanted)
                return;

            VaultingTuning.Apply();
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
