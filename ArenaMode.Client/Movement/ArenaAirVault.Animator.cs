using AnimationEventSystem;
using EFT;
using EFT.Vaulting;
using FastAnimatorSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

internal static partial class ArenaAirVault
{
    private sealed class AnimatorFrame
    {
        internal Pending Pending;
        internal FastAnimatorProcessor Processor;
        internal FastLayerInfo Layer;
        internal AbstractAnimatorControllerState Source;
        internal AnimatorControllerTransition Issued;
    }

    private static AnimatorFrame _animatorFrame;
    private static WeakReference<GameWorld> _traceWorld;
    private static readonly Dictionary<string, float> TraceNext = new();
    private static int _traceCount;

    private static void Trace(Request request, string message, bool entered = false, Func<string> detail = null)
    {
        try
        {
            if (!OwnsTarget(request)) return;
            if (_traceWorld == null || !_traceWorld.TryGetTarget(out var tracedWorld)
                || !ReferenceEquals(tracedWorld, request.World))
            { _traceWorld = new WeakReference<GameWorld>(request.World); TraceNext.Clear(); _traceCount = 0; }
            // Normal held input can request every frame. Keep useful diagnostics
            // without adding a per-frame log flood during long Arena matches.
            if (_traceCount >= 100 || (!entered && TraceNext.TryGetValue(message, out float nextTrace)
                && Time.unscaledTime < nextTrace)) return;
            // Queue/refusal can alternate every frame. Throttle each stable
            // reason independently, not merely the most recent log line.
            TraceNext[message] = Time.unscaledTime + 2f;
            _traceCount++;
            // Throttle by the stable reason above, not changing per-frame geometry.
            // Formatting is lazy and diagnostics must never change gameplay results.
            if (detail != null)
            {
                try { message += "; " + detail(); }
                catch (Exception) { message += "; diagnostic values unavailable"; }
            }
            Plugin.ModLogger.LogInfo("Arena airborne mantle: " + message);
        }
        catch (Exception) { /* Best-effort diagnostics only. */ }
    }

    private static bool OwnsFrame(AnimatorFrame frame)
        => frame != null && ReferenceEquals(_animatorFrame, frame)
            && ReferenceEquals(_pending, frame.Pending) && IsEligible(frame.Pending.Request)
            && ReferenceEquals(frame.Processor, frame.Pending.Request.AnimatorHandle)
            && ReferenceEquals(frame.Processor.FastControllerInfo.GetStateInfo(0), frame.Layer)
            && ReferenceEquals(frame.Layer.CurrentState, frame.Source)
            && SourceOwnsJump(frame)
            && frame.Layer.Transition == null && frame.Layer.ForcePlayState == null;

    private static bool SourceOwnsJump(AnimatorFrame frame)
    {
        var states = frame.Processor.FastAnimatorController.AllStates;
        if (!states.Contains(frame.Source)) return false;
        var behavior = frame.Source.GetBehavior<FastAnimationPlayerStateContainerBehaviour>();
        if (behavior != null) return ReferenceEquals(behavior.EncapsulatedState, frame.Pending.Request.Jump);
        // The actual Jump_Loop leaf has no PlayerContainer. After Jump_Idle's
        // timed exit it deliberately retains that state's managed Jump owner.
        // Prove that exact incoming relationship, not any behaviorless node.
        if (frame.Source.Name != "Base Layer.JUMP.Jump_Loop" || frame.Source.Hash != 1889265959
            || frame.Source.BehaviorsCount != 0 || frame.Source.ParentStateMachine?.Name != "Base Layer.JUMP"
            || frame.Source.ParentStateMachine.Hash != -1804865118) return false;
        foreach (var state in states)
        {
            if (state is not AbstractAnimatorControllerState idle || idle.Name != "Base Layer.JUMP.Jump_Idle" || idle.Hash != 232800732
                || !ReferenceEquals(idle.ParentStateMachine, frame.Source.ParentStateMachine)
                || !ReferenceEquals(idle.GetBehavior<FastAnimationPlayerStateContainerBehaviour>()?.EncapsulatedState,
                    frame.Pending.Request.Jump)) continue;
            foreach (var edge in idle.GetOutTransitions())
                if (ReferenceEquals(edge.SourceState, idle) && ReferenceEquals(edge.DestinationState, frame.Source)) return true;
        }
        return false;
    }

