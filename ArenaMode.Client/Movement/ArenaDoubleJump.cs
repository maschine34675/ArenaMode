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
        internal bool ApplyingImpulse;
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
        internal float CreatedAt;
        internal int StateRevision;
        internal Request NativeReservation;
        internal bool OwnsNativeSignal;
        internal bool NativeJumpEntered;
        internal bool Coyote;
        internal int CoyoteEpoch;
        internal bool ExecuteImpulse;
        internal bool GroundSlide;
        internal long SlideEpoch;
        internal int InputEpoch;
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
    private static Request _command;
    private static Request _slideRequest;
    private static int _inputEpoch;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            VaultingTiming = AccessTools.FieldRefAccess<Player, float>("_vaultingTiming");
            new ArenaDoubleJumpJumpEnterPatch().Enable();
            new ArenaDoubleJumpFallEnterPatch().Enable();
            new ArenaDoubleJumpLandingPatch().Enable();
            new ArenaDoubleJumpJumpCommandPatch().Enable();
            new ArenaDoubleJumpInputOwnerPatch().Enable();
            Settings.DoubleJumpEnabled.SettingChanged += ClearCredits;
            Settings.MovementEnabled.SettingChanged += ClearCredits;
            Settings.SlideEnabled.SettingChanged += ClearSlideRequests;
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
        Settings.SlideEnabled.SettingChanged -= ClearSlideRequests;
        RaidLifecycle.OnRaidEnded -= ResetCredits;
        _slideRequest = null;
    }

    private static void ResetCredits()
    {
        // Raid-end notification precedes singleton teardown. Do not grant a
        // fresh falling credit to the just-ended world during that interval.
        _endedWorld = new WeakReference<GameWorld>(Singleton<GameWorld>.Instance);
        _endedGame = new WeakReference<AbstractGame>(Singleton<AbstractGame>.Instance);
        CancelNativeRequest(_credit);
        _slideRequest = null;
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
        ++credit.StateRevision; // Invalidates captures across synchronous setting callbacks.
        // A switch changed while standing on confirmed ground must work on the
        // next jump. Once airborne, only ArenaDoubleJumpLandingPatch can establish a new credit.
        credit.Spent = credit.Airborne || !credit.Context.IsGrounded
            || credit.Player.CharacterController == null || !credit.Player.CharacterController.isGrounded;
    }

    private static void ClearCredits(object sender, EventArgs args)
    {
        _slideRequest = null;
        InvalidateCredit();
    }
    private static void ClearSlideRequests(object sender, EventArgs args)
    {
        _slideRequest = null;
        if (_credit?.NativeRequest?.GroundSlide == true) CancelNativeRequest(_credit);
    }

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
            && ArenaMovementScope.IsLocal(player, requireInput: true)
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

    private static bool PendingCurrent(Credit credit)
    {
        var request = credit?.NativeRequest;
        if (request == null) return true;
        return TryGetLocalPlayer(credit.Context, out var player) && ReferenceEquals(player, credit.Player)
            && request.InputEpoch == _inputEpoch
            && ReferenceEquals(_credit, credit) && ReferenceEquals(credit.World, Singleton<GameWorld>.Instance)
            && ReferenceEquals(credit.Game, Singleton<AbstractGame>.Instance)
            && credit.Context.CanJump && !credit.Context.IsInPronePose && !credit.Context.IsInMountedState
            && !credit.Context.PlayerAnimatorGetIsVaulting() && !ArenaAirVault.HasPendingRequest(player)
            && Finite(Time.realtimeSinceStartup) && Time.realtimeSinceStartup <= request.ExpiresAt
            && (request.Coyote ? ArenaJumpReliability.IsCoyoteCurrent(player, request.CoyoteEpoch)
                : request.GroundSlide ? Settings.SlideEnabled.Value && request.SlideEpoch == ArenaSlideTransitions.OwnerEpoch
                : Settings.DoubleJumpEnabled.Value);
    }

    // Independent of CheckFlying/Observe: inventory and respawn can stop their
    // eligible observer before a pending native animator bool is consumed.
    internal static void TickOwner()
    {
        try
        {
            if (_credit?.NativeRequest != null && !PendingCurrent(_credit)) CancelNativeRequest(_credit);
            var request = _slideRequest;
            if (request == null) return;
            var player = request.Player;
            var context = request.Credit.Context;
            if (!OwnsSource(request)
                || !ArenaSlideTransitions.HasActiveSlide(player) || request.SlideEpoch != ArenaSlideTransitions.OwnerEpoch
                || !Finite(Time.realtimeSinceStartup) || Time.realtimeSinceStartup > request.ExpiresAt
                || Time.realtimeSinceStartup < request.CreatedAt)
            { _slideRequest = null; return; }
            if (!IsGroundLocomotion(player.CurrentManagedState) || _command != null) return;
            _slideRequest = null; // Never re-enter or extend this user's one request.
            _command = request;
            try
            {
                ArenaSlideTransitions.PrepareJump(player);
                // The original queued tap was crouched; repair an unheld stale
                // vault timer only after native headroom allowed standing here.
                ArenaJumpReliability.PrepareJump(player);
                if (!OwnsSource(request)
                    || !ArenaSlideTransitions.HasActiveSlide(player) || request.SlideEpoch != ArenaSlideTransitions.OwnerEpoch
                    || !context.IsGrounded || context.PoseLevel <= .6f || !context.CanJump || !player.Physical.CanJump
                    || context.PlayerAnimator.IsJumpSetted() || context.PlayerAnimatorGetIsVaulting()
                    || ArenaAirVault.HasPendingRequest(player) || !Singleton<GlobalConfiguration>.Instantiated
                    || VaultingTiming(player) > Singleton<GlobalConfiguration>.Instance.VaultingSettings.VaultingInputTime) return;
                request.Credit = Observe(context);
                if (request.Credit == null || request.Credit.NativeRequest != null) return;
                request.State = player.CurrentManagedState;
                request.StateRevision = request.Credit.StateRevision;
                request.ExpiresAt = Time.realtimeSinceStartup + .25f;
                request.OwnsNativeSignal = true;
                request.Credit.NativeRequest = request;
                DispatchNativeRequest(request, () => player.Jump());
            }
            finally { ReleaseCommand(request); }
        }
        catch (Exception exception) { Warn(exception); }
    }

    private static void ReleaseCommand(Request request)
    {
        if (ReferenceEquals(_command, request)) _command = null;
    }

    private static bool OwnsSource(Request request)
        => request?.Credit != null && TryGetLocalPlayer(request.Credit.Context, out var player)
            && request.InputEpoch == _inputEpoch
            && ReferenceEquals(player, request.Player) && ReferenceEquals(request.Credit.World, Singleton<GameWorld>.Instance)
            && ReferenceEquals(request.Credit.Game, Singleton<AbstractGame>.Instance);

    private static void DispatchNativeRequest(Request request, Action dispatch)
    {
        try { dispatch(); }
        catch { CancelNativeRequest(request.Credit); throw; }
        finally
        {
            if (!PendingCurrent(request.Credit)) CancelNativeRequest(request.Credit);
            // TryJump emits noise BEFORE setting the bool. A synchronous
            // cancellation may have released it before native code raised it.
            if (!ReferenceEquals(request.Credit.NativeRequest, request))
            {
                request.OwnsNativeSignal = true;
                ReleaseNativeSignal(request);
            }
        }
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

    private static bool CanUse(MovementState state, Player player, Credit credit, bool coyote = false)
    {
        if (credit == null || (credit.Spent && !coyote) || credit.ApplyingImpulse || credit.NativeRequest != null || !OwnsCredit(state, player, credit)) return false;
        return IsAirborneActionAllowed(state, player, coyote: coyote) && player.Physical.CanJump;
    }

    private static bool IsGroundLocomotion(MovementState state)
        => state != null && (state.GetType() == typeof(IdlePlayerState)
            || state.GetType() == typeof(MovePlayerState) || state.GetType() == typeof(SprintPlayerState));

    private static bool IsAirborneActionAllowed(MovementState state, Player player, bool entering = false, bool coyote = false)
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
        return supported && Settings.MovementEnabled.Value && (coyote || Settings.DoubleJumpEnabled.Value)
            && !context.IsGrounded && !ArenaAirVault.HasPendingRequest(player)
            && !context.IsInPronePose && !context.IsInMountedState
            && context.CanJump && player.Physical != null
            && Singleton<GlobalConfiguration>.Instantiated
            && !(VaultingTiming(player) > Singleton<GlobalConfiguration>.Instance.VaultingSettings.VaultingInputTime)
            && !context.PlayerAnimatorGetIsVaulting();
    }

    private static bool TryGetImpulse(JumpPlayerState jump, Credit credit, out Vector3 lift, out Vector3 velocity, bool coyote = false)
    {
        lift = jump._liftForce;
        velocity = Vector3.zero;
        float height = coyote ? 1f : Settings.DoubleJumpHeightMult.Value;
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
        ArenaJumpReliability.JumpEntered(jump);
        if (_credit?.NativeRequest != null && !PendingCurrent(_credit)) CancelNativeRequest(_credit);
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
            if (request.NativeReservation.GroundSlide)
            {
                // Buffered landing-slide input requested a NORMAL ground jump.
                // Native Enter owns all lift/debit; a passive FallDown never
                // becomes an artificial jump merely because that bool was set.
                if (ReferenceEquals(request.Credit.NativeRequest, request.NativeReservation))
                    CancelNativeRequest(request.Credit);
                return;
            }
            // The native Enter just paid OnJump, possibly invoking synchronous
            // body/session/settings callbacks. Never debit a second time here.
            if (!OwnsCredit(jump, request.Player, request.Credit)
                || !ReferenceEquals(request.Credit.NativeRequest, request.NativeReservation)
                || request.StateRevision != request.Credit.StateRevision
                || (request.NativeReservation.Coyote && !ArenaJumpReliability.IsCoyoteCurrent(request.Player, request.NativeReservation.CoyoteEpoch))
                || !IsAirborneActionAllowed(jump, request.Player, entering: true, coyote: request.NativeReservation.Coyote)
                || !TryGetImpulse(jump, request.Credit, out Vector3 lift, out Vector3 velocity, request.NativeReservation.Coyote)) return;
            request.Credit.NativeRequest = null;
            if (jump.GetType() == typeof(FallDown)) ReleaseNativeSignal(request.NativeReservation);
            // A coyote request that entered native Jump retains normal push/lift
            // timing. FallDown deliberately erased lift and needs reconstruction.
            if (!request.NativeReservation.Coyote || jump.GetType() == typeof(FallDown))
                ApplyImpulse(jump, lift, velocity);
            if (ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled)
                ArenaMode.Diagnostics.ArenaLog.Developer("Arena double jump: completed local ledge jump.");
        }
        catch (Exception exception) { Warn(exception); }
        finally
        {
            if (ReferenceEquals(request.Credit.NativeRequest, request.NativeReservation))
                CancelNativeRequest(request.Credit);
        }
    }

    private sealed class ArenaDoubleJumpJumpEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter));
        [PatchPrefix]
        private static void Prefix(JumpPlayerState __instance, out Request __state)
            => __state = __instance.GetType() == typeof(JumpPlayerState) ? CaptureNativeEntry(__instance) : null;
        // ArenaMovementJumpHeightPatch must have applied the existing first-jump multiplier.
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

    private sealed class ArenaDoubleJumpFallEnterPatch : ModulePatch
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

    private sealed class ArenaDoubleJumpLandingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.CheckFlying));
        [PatchPostfix]
        private static void Postfix(MovementContext __instance)
        {
            try
            {
                ArenaJumpReliability.ObserveGround(__instance);
                if (_credit?.NativeRequest != null && !PendingCurrent(_credit)) CancelNativeRequest(_credit);
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
                    && (credit.Player.CharacterController.isGrounded || ArenaBunnyhop.HasRecentPhysicalContact(__instance)) && landedState
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

    private sealed class ArenaDoubleJumpJumpCommandPatch : ModulePatch
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
                if (_command != null || !TryGetLocalPlayer(player?.MovementContext, out var admitted)
                    || !ReferenceEquals(admitted, player)) return;
                // The lock begins BEFORE pose/noise/native translator callbacks,
                // not only at stamina debit. A nested original may still run,
                // but it cannot claim a second Arena command or credit.
                _command = __state = new Request { Player = player, InputEpoch = _inputEpoch };
                ArenaSlideTransitions.PrepareJump(player);
                ArenaJumpReliability.PrepareJump(player);
                MovementState state = player?.CurrentManagedState;
                // Settle an older air request before vanilla handles a new ground
                // press. UI blocking may have delayed the landing observer; the
                // new ground command must own its freshly raised native bool.
                if (state?.MovementContext.IsGrounded == true && _credit != null
                    && ReferenceEquals(_credit.Player, player) && ReferenceEquals(_credit.Context, state.MovementContext))
                    CancelNativeRequest(_credit);
                Credit credit = Observe(state?.MovementContext);
                __state.State = state;
                __state.Credit = credit;
                if (credit == null) return;
                if (_slideRequest != null && OwnsSource(_slideRequest) && ArenaSlideTransitions.HasActiveSlide(player)
                    && _slideRequest.SlideEpoch == ArenaSlideTransitions.OwnerEpoch
                    && Finite(Time.realtimeSinceStartup) && Time.realtimeSinceStartup >= _slideRequest.CreatedAt
                    && Time.realtimeSinceStartup <= _slideRequest.ExpiresAt) return;
                _slideRequest = null;
                float bufferWindow = ArenaSlideTransitions.BufferedJumpWindow(player);
                if (ArenaSlideTransitions.NeedsBufferedJump(player) && Finite(Time.realtimeSinceStartup)
                    && Finite(bufferWindow) && bufferWindow > 0f && bufferWindow <= 1f)
                {
                    // Native sprint exit may last about .7 s; only the slide's
                    // exact witnessed exit receives 1 s. JumpLanding keeps .5 s.
                    // Repeated commands above never extend the captured deadline.
                    _slideRequest = new Request { Player = player, Credit = credit, GroundSlide = true,
                        SlideEpoch = ArenaSlideTransitions.OwnerEpoch, InputEpoch = _inputEpoch,
                        CreatedAt = Time.realtimeSinceStartup, ExpiresAt = Time.realtimeSinceStartup + bufferWindow };
                    return;
                }
                if (ArenaBunnyhop.TryBufferJump(player)) return;
                bool coyote = CanUse(state, player, credit, coyote: true) && ArenaJumpReliability.TryConsumeCoyote(player);
                if (coyote || CanUse(state, player, credit))
                {
                    __state.StateRevision = credit.StateRevision;
                    __state.Coyote = coyote;
                    __state.CoyoteEpoch = ArenaJumpReliability.Epoch;
                    __state.ExecuteImpulse = true;
                }
            }
            catch (Exception exception) { Warn(exception); }
        }

        [PatchPostfix]
        private static void Postfix(MoveInputTranslator __instance, Request __state)
        {
            if (__state == null) return;
            try
            {
                if (!__state.ExecuteImpulse) return;
                MovementState state = __state.State;
                Player player = __state.Player;
                if (!ReferenceEquals(__instance._controller, player) || !CanUse(state, player, __state.Credit, __state.Coyote)
                    || __state.InputEpoch != _inputEpoch
                    || (__state.Coyote && !ArenaJumpReliability.IsCoyoteCurrent(player, __state.CoyoteEpoch))
                    || __state.StateRevision != __state.Credit.StateRevision) return;
                if (state is not JumpPlayerState jump)
                {
                    if ((!__state.Coyote && !Finite(Settings.DoubleJumpHeightMult.Value)) || !Finite(Time.realtimeSinceStartup)) return;
                    // Idle/Move.Jump refuses !IsGrounded. Ask the native animator
                    // directly once, never fake ground/CanJump. Actual Jump or
                    // FallDown Enter owns the debit; an absent transition pays none.
                    if (!__state.Coyote) __state.Credit.Spent = true;
                    __state.ExpiresAt = Time.realtimeSinceStartup + 0.5f;
                    __state.OwnsNativeSignal = true;
                    __state.Credit.NativeRequest = __state;
                    DispatchNativeRequest(__state, () => state.MovementContext.TryJump());
                    return;
                }
                if (!TryGetImpulse(jump, __state.Credit, out Vector3 lift, out Vector3 velocity, __state.Coyote)) return;
                if (!__state.Coyote) __state.Credit.Spent = true; // Before callbacks, including failure and re-entry.
                __state.Credit.ApplyingImpulse = true;
                player.Physical.OnJump(true);
                // Paying the normal cost may exhaust stamina. Recheck ownership
                // and restrictions, but never demand CanJump again after debit.
                if (!OwnsCredit(jump, player, __state.Credit)
                    || __state.InputEpoch != _inputEpoch
                    || __state.StateRevision != __state.Credit.StateRevision
                    || (__state.Coyote && !ArenaJumpReliability.IsCoyoteCurrent(player, __state.CoyoteEpoch))
                    || !IsAirborneActionAllowed(jump, player, coyote: __state.Coyote)) return;
                // OnJump may synchronously clear/refresh a powerup. Use the live
                // bonus after that callback, without a second stamina debit.
                if (!TryGetImpulse(jump, __state.Credit, out lift, out velocity, __state.Coyote)) return;
                ApplyImpulse(jump, lift, velocity);
                jump.MovementContext.EmitJumpNoise();
                if (ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled)
                    ArenaMode.Diagnostics.ArenaLog.Developer("Arena double jump: renewed local vertical impulse.");
            }
            catch (Exception exception) { Warn(exception); }
            finally
            {
                if (__state.Credit != null) __state.Credit.ApplyingImpulse = false;
            }
        }

        [PatchFinalizer, HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception, Request __state)
        {
            ReleaseCommand(__state);
            return __exception;
        }
    }

    private sealed class ArenaDoubleJumpInputOwnerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(PlayerOwner), nameof(PlayerOwner.CreateInputTranslators));
        [PatchPrefix]
        private static void Prefix(PlayerOwner __instance)
        {
            if (__instance?.Player == null) return;
            if (!ReferenceEquals(__instance?.Player, _credit?.Player)
                && !ReferenceEquals(__instance?.Player, _command?.Player)) return;
            unchecked { _inputEpoch++; }
            _slideRequest = null;
            if (_credit == null) return;
            Invalidate(_credit);
            CancelNativeRequest(_credit);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool FinitePositive(float value) => Finite(value) && value > 0f;
}
