using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// One deliberate vertical impulse per uninterrupted local Arena airborne episode,
/// including walking off a ledge. EFT still owns motion, collision and fall damage.
/// </summary>
internal static class ArenaDoubleJump
{
    private sealed class Credit
    {
        internal Player Player;
        internal MovementContext Context;
        internal GameWorld World;
        internal AbstractGame Game;
        internal bool Airborne;
        internal bool Spent;
        internal int StateRevision;
        internal Request NativeRequest;
        internal JumpPlayerState LiftState;
        internal Vector3 NativeLift;
    }

    private sealed class Request
    {
        internal MovementState State;
        internal Credit Credit;
        internal Player Player;
        internal float ExpiresAt;
        internal int StateRevision;
        internal Request NativeReservation;
        internal bool OwnsNativeSignal;
        internal bool NativeJumpEntered;
    }

    // The owner is the body/context episode, never an animator state: EFT reuses
    // Jump and FallDown objects and may switch between them without touching down.
    private static Credit _credit;
    private static ConditionalWeakTable<MovementContext, Credit> _credits = new();
    private static WeakReference<GameWorld> _endedWorld;
    private static WeakReference<AbstractGame> _endedGame;
    private static AccessTools.FieldRef<Player, float> VaultingTiming;
    private static bool _enabled;
    private static bool _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            VaultingTiming = AccessTools.FieldRefAccess<Player, float>("_vaultingTiming");
            new JumpEnterPatch().Enable();
            new FallEnterPatch().Enable();
            new LandingPatch().Enable();
            new JumpCommandPatch().Enable();
            Settings.DoubleJumpEnabled.SettingChanged += ClearCredits;
            Settings.MovementEnabled.SettingChanged += ClearCredits;
            RaidLifecycle.OnRaidEnded += ResetCredits;
            _enabled = true;
        }
        catch (Exception exception)
        {
            _enabled = false;
            Warn(exception);
        }
    }

    internal static void Shutdown()
    {
        InvalidateCredit();
        _credit = null; // Weak context ownership below preserves spent state without retaining a stopped raid.
        _enabled = false;
        Settings.DoubleJumpEnabled.SettingChanged -= ClearCredits;
        Settings.MovementEnabled.SettingChanged -= ClearCredits;
        RaidLifecycle.OnRaidEnded -= ResetCredits;
    }

    private static void ResetCredits()
    {
        // Raid-end notification precedes singleton teardown. Do not grant a
        // fresh falling credit to the just-ended world during that interval.
        _endedWorld = new WeakReference<GameWorld>(Singleton<GameWorld>.Instance);
        _endedGame = new WeakReference<AbstractGame>(Singleton<AbstractGame>.Instance);
        CancelNativeRequest(_credit);
        _credit = null;
        _credits = new ConditionalWeakTable<MovementContext, Credit>();
    }

    private static void InvalidateCredit()
    {
        if (_credit != null)
        {
            Invalidate(_credit);
            CancelNativeRequest(_credit);
        }
        Player player = Singleton<GameWorld>.Instance?.MainPlayer;
        Credit credit = Observe(player?.MovementContext);
        if (credit == null) return;
        Invalidate(credit);
        CancelNativeRequest(credit);
    }

    private static void Invalidate(Credit credit)
    {
        // A switch changed while standing on confirmed ground must work on the
        // next jump. Once airborne, only LandingPatch can establish a new credit.
        credit.Spent = credit.Airborne || !credit.Context.IsGrounded
            || credit.Player.CharacterController == null || !credit.Player.CharacterController.isGrounded;
    }

    private static void ClearCredits(object sender, EventArgs args) => InvalidateCredit();

    private static void Warn(Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning($"Arena double jump callback failed: {exception.GetBaseException().Message}");
    }

    private static bool TryGetLocalPlayer(MovementContext context, out Player player)
    {
        player = null;
        if (!ArenaSession.IsActive || !_enabled || context == null
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)
            || (_endedWorld != null && _endedWorld.TryGetTarget(out GameWorld endedWorld)
                && ReferenceEquals(endedWorld, Singleton<GameWorld>.Instance)
                && _endedGame != null && _endedGame.TryGetTarget(out AbstractGame endedGame)
                && ReferenceEquals(endedGame, Singleton<AbstractGame>.Instance))
            || ArenaRespawnDriver.IsRespawnInProgress || ArenaSpectatorService.IsActive
            || ArenaInputBlocker.Blocked)
            return false;

        player = Singleton<GameWorld>.Instance.MainPlayer;
        return player != null && player.IsYourPlayer && !player.IsAI
            && !player.IsInventoryOpened
            && player.HealthController != null && player.HealthController.IsAlive
            && ReferenceEquals(player.MovementContext, context);
    }

    private static Credit Observe(MovementContext context)
    {
        if (!TryGetLocalPlayer(context, out Player player)) return null;
        GameWorld world = Singleton<GameWorld>.Instance;
        AbstractGame game = Singleton<AbstractGame>.Instance;
        if (_credit == null || !ReferenceEquals(_credit.Player, player)
            || !ReferenceEquals(_credit.Context, context) || !ReferenceEquals(_credit.World, world)
            || !ReferenceEquals(_credit.Game, game))
        {
            CancelNativeRequest(_credit);
            if (_credits.TryGetValue(context, out Credit previous) && ReferenceEquals(previous.Player, player)
                && ReferenceEquals(previous.World, world) && ReferenceEquals(previous.Game, game))
                _credit = previous;
            else
                SetCredit(new Credit { Player = player, Context = context, World = world, Game = game });
        }
        if (!context.IsGrounded) _credit.Airborne = true;
        return _credit;
    }

    private static void SetCredit(Credit credit)
    {
        _credit = credit;
        _credits.Remove(credit.Context);
        _credits.Add(credit.Context, credit);
    }

    private static void CancelNativeRequest(Credit credit)
    {
        Request request = credit?.NativeRequest;
        if (request == null) return;
        credit.NativeRequest = null;
        ReleaseNativeSignal(request);
    }

    private static void ReleaseNativeSignal(Request request)
    {
        if (!request.OwnsNativeSignal || request.NativeJumpEntered) return;
        request.OwnsNativeSignal = false;
        Credit credit = request.Credit;
        Player player = request.Player;
        // This request raised a previously-false bool. Release it only on the
        // same current body/world and its source locomotion or FallDown state.
        // An entered Jump has transferred that signal to native jump ownership.
        if (ReferenceEquals(Singleton<GameWorld>.Instance, credit.World)
            && ReferenceEquals(Singleton<AbstractGame>.Instance, credit.Game)
            && ReferenceEquals(credit.World.MainPlayer, player)
            && ReferenceEquals(player.MovementContext, credit.Context)
            && (IsGroundLocomotion(player.CurrentManagedState)
                || player.CurrentManagedState?.GetType() == typeof(FallDown))
            && credit.Context.PlayerAnimator != null && credit.Context.PlayerAnimator.IsJumpSetted())
            credit.Context.PlayerAnimatorEnableJump(false);
    }

    private static bool OwnsCredit(MovementState state, Player expectedPlayer, Credit expectedCredit)
    {
        if (state == null || !TryGetLocalPlayer(state.MovementContext, out Player player)
            || !ReferenceEquals(player, expectedPlayer)
            || !ReferenceEquals(player.CurrentManagedState, state)
            || !ReferenceEquals(_credit, expectedCredit)
            || !ReferenceEquals(expectedCredit.Player, player)
            || !ReferenceEquals(expectedCredit.Context, state.MovementContext)
            || !ReferenceEquals(expectedCredit.World, Singleton<GameWorld>.Instance)
            || !ReferenceEquals(expectedCredit.Game, Singleton<AbstractGame>.Instance))
            return false;
        return true;
    }

    private static bool CanUse(MovementState state, Player player, Credit credit)
    {
        if (credit == null || credit.Spent || !OwnsCredit(state, player, credit)) return false;
        return IsAirborneActionAllowed(state, player) && player.Physical.CanJump;
    }

    private static bool IsGroundLocomotion(MovementState state)
        => state != null && (state.GetType() == typeof(IdlePlayerState)
            || state.GetType() == typeof(MovePlayerState) || state.GetType() == typeof(SprintPlayerState));

    private static bool IsAirborneActionAllowed(MovementState state, Player player, bool entering = false)
    {
        MovementContext context = state.MovementContext;
        bool supported = state is JumpPlayerState jump
            ? (state.GetType() == typeof(JumpPlayerState) || state.GetType() == typeof(FallDown))
                && (jump._jumpState == JumpPlayerState.EJumpState.Jump
                    || (entering && jump._jumpState == JumpPlayerState.EJumpState.PushingFromTheGround))
            : IsGroundLocomotion(state) && context.PoseLevel > 0.6f
                && context.PlayerAnimator != null && !context.PlayerAnimator.IsJumpSetted();
        // Match Player.Jump's vault guard and the physical/context restrictions.
        // Early ledge input may still be in Idle/Move/Sprint before FallDown enters.
        return supported && Settings.MovementEnabled.Value && Settings.DoubleJumpEnabled.Value
            && !context.IsGrounded && !ArenaAirVault.HasPendingRequest(player)
            && !context.IsInPronePose && !context.IsInMountedState
            && context.CanJump && player.Physical != null
            && Singleton<GlobalConfiguration>.Instantiated
            && !(VaultingTiming(player) > Singleton<GlobalConfiguration>.Instance.VaultingSettings.VaultingInputTime)
            && !context.PlayerAnimatorGetIsVaulting();
    }

    private static bool TryGetImpulse(JumpPlayerState jump, Credit credit, out Vector3 lift, out Vector3 velocity)
    {
        lift = jump._liftForce;
        velocity = Vector3.zero;
        float height = Settings.DoubleJumpHeightMult.Value;
        if (!Finite(height)) return false;
        if (jump.GetType() == typeof(JumpPlayerState))
        {
            if (!ReferenceEquals(credit.LiftState, jump))
            {
                credit.LiftState = jump;
                credit.NativeLift = ArenaJumpBoots.WithoutEntryBonus(jump);
            }
            lift = credit.NativeLift;
        }
        if (jump.GetType() == typeof(FallDown))
        {
            // FallDown.Enter calls Jump.Enter, then deliberately zeroes lift.
            // Reconstruct that native lift, without re-entering/debiting native
            // state or repeating its skill, leg, grounder and animator side effects.
            MovementContext context = jump.MovementContext;
            if (EFTHardSettings.Instance == null || !Finite(jump._initialMagnitude)
                || !Finite(context.Overweight) || !Finite(Settings.JumpHeightMult.Value)) return false;
            float weight = (!context.PhysicalConditionContainsAny(EPhysicalCondition.LeftLegDamaged | EPhysicalCondition.RightLegDamaged)
                || context.PhysicalConditionIs(EPhysicalCondition.OnPainkillers))
                ? Mathf.Lerp(1f, 0.66f, context.Overweight) : 0.66f;
            lift = EFTHardSettings.Instance.LIFT_VELOCITY_BY_SPEED.Evaluate(jump._initialMagnitude) * weight * Vector3.up;
            float firstHeight = Settings.JumpHeightMult.Value;
            if (firstHeight > 1f) lift *= Mathf.Sqrt(firstHeight);
        }
        lift *= ArenaJumpBoots.LiftMultiplier(jump.MovementContext);
        lift *= Mathf.Sqrt(Mathf.Clamp(height, 0.25f, 1.5f));
        velocity = lift * jump._skilledBoost;
        return FinitePositive(velocity.y) && FinitePositive(lift.y)
            && Finite(lift.x) && Finite(lift.z) && Finite(velocity.x) && Finite(velocity.z)
            && Finite(jump.MovementContext.TransformPosition.y) && Finite(jump._liftDelay);
    }

    private static void ApplyImpulse(JumpPlayerState jump, Vector3 lift, Vector3 velocity)
    {
        jump._liftForce = lift;
        jump._stateTime = jump._liftDelay;
        jump._currentLiftforce = velocity;
        jump._potentialH = jump.MovementContext.TransformPosition.y;
        jump._timeDescending = 0f;
        jump._framesWithoutMotion = 0;
        jump._hasLastHeight = false;
        jump._jumpState = JumpPlayerState.EJumpState.Jump;
    }

    private static Request CaptureNativeEntry(JumpPlayerState jump)
    {
        Credit credit = Observe(jump.MovementContext);
        if (credit == null) return null;
        credit.StateRevision++;
        Request request = credit?.NativeRequest;
        if (request != null && ReferenceEquals(request.Credit, credit)
            && OwnsCredit(jump, request.Player, credit)
            && Time.realtimeSinceStartup <= request.ExpiresAt)
        {
            if (jump.GetType() == typeof(JumpPlayerState)) request.NativeJumpEntered = true;
            // Keep an immutable entry capture: a synchronous native debit
            // callback can re-enter even this same animator object.
            return new Request { State = jump, Player = request.Player, Credit = credit,
                StateRevision = credit.StateRevision, NativeReservation = request };
        }
        if (request != null && Time.realtimeSinceStartup > request.ExpiresAt) CancelNativeRequest(credit);
        return null;
    }

    private static void CompleteNativeEntry(JumpPlayerState jump, Request request)
    {
        if (request == null) return;
        try
        {
            // The native Enter just paid OnJump, possibly invoking synchronous
            // body/session/settings callbacks. Never debit a second time here.
            if (!OwnsCredit(jump, request.Player, request.Credit)
                || !ReferenceEquals(request.Credit.NativeRequest, request.NativeReservation)
                || request.StateRevision != request.Credit.StateRevision
                || !IsAirborneActionAllowed(jump, request.Player, entering: true)
                || !TryGetImpulse(jump, request.Credit, out Vector3 lift, out Vector3 velocity)) return;
            request.Credit.NativeRequest = null;
            if (jump.GetType() == typeof(FallDown)) ReleaseNativeSignal(request.NativeReservation);
            ApplyImpulse(jump, lift, velocity);
            Plugin.ModLogger.LogDebug("Arena double jump: completed local ledge jump.");
        }
        catch (Exception exception) { Warn(exception); }
        finally
        {
            if (ReferenceEquals(request.Credit.NativeRequest, request.NativeReservation))
                CancelNativeRequest(request.Credit);
        }
    }

    private sealed class JumpEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter));
        [PatchPrefix]
        private static void Prefix(JumpPlayerState __instance, out Request __state)
            => __state = __instance.GetType() == typeof(JumpPlayerState) ? CaptureNativeEntry(__instance) : null;
        // JumpHeightPatch must have applied the existing first-jump multiplier.
        [PatchPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(JumpPlayerState __instance, Request __state)
        {
            try
            {
                if (__instance.GetType() == typeof(JumpPlayerState))
                {
                    Credit credit = Observe(__instance.MovementContext);
                    if (credit != null && OwnsCredit(__instance, credit.Player, credit))
                    {
                        credit.LiftState = __instance;
                        credit.NativeLift = ArenaJumpBoots.WithoutEntryBonus(__instance);
                    }
                }
                CompleteNativeEntry(__instance, __state);
            }
            catch (Exception exception) { Warn(exception); }
        }
    }

    private sealed class FallEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(FallDown), nameof(FallDown.Enter));
        [PatchPrefix]
        private static void Prefix(FallDown __instance, out Request __state)
            => __state = CaptureNativeEntry(__instance);
        [PatchPostfix]
        private static void Postfix(FallDown __instance, Request __state)
            => CompleteNativeEntry(__instance, __state);
    }

    private sealed class LandingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.CheckFlying));
        [PatchPostfix]
        private static void Postfix(MovementContext __instance)
        {
            try
            {
                Credit credit = Observe(__instance);
                if (credit == null) return;
                if (credit.NativeRequest != null && Time.realtimeSinceStartup > credit.NativeRequest.ExpiresAt)
                    CancelNativeRequest(credit); // Reservation stays spent if native transition failed.
                MovementState state = credit.Player.CurrentManagedState;
                bool descending = state is JumpPlayerState jump
                    && (jump.GetType() == typeof(JumpPlayerState) || jump.GetType() == typeof(FallDown))
                    && jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround
                    && jump._currentLiftforce.y <= 0f;
                bool landedState = descending || IsGroundLocomotion(state) || state?.GetType() == typeof(JumpLandingState);
                // IsGrounded also includes a proximity ray. Require actual native
                // controller contact and reject the push/upward phase, so a low
                // jump's transient near-ground signal cannot mint another credit.
                if (credit.Airborne && __instance.IsGrounded && credit.Player.CharacterController != null
                    && credit.Player.CharacterController.isGrounded && landedState
                    && !__instance.PlayerAnimatorGetIsVaulting() && !ArenaAirVault.HasPendingRequest(credit.Player))
                {
                    CancelNativeRequest(credit);
                    SetCredit(new Credit { Player = credit.Player, Context = __instance, World = credit.World, Game = credit.Game,
                        LiftState = credit.LiftState, NativeLift = credit.NativeLift });
                }
            }
            catch (Exception exception) { Warn(exception); }
        }
    }

    private sealed class JumpCommandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateCommand));
        // Hook the non-empty translator downstream of native UI input blocking.
        // The original command always runs, including the configured jump binding.
        [PatchPrefix]
        private static void Prefix(MoveInputTranslator __instance, ECommand command, out Request __state)
        {
            __state = null;
            if (command != ECommand.Jump) return;
            try
            {
                Player player = __instance._controller;
                MovementState state = player?.CurrentManagedState;
                // Settle an older air request before vanilla handles a new ground
                // press. UI blocking may have delayed the landing observer; the
                // new ground command must own its freshly raised native bool.
                if (state?.MovementContext.IsGrounded == true && _credit != null
                    && ReferenceEquals(_credit.Player, player) && ReferenceEquals(_credit.Context, state.MovementContext))
                    CancelNativeRequest(_credit);
                Credit credit = Observe(state?.MovementContext);
                if (CanUse(state, player, credit))
                    __state = new Request { State = state, Player = player, Credit = credit, StateRevision = credit.StateRevision };
            }
            catch (Exception exception) { Warn(exception); }
        }

        [PatchPostfix]
        private static void Postfix(MoveInputTranslator __instance, Request __state)
        {
            if (__state == null) return;
            try
            {
                MovementState state = __state.State;
                Player player = __state.Player;
                if (!ReferenceEquals(__instance._controller, player) || !CanUse(state, player, __state.Credit)
                    || __state.StateRevision != __state.Credit.StateRevision) return;
                if (state is not JumpPlayerState jump)
                {
                    if (!Finite(Settings.DoubleJumpHeightMult.Value) || !Finite(Time.realtimeSinceStartup)) return;
                    // Idle/Move.Jump refuses !IsGrounded. Ask the native animator
                    // directly once, never fake ground/CanJump. Actual Jump or
                    // FallDown Enter owns the debit; an absent transition pays none.
                    __state.Credit.Spent = true;
                    __state.ExpiresAt = Time.realtimeSinceStartup + 0.5f;
                    __state.OwnsNativeSignal = true;
                    __state.Credit.NativeRequest = __state;
                    try { state.MovementContext.TryJump(); }
                    catch { CancelNativeRequest(__state.Credit); throw; }
                    finally
                    {
                        // Native noise subscribers run before TryJump sets the
                        // bool; cancellation inside them must also release it
                        // after native dispatch returns.
                        if (!ReferenceEquals(__state.Credit.NativeRequest, __state))
                        {
                            __state.OwnsNativeSignal = true;
                            ReleaseNativeSignal(__state);
                        }
                    }
                    return;
                }
                if (!TryGetImpulse(jump, __state.Credit, out Vector3 lift, out Vector3 velocity)) return;
                __state.Credit.Spent = true; // Before callbacks, including failure and re-entry.
                player.Physical.OnJump(true);
                // Paying the normal cost may exhaust stamina. Recheck ownership
                // and restrictions, but never demand CanJump again after debit.
                if (!OwnsCredit(jump, player, __state.Credit)
                    || __state.StateRevision != __state.Credit.StateRevision
                    || !IsAirborneActionAllowed(jump, player)) return;
                // OnJump may synchronously clear/refresh a powerup. Use the live
                // bonus after that callback, without a second stamina debit.
                if (!TryGetImpulse(jump, __state.Credit, out lift, out velocity)) return;
                ApplyImpulse(jump, lift, velocity);
                jump.MovementContext.EmitJumpNoise();
                Plugin.ModLogger.LogDebug("Arena double jump: renewed local vertical impulse.");
            }
            catch (Exception exception) { Warn(exception); }
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool FinitePositive(float value) => Finite(value) && value > 0f;
}