    private static AnimatorControllerTransition FindNativeEntry(AnimatorFrame frame)
    {
        bool climb = frame.Pending.Strategy == EVaultingStrategy.Climb;
        string name = climb ? "Base Layer.Vaulting.Climb_on" : "Base Layer.Vaulting.Vault";
        int hash = climb ? -285230529 : 262098243;
        AnimatorControllerTransition found = null;
        foreach (var state in frame.Processor.FastAnimatorController.AllStates)
        {
            if (state is not AbstractAnimatorControllerState move
                || move.Name != "Base Layer.StateMachine_Move.MOVE" || move.Hash != 886311417) continue;
            foreach (var edge in move.GetOutTransitions())
            {
                if (edge.DestinationState?.Name != name || edge.DestinationState.Hash != hash) continue;
                var target = edge.DestinationState.GetBehavior<FastAnimationPlayerStateContainerBehaviour>()?.EncapsulatedState;
                if (found != null || edge.IsMuted || edge.IsSolo || edge.HasExitTime
                    || target is not MovementState movement
                    || !ReferenceEquals(movement.MovementContext, frame.Pending.Request.Context)
                    || (climb ? target.GetType() != typeof(ClimbState) : target.GetType() != typeof(VaultState))) return null;
                found = edge;
            }
        }
        return found;
    }

