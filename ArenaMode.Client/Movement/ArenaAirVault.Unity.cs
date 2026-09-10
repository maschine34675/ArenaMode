using EFT;
using EFT.Vaulting;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

internal static partial class ArenaAirVault
{
    private sealed class UnityTransfer
    {
        internal PlayerStateContainer Destination;
        internal MovementState Movement;
        internal bool TimeoutReported;
    }

    private static bool UnityReady(Request request)
        => OwnsTarget(request) && request.UnityAnimator != null
            && request.UnityAnimator.isActiveAndEnabled && request.UnityAnimator.isInitialized
            && request.UnityAnimator.speed > 0f && !float.IsInfinity(request.UnityAnimator.speed);

    private static bool UnitySourceOwnsJump(Request request)
        => UnitySourceRejection(request) == null;

    private static string UnitySourceRejection(Request request)
    {
        if (!UnityReady(request)) return "animator-not-ready";
        var animator = request.UnityAnimator;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        // Native PLAYER_DEFAULT_ANIMATOR_CONTROLLER has four jump leaves.
        // Moving and sprinting jumps have their OWN initialized containers; all
        // use the cached EPlayerState.Jump object (CreateUnique=false).
        int ownerHash = state.fullPathHash;
        string ownerName;
        switch (state.fullPathHash)
        {
            case 232800732: ownerName = "Base Layer.JUMP.Jump_Idle"; break;
            case 1056621489: ownerName = "Base Layer.JUMP.Jump_Move"; break;
            case 1332272753: ownerName = "Base Layer.JUMP.Jump_Sprint"; break;
            case 1889265959: ownerHash = 232800732; ownerName = "Base Layer.JUMP.Jump_Idle"; break;
            default: return "unsupported-jump-leaf";
        }
        if (float.IsNaN(state.normalizedTime) || float.IsInfinity(state.normalizedTime)
            || !animator.HasState(0, state.fullPathHash)) return "invalid-jump-state";
        // Unity Jump_Loop is behaviorless and retains the initialized Jump_Idle
        // owner. Unlike FastAnimator, Unity has no timed Idle -> Loop graph edge.
        PlayerStateContainer owner = null;
        foreach (var behavior in animator.GetBehaviours<PlayerStateContainer>())
        {
            if (behavior.StateFullNameHash != ownerHash || behavior.StateFullName != ownerName) continue;
            if (owner != null) return "ambiguous-jump-container";
            if (!ReferenceEquals(behavior.EncapsulatedState, request.Jump)) return "foreign-jump-owner";
            owner = behavior;
        }
        if (owner == null) return "missing-jump-container";
        return ReferenceEquals(request.Jump.MovementContext, request.Context) ? null : "foreign-jump-context";
    }

    private static void TraceUnityRejection(Request request, string stage, string reason)
    {
        if (request?.UnityAnimator == null) return;
        Trace(request, "Unity rejected " + stage + ": " + reason, detail: () =>
        {
            var animator = request.UnityAnimator;
            bool transition = animator.IsInTransition(0);
            return $"state={animator.GetCurrentAnimatorStateInfo(0).fullPathHash}, transition={transition}, "
                + $"next={(transition ? animator.GetNextAnimatorStateInfo(0).fullPathHash : 0)}, "
                + $"active={animator.isActiveAndEnabled}, initialized={animator.isInitialized}, speed={animator.speed:0.###}, "
                + $"update={request.Player.BodyUpdateMode}, phase={request.Jump._jumpState}, held={request.Player.IsVaultingPressed}, "
                + $"grounded={request.Context.IsGrounded}, jump={animator.GetBool(PlayerAnimator.JUMP_PARAM_HASH)}, "
                + $"landing={animator.GetBool(PlayerAnimator.LANDING_PARAM_HASH)}, falling={animator.GetBool(PlayerAnimator.FALLING_DOWN_PARAM_HASH)}, "
                + $"climb={request.Animator.GetDoClimb()}, vault={request.Animator.GetDoVault()}, "
                + $"height={request.Component.VaultingHeight:0.###}, length={request.Component.VaultingLength:0.###}";
        });
    }

    private static bool UnityNativeJumpCanYield(Animator animator)
        => !animator.IsInTransition(0) && !animator.GetBool(PlayerAnimator.LANDING_PARAM_HASH)
            && !animator.GetBool(PlayerAnimator.FALLING_DOWN_PARAM_HASH)
            && animator.GetBool(PlayerAnimator.JUMP_PARAM_HASH);

    private static PlayerStateContainer UnityDestination(Pending pending)
    {
        bool climb = pending.Strategy == EVaultingStrategy.Climb;
        int hash = climb ? -285230529 : 262098243;
        string name = climb ? "Base Layer.Vaulting.Climb_on" : "Base Layer.Vaulting.Vault";
        var request = pending.Request;
        if (!request.UnityAnimator.HasState(0, hash)) return null;
        PlayerStateContainer found = null;
        foreach (var behavior in request.UnityAnimator.GetBehaviours<PlayerStateContainer>())
        {
            if (behavior.StateFullNameHash != hash || behavior.StateFullName != name) continue;
            if (found != null || behavior.DisableRootMotion || behavior.EncapsulatedState is not MovementState movement
                || !ReferenceEquals(movement.MovementContext, request.Context)
                || (climb ? movement.GetType() != typeof(ClimbState) : movement.GetType() != typeof(VaultState))) return null;
            found = behavior;
        }
        return found;
    }

