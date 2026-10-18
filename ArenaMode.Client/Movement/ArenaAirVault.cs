using ArenaMode.Config;
using Comfort.Common;
using EFT;
using EFT.Vaulting;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// The held native vault binding may request a mantle during a real jump.
/// Only this synchronous request supplies an airborne strategy: native geometry,
/// stamina, gameplay restrictions and the animator still execute the actual move.
/// No grounded flag, transform, controller motion or global settings are written.
/// </summary>
internal static partial class ArenaAirVault
{
    private sealed class Request
    {
        internal Player Player;
        internal MovementContext Context;
        internal JumpPlayerState Jump;
        internal VaultingComponent Component;
        internal GameWorld World;
        internal AbstractGame Game;
        internal PlayerAnimator Animator;
        internal object AnimatorHandle;
        internal object AnimatorState;
        internal Animator UnityAnimator;
        internal RuntimeAnimatorController UnityController;
        internal Geometry CommittedGeometry;
    }

    private readonly struct Geometry
    {
        private readonly float _length, _height, _behind, _velocity;
        private readonly Vector3 _obstacle;

        internal Geometry(VaultingComponent component)
        {
            _length = component.VaultingLength;
            _height = component.VaultingHeight;
            _behind = component.BehindObstacleRatio;
            _velocity = component.AbsoluteForwardVelocity;
            _obstacle = component.MaxWeightPointPosition;
        }

        internal bool IsFinite => Finite(_length) && Finite(_height) && Finite(_behind)
            && Finite(_velocity) && Finite(_obstacle.x) && Finite(_obstacle.y) && Finite(_obstacle.z);

        internal bool Matches(VaultingComponent component)
        {
            var current = new Geometry(component);
            return IsFinite && current.IsFinite && _length == current._length && _height == current._height
                && _behind == current._behind && _velocity == current._velocity
                && (_obstacle - current._obstacle).sqrMagnitude == 0f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private sealed class Pending
    {
        internal Request Request;
        internal EVaultingStrategy Strategy;
        internal float ExpiresAt;
        internal UnityTransfer Unity;
    }

    private static Request _request;
    private static Pending _pending;
    private static Player _blockedUntilRelease;
    private static bool _enabled;
    private static bool _warned;

    internal static void Enable()
    {
        try
        {
            new ArenaAirVaultHeldVaultPatch().Enable();
            new ArenaAirVaultStrategyPatch().Enable();
            new ArenaAirVaultCommitPatch().Enable();
            new ArenaAirVaultAnimatorFramePatch().Enable();
            new ArenaAirVaultAnimatorTransitionPatch().Enable();
            new ArenaAirVaultUnityBodyPatch().Enable();
            new ArenaAirVaultUnityEntryPatch().Enable();
            new ArenaAirVaultObservedMotionPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { _enabled = false; Warn(ex); }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        _request = null;
        _animatorFrame = null;
        _motionObservation = null;
        CancelPending();
        _blockedUntilRelease = null;
    }

    internal static bool HasPendingRequest(Player player)
    {
        try { return _pending != null && ReferenceEquals(_pending.Request.Player, player) && OwnsTarget(_pending.Request); }
        catch (Exception) { return false; }
    }

    /// <summary>Called even when movement is disabled, to release our own queued flag.</summary>
    internal static void Tick()
    {
        if (_request != null) return;
        try
        {
            TickMotionObservation();
            if (_blockedUntilRelease != null
                && (!_blockedUntilRelease.IsVaultingPressed || !ArenaMovementScope.IsLocal(_blockedUntilRelease)))
                _blockedUntilRelease = null;

            Pending pending = _pending;
            if (pending == null) return;
            Request request = pending.Request;
            if (!OwnsTarget(request))
            { _pending = null; return; }

            if (pending.Unity != null) { ObserveUnityEntry(pending); return; }

            // Once EFT has entered the move, its state owns flags/capsule/cleanup.
            // Never clear a successfully entered native climb or vault.
            if (request.Context.CurrentState is VaultingMovementState)
            { _pending = null; return; }

            if (!IsEligible(request) || !HasFlag(pending))
            { TraceUnityRejection(request, "pending", "request-ineligible-or-flag-cleared"); CancelPending(); return; }

            if (Time.unscaledTime >= pending.ExpiresAt)
            {
                _blockedUntilRelease = request.Player;
                CancelPending();
                Trace(request, "animation timeout; release vault to retry");
                return;
            }


            // The animator may already be blending into its move. Do not cancel
            // that handoff merely because native strategy selection blocks NEW
            // requests while an animation transition is running.
            if (request.Context.IsAnimatorInTransitionState(0)) return;
            if (!RefreshPending(pending))
            { TraceUnityRejection(request, "pending", "geometry-stamina-hands-refresh"); CancelPending(); }
        }
        catch (Exception ex)
        {
            if (_pending?.Unity == null) CancelPending();
            Warn(ex);
        }
    }

    // Refresh parameters, not the input request: TryVaulting has already sent
    // its one-time hands/animation notifications. Never extend the expiry.
    private static bool RefreshPending(Pending pending)
    {
        Request request = pending.Request;
        if (!ReferenceEquals(_pending, pending) || !IsEligible(request) || !HasFlag(pending)
            || Time.unscaledTime >= pending.ExpiresAt || request.Context.IsAnimatorInTransitionState(0)) return false;
        request.Component.Tick();
        if (!ReferenceEquals(_pending, pending) || !IsEligible(request) || !HasFlag(pending)
            || SelectGeometryStrategy(request) != pending.Strategy
            || !new Geometry(request.Component).IsFinite) return false;
        bool valid;
        _request = request;
        try { valid = request.Player.VaultingGameplayRestrictions?.CanVaulting() == true; }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
        if (!valid || !ReferenceEquals(_pending, pending) || !IsEligible(request) || !HasFlag(pending)
            || request.Context.IsAnimatorInTransitionState(0)) return false;
        var geometry = new Geometry(request.Component);
        request.Component.SetParametersInAnimator();
        if (!ReferenceEquals(_pending, pending) || !IsEligible(request) || !HasFlag(pending)
            || request.Context.IsAnimatorInTransitionState(0) || !geometry.Matches(request.Component)) return false;
        // Native float setters can notify other listeners. A changed hands
        // operation or obstacle after those callbacks cannot inherit approval.
        _request = request;
        try { valid = request.Player.VaultingGameplayRestrictions?.CanVaulting() == true; }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
        return valid && ReferenceEquals(_pending, pending) && IsEligible(request) && HasFlag(pending)
            && !request.Context.IsAnimatorInTransitionState(0) && geometry.Matches(request.Component)
            && SelectGeometryStrategy(request) == pending.Strategy;
    }

    private static bool IsEligible(Request request)
    {
        if (!_enabled || !Settings.AirVaultEnabled.Value || request == null
            || !ArenaMovementScope.IsLocal(request.Player, requireInput: true)
            || !OwnsTarget(request)
            || !ReferenceEquals(request.Context.CurrentState, request.Jump)
            || !ReferenceEquals(request.Player.CurrentManagedState, request.Jump)
            || request.Jump.GetType() != typeof(JumpPlayerState)
            || request.Context.OverridenState != null || request.Context.OverridenControlsState != null)
            return false;

        return request.Player.IsVaultingPressed
            && !request.Context.IsGrounded
            && request.Jump._jumpState == JumpPlayerState.EJumpState.Jump
            && !request.Context.IsInPronePose && !request.Context.IsInMountedState
            && !request.Context.IsStationaryWeaponInHands
            && !request.Context.PlayerAnimatorGetIsVaulting()
            && request.Player.IsForwardInputDirection;
    }

    private static bool OwnsTarget(Request request)
    {
        // A pooled body/context can survive an epoch boundary or receive a new
        // animator. Neither its new flags nor a new raid belong to this lease.
        if (request == null || request.Player == null || request.World == null || request.Game == null
            || !ReferenceEquals(Singleton<GameWorld>.Instance, request.World)
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, request.Game)) return false;
        return ReferenceEquals(request.Player.MovementContext, request.Context)
            && ReferenceEquals(request.Player.VaultingComponent, request.Component)
            && ReferenceEquals(request.Player.VaultingParameters, request.Component)
            && ReferenceEquals(request.Component._vaultingContext, request.Context)
            && request.Animator != null && request.AnimatorHandle != null
            && ReferenceEquals(request.Context.PlayerAnimator, request.Animator)
            && ReferenceEquals(request.Animator.Animator, request.AnimatorHandle)
            && (request.AnimatorHandle is not FastAnimatorSystem.FastAnimatorProcessor fast
                || ReferenceEquals(fast.FastControllerInfo, request.AnimatorState))
            && (request.AnimatorHandle is not AnimationSystem.UnityAnimatorWrapper unity
                || (request.UnityAnimator != null && request.UnityController != null
                    && ReferenceEquals(unity.Animator, request.UnityAnimator)
                    && ReferenceEquals(request.UnityAnimator.runtimeAnimatorController, request.UnityController)));
    }

    /// <summary>
    /// Mirrors the native explicit strategy policy except its two ground-jump
    /// exclusions, with native-clip height and actual planar reach bounds for airborne climbs.
    /// CanMove retains roof/wall/distance geometry; later inventory/meds/weapon/grenade checks remain in
    /// MovementContext.TryVaulting -> VaultingGameplayRestrictions.CanVaulting.
    /// </summary>
    private static EVaultingStrategy SelectGeometryStrategy(Request request)
    {
        VaultingComponent component = request.Component;
        MovementContext context = request.Context;
        var model = component._vaultingModelDebug;
        if (component._vaultingSettings?.IsActive != true || model == null
            || component.IsVaulting() || context.MovementDirection.y < 0f
            || context.IsAnimatorInTransitionState(0)
            || model.ObstacleCalculatorModel.IsTerrain
            || model.StairsCalculatorModel.IsStairsCondition()) return EVaultingStrategy.None;

        bool sprintVaultBlocked = context.IsSprintEnabled && component.BehindObstacleRatio > -0.5f;
        if (component._canVaultByStaminaGetter?.Invoke() == true
            && model.VaultingStatesModel.VaultState.CanMove() && !sprintVaultBlocked)
            return EVaultingStrategy.Vault;
        if (component._canClimbByStaminaGetter?.Invoke() == true
            && model.VaultingStatesModel.ClimbState.CanMove() && !context.IsSprintEnabled)
        {
            // SPT 4.1 Climb_on clips end at 1.30m of vertical root travel.
            // Retain vanilla's 1cm tolerance, not Arena's widened 1.70m search
            // limit. Held input retries as the jump reduces the remaining height.
            // This is request-local; standing reach and the search stay intact.
            if (component.VaultingHeight > 1.31f)
            {
                Trace(request, "climb beyond native animation reach; waiting for closer height",
                    detail: () => $"height={component.VaultingHeight:0.###}, max=1.31");
                return EVaultingStrategy.None;
            }
            // Native distance starts at the velocity-shifted scan grid. At 9m/s
            // this can approve a ledge ~1.4m from the body, but these Climb clips
            // move only ~0.55m forward through our Jump blend. Require the real
            // selected surface point within vanilla's 0.5m standing reach.
            // Keep Y separate (above); a 3D distance would reject tall ledges.
            Vector3 gap = component.MaxWeightPointPosition - context.TransformPosition;
            float planarDistanceSquared = gap.x * gap.x + gap.z * gap.z;
            if (!(planarDistanceSquared <= 0.25f))
            {
                Trace(request, "climb beyond physical approach reach; waiting to approach",
                    detail: () => $"bodyDistance={Math.Sqrt(planarDistanceSquared):0.###}, max=0.5, gridDistance={model.ObstacleCalculatorModel.DistanceToMainObstacle:0.###}");
                return EVaultingStrategy.None;
            }
            return EVaultingStrategy.Climb;
        }
        return EVaultingStrategy.None;
    }

    private static bool HasFlag(Pending pending)
        => pending.Strategy == EVaultingStrategy.Climb
            ? pending.Request.Context.PlayerAnimator.GetDoClimb()
            : pending.Request.Context.PlayerAnimator.GetDoVault();

    private static void CancelPending()
    {
        Pending pending = _pending;
        _pending = null;
        if (pending == null) return;
        try
        {
            Request request = pending.Request;
            if (!OwnsTarget(request) || request.Context.CurrentState is VaultingMovementState) return;
            if (pending.Unity != null) return; // Already dispatched: native callbacks own the move.
            if (pending.Strategy == EVaultingStrategy.Climb)
                request.Context.PlayerAnimatorSetDoClimb(false);
            else request.Context.PlayerAnimatorSetDoVault(false);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena airborne mantle unavailable for this request; native movement retained: "
            + exception.GetBaseException().Message);
    }

    private sealed class ArenaAirVaultHeldVaultPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ToggleVaulting));

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            if (_request != null || _pending != null || ReferenceEquals(_blockedUntilRelease, __instance)) return;
            Request request = null;
            try
            {
                if (__instance?.VaultingComponent is not VaultingComponent component
                    || __instance.CurrentManagedState is not JumpPlayerState jump) return;
                request = new Request { Player = __instance, Context = __instance.MovementContext,
                    Jump = jump, Component = component, World = Singleton<GameWorld>.Instance,
                    Game = Singleton<AbstractGame>.Instance, Animator = __instance.MovementContext?.PlayerAnimator };
                request.AnimatorHandle = request.Animator?.Animator;
                request.AnimatorState = (request.AnimatorHandle as FastAnimatorSystem.FastAnimatorProcessor)?.FastControllerInfo;
                if (request.AnimatorHandle is AnimationSystem.UnityAnimatorWrapper unity)
                {
                    request.UnityAnimator = unity.Animator;
                    request.UnityController = request.UnityAnimator?.runtimeAnimatorController;
                }
                if (!IsEligible(request) || request.Context.IsAnimatorInTransitionState(0)
                    || request.Context.PlayerAnimator.GetDoClimb() || request.Context.PlayerAnimator.GetDoVault()) return;
                if (request.AnimatorHandle is not FastAnimatorSystem.FastAnimatorProcessor
                    && request.UnityAnimator == null)
                { Trace(request, "unsupported body animator: " + request.AnimatorHandle.GetType().Name); return; }
                if (request.UnityAnimator != null)
                {
                    string reason = UnitySourceRejection(request);
                    if (reason != null) { TraceUnityRejection(request, "held", reason); return; }
                }

                // Manual mode normally refreshes after gameplay restrictions.
                // Refresh before them too, so their height-dependent checks see
                // the actual airborne obstacle, never the earlier ground scan.
                component.Tick();
                if (!IsEligible(request)) return;
                _request = request;
                if (!request.Context.TryVaulting()) Trace(request, "held input received; native geometry, stamina or hands rejected the move");
            }
            catch (Exception ex) { Warn(ex); }
            finally { if (ReferenceEquals(_request, request)) _request = null; }
        }
    }

    private sealed class ArenaAirVaultStrategyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(VaultingComponent), nameof(VaultingComponent.GetVaultingStrategy));

        [PatchPostfix]
        private static void Postfix(VaultingComponent __instance, ref EVaultingStrategy __result)
        {
            Request request = _request;
            if (__result != EVaultingStrategy.None || request == null
                || !ReferenceEquals(request.Component, __instance)) return;
            try
            {
                if (IsEligible(request)) __result = SelectGeometryStrategy(request);
            }
            catch (Exception ex) { Warn(ex); }
        }
    }

    private sealed class ArenaAirVaultCommitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(VaultingComponent), nameof(VaultingComponent.DoVaultingByStrategy));

        [PatchPrefix]
        private static bool Prefix(VaultingComponent __instance, EVaultingStrategy strategy, ref bool __result)
        {
            Request request = _request;
            if (request == null || !ReferenceEquals(request.Component, __instance)) return true;
            try
            {
                // Native manual TryVaulting refreshes its grid a second time.
                // Recheck the ordinary height-dependent hands restrictions on
                // that final geometry before the animator receives any flag.
                if (IsEligible(request) && SelectGeometryStrategy(request) == strategy
                    && request.Player.VaultingGameplayRestrictions?.CanVaulting() == true)
                {
                    request.CommittedGeometry = new Geometry(__instance);
                    if (request.CommittedGeometry.IsFinite) return true;
                }
            }
            catch (Exception ex) { Warn(ex); }
            __result = false;
            return false;
        }

        [PatchPostfix]
        private static void Postfix(VaultingComponent __instance, EVaultingStrategy strategy, bool __result)
        {
            Request request = _request;
            if (!__result || request == null || !ReferenceEquals(request.Component, __instance)
                || (strategy != EVaultingStrategy.Climb && strategy != EVaultingStrategy.Vault)) return;
            try
            {
                if (request.Context.CurrentState is VaultingMovementState) return;
                _pending = new Pending { Request = request, Strategy = strategy, ExpiresAt = Time.unscaledTime + 0.5f };
                if (!IsEligible(request) || !HasFlag(_pending)) CancelPending();
                else Trace(request, "queued " + strategy);
            }
            catch (Exception ex) { CancelPending(); Warn(ex); }
        }
    }
}