    private sealed class AnimatorFramePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(FastAnimatorProcessor), nameof(FastAnimatorProcessor.ProcessState));

        [PatchPrefix]
        private static void Prefix(FastAnimatorProcessor __instance, FastLayerInfo layerInfo,
            int layerIndex, int recursionLevel, out AnimatorFrame __state)
        {
            __state = _animatorFrame;
            _animatorFrame = null; // Nested/foreign processors cannot borrow the outer lease.
            Pending pending = _pending;
            if (pending == null || layerIndex != 0 || recursionLevel != 0
                || !ReferenceEquals(__instance, pending.Request.AnimatorHandle)) return;
            try
            {
                var frame = new AnimatorFrame { Pending = pending, Processor = __instance,
                    Layer = layerInfo, Source = layerInfo?.CurrentState };
                if (frame.Source == null) return;
                _animatorFrame = frame;
                if (!OwnsFrame(frame)) _animatorFrame = null;
            }
            catch (Exception ex) { _animatorFrame = null; Warn(ex); }
        }

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, AnimatorFrame __state)
        {
            AnimatorFrame frame = _animatorFrame;
            _animatorFrame = __state;
            if (frame?.Issued == null) return __exception;
            try
            {
                Request request = frame.Pending.Request;
                bool owns = OwnsTarget(request) && ReferenceEquals(request.World.MainPlayer, request.Player)
                    && request.Game is LocalGame local && ReferenceEquals(local.LocalPlayer, request.Player)
                    && ReferenceEquals(frame.Processor.FastControllerInfo.GetStateInfo(0), frame.Layer);
                var target = frame.Issued.DestinationState.GetBehavior<FastAnimationPlayerStateContainerBehaviour>()?.EncapsulatedState;
                if (owns && ReferenceEquals(request.Context.CurrentState, target)
                    && ReferenceEquals(request.Player.CurrentManagedState, target)
                    && request.Context.OverridenState == null && request.Context.OverridenControlsState == null
                    && (ReferenceEquals(frame.Layer.Transition, frame.Issued)
                        || ReferenceEquals(frame.Layer.CurrentState, frame.Issued.DestinationState)))
                {
                    // Jump.Exit does not clear this bool; normally landing does.
                    // Only native target entry commits our airborne handoff.
                    request.Context.PlayerAnimatorEnableJump(false);
                    BeginMotionObservation(request, request.Context.CurrentState as VaultingMovementState);
                    Trace(request, "native " + frame.Pending.Strategy + " entered" +
                        (__exception == null ? "" : "; native callback failed after entry"), entered: true);
                    if (ReferenceEquals(_pending, frame.Pending)) _pending = null;
                }
                else
                {
                    // A native callback can throw after installing our edge but
                    // before target Enter. Do not leave that edge to resume next
                    // frame without the target's capsule/stamina initialization.
                    if (owns && ReferenceEquals(request.Context.CurrentState, request.Jump)
                        && ReferenceEquals(frame.Layer.Transition, frame.Issued))
                    {
                        frame.Layer.Transition = null;
                        frame.Layer.TransitionAbsTime = 0f;
                        frame.Layer.DestinationStateAbsoluteTime = 0f;
                        frame.Source.MarkTransitionDirty();
                        _blockedUntilRelease = request.Player;
                    }
                    if (ReferenceEquals(_pending, frame.Pending))
                    { if (owns) CancelPending(); else _pending = null; }
                }
            }
            catch (Exception ex) { Warn(ex); }
            return __exception; // Never hide a native callback failure.
        }
    }

    private sealed class AnimatorTransitionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(AbstractAnimatorControllerState), nameof(AbstractAnimatorControllerState.GetTriggeredTransition));

        [PatchPostfix]
        private static void Postfix(AbstractAnimatorControllerState __instance, ref AnimatorControllerTransition __result)
        {
            AnimatorFrame frame = _animatorFrame;
            // A native landing/death/other transition always wins.
            if (__result != null || frame == null || frame.Issued != null || !ReferenceEquals(frame.Source, __instance)) return;
            try
            {
                if (!OwnsFrame(frame)) return;
                if (!RefreshPending(frame.Pending) || !OwnsFrame(frame))
                { if (ReferenceEquals(_pending, frame.Pending)) CancelPending(); return; }
                var entry = FindNativeEntry(frame);
                if (entry == null || !entry.InternalCheckConditions())
                { Trace(frame.Pending.Request, "native animator entry is unavailable"); return; }
                float sourceDuration = frame.Source.MotionDuration;
                float destinationDuration = entry.DestinationState.MotionDuration;
                float seconds = entry.IsFixedDuration ? entry.Duration : entry.Duration * entry.SourceState.MotionDuration;
                if (!(sourceDuration > 0f && destinationDuration > 0f && seconds > 0f)
                    || !Finite(sourceDuration) || !Finite(destinationDuration) || !Finite(seconds)
                    || !Finite(entry.NormalizedDestinationStateOffset) || !OwnsFrame(frame)) return;
                float normalizedDuration = seconds / sourceDuration;
                if (!(normalizedDuration > 0f) || !Finite(normalizedDuration)) return;
                // Native ProcessState completion multiplies Duration by source
                // motion duration even for fixed-duration edges. Normalize the
                // verified standing blend seconds to THIS jump source instead.
                var transition = new AnimatorControllerTransition("Arena airborne mantle", 0,
                    frame.Source, entry.DestinationState, false, false, false, 0f,
                    normalizedDuration, false, entry.NormalizedDestinationStateOffset,
                    Array.Empty<TransitionCondition>(), entry.InterruptionSource, entry.IsOrderedInterruption);
                frame.Issued = transition;
                __result = transition;
                // This edge is never added to a controller list and owns no
                // parameter listeners. The normal engine now runs target Enter
                // before old Jump Move, with full native Exit/Enter/cleanup.
            }
            catch (Exception ex) { Warn(ex); }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