    private static void ObserveUnityEntry(Pending pending, bool nativeFailure = false)
    {
        var request = pending.Request;
        var target = pending.Unity.Movement;
        // Cleanup follows the captured native body, not the now mutable Movement
        // setting/input gate. Turning it off after dispatch must still consume Jump.
        bool ownsBody = OwnsTarget(request) && ReferenceEquals(request.World.MainPlayer, request.Player)
            && request.Game is LocalGame game && ReferenceEquals(game.LocalPlayer, request.Player);
        if (ownsBody
            && target is VaultingMovementState && ReferenceEquals(request.Context.CurrentState, target)
            && ReferenceEquals(request.Player.CurrentManagedState, target)
            && request.Context.OverridenState == null && request.Context.OverridenControlsState == null
            && ReferenceEquals(pending.Unity.Destination.EncapsulatedState, target)
            && UnityDestinationIsActive(pending)
            && ReferenceEquals(UnityDestination(pending), pending.Unity.Destination))
        {
            request.Context.PlayerAnimatorEnableJump(false);
            if (ReferenceEquals(_pending, pending)) _pending = null;
            Trace(request, nativeFailure ? "Unity target owns movement after native callback error; see original exception"
                : "Unity entered " + pending.Strategy + " through native state callback", entered: true);
        }
        else if (!ownsBody || !ReferenceEquals(pending.Unity.Destination.EncapsulatedState, target)
            || (!ReferenceEquals(request.Context.CurrentState, request.Jump)
                && !ReferenceEquals(request.Context.CurrentState, target)))
        {
            if (ReferenceEquals(_pending, pending)) _pending = null;
        }
        else if (Time.unscaledTime >= pending.ExpiresAt && !pending.Unity.TimeoutReported)
        {
            // CrossFade is the commit boundary, like an accepted standing vault.
            // Never rewind the graph or clear its flags after dispatch: sibling
            // animation-event behaviors and native capsule work may already run.
            // A failed entry is diagnostic, not permission to fake/force a move.
            pending.Unity.TimeoutReported = true;
            _blockedUntilRelease = request.Player;
            Trace(request, "Unity native entry delayed; retaining committed observer until entry or body/state replacement");
        }
    }

    private static bool UnityDestinationIsActive(Pending pending)
    {
        var animator = pending.Request.UnityAnimator;
        int hash = pending.Unity.Destination.StateFullNameHash;
        return animator.GetCurrentAnimatorStateInfo(0).fullPathHash == hash
            || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == hash);
    }

    private sealed class UnityBodyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.BodyUpdate));

        [PatchPrefix]
        private static void Prefix(Player __instance)
        {
            var pending = _pending;
            if (pending == null || pending.Unity != null || !ReferenceEquals(pending.Request.Player, __instance)
                || pending.Request.UnityAnimator == null) return;
            try
            {
                var request = pending.Request;
                var animator = request.UnityAnimator;
                string rejected = !IsEligible(request) ? "request-no-longer-eligible" : UnitySourceRejection(request);
                if (rejected == null && !UnityNativeJumpCanYield(animator)) rejected = "native-jump-flags-or-transition";
                if (rejected == null && !RefreshPending(pending)) rejected = "geometry-stamina-hands-refresh";
                if (rejected != null)
                { TraceUnityRejection(request, "dispatch", rejected); CancelPending(); return; }
                var destination = UnityDestination(pending);
                if (destination == null || !UnitySourceOwnsJump(request)
                    || !UnityNativeJumpCanYield(animator))
                { TraceUnityRejection(request, "dispatch", destination == null ? "missing-or-foreign-destination" : "source-changed-after-refresh"); CancelPending(); return; }
                // Fresh native geometry/hands/stamina checks above are the final
                // admission. Release of Space AFTER this point must not cancel an
                // accepted move; native sibling event behaviors would still run.
                // CrossFade queues once; no Update(0), Play or manual state calls.
                pending.Unity = new UnityTransfer { Destination = destination,
                    Movement = (MovementState)destination.EncapsulatedState };
                bool climb = pending.Strategy == EVaultingStrategy.Climb;
                // Native Unity MOVE entry blend and destination offset. The
                // duration is normalized against the source animation by Unity.
                animator.CrossFade(destination.StateFullNameHash, climb ? 0.15f : 0.1f, 0, climb ? 0f : 0.1f);
                Trace(request, "Unity transition requested: " + pending.Strategy);
            }
            catch (Exception ex)
            {
                // Native queue acceptance can precede a callback/logging error.
                // Keep its observer: generic cancellation would strand IsJumping.
                if (pending.Unity == null) CancelPending();
                Warn(ex);
            }
        }
    }

    private sealed class UnityEntryPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(PlayerStateContainer), nameof(PlayerStateContainer.OnStateEnter));

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, PlayerStateContainer __instance,
            Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var pending = _pending;
            if (pending?.Unity == null || layerIndex != 0
                || !ReferenceEquals(animator, pending.Request.UnityAnimator)
                || !ReferenceEquals(__instance, pending.Unity.Destination)
                || stateInfo.fullPathHash != pending.Unity.Destination.StateFullNameHash) return __exception;
            try { ObserveUnityEntry(pending, __exception != null); }
            catch (Exception ex) { Warn(ex); }
            return __exception; // Preserve every native callback exception.
        }
    }
}
