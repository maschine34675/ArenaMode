using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

internal static partial class ArenaAirVault
{
    // Read-only evidence, separately budgeted from admission refusals. Never
    // correct position, root motion, capsule, grounding or native state here.
    private sealed class MotionObservation
    {
        internal Request Request;
        internal VaultingMovementState Movement;
        internal object Controller;
        internal Vector3 Start, Requested, Applied;
        internal float StartedAt, ExitAt = -1f, PeakRise, MinCapsuleHeight;
        internal int Id, Steps;
        internal bool NativeError;
    }

    private static MotionObservation _motionObservation;
    private static WeakReference<GameWorld> _motionWorld;
    private static int _motionCount;

    private static bool OwnsObservation(MotionObservation observation)
        => observation != null && OwnsTarget(observation.Request)
            && ArenaMovementScope.IsLocal(observation.Request.Player)
            && ReferenceEquals(observation.Request.World.MainPlayer, observation.Request.Player)
            && observation.Request.Game is LocalGame game
            && ReferenceEquals(game.LocalPlayer, observation.Request.Player)
            && ReferenceEquals(observation.Request.Context.CharacterController, observation.Controller);

    private static bool ObservedMoveIsCurrent(MotionObservation observation)
        => ReferenceEquals(observation.Request.Context.CurrentState, observation.Movement)
            && ReferenceEquals(observation.Request.Player.CurrentManagedState, observation.Movement)
            && observation.Request.Context.OverridenState == null
            && observation.Request.Context.OverridenControlsState == null;

    private static void BeginMotionObservation(Request request, VaultingMovementState movement)
    {
        if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) return;
        try
        {
            if (movement == null || !OwnsTarget(request) || !ArenaMovementScope.IsLocal(request.Player)
                || request.Context.CharacterController == null) return;
            if (_motionWorld == null || !_motionWorld.TryGetTarget(out var world)
                || !ReferenceEquals(world, request.World))
            { _motionWorld = new WeakReference<GameWorld>(request.World); _motionCount = 0; }
            if (_motionObservation != null) TickMotionObservation();
            _motionObservation = null;
            if (_motionCount >= 12) return;
            var observation = new MotionObservation { Request = request, Movement = movement,
                Controller = request.Context.CharacterController, Start = request.Context.TransformPosition,
                StartedAt = Time.unscaledTime, Id = ++_motionCount,
                MinCapsuleHeight = request.Context.CharacterController.height };
            if (!OwnsObservation(observation) || !ObservedMoveIsCurrent(observation)) return;
            _motionObservation = observation;
            ObservationLog(observation, "entry", () => FormattableString.Invariant(
                $"height={movement.vaultingHeight:0.###}, length={movement.vaultingLength:0.###}, distance={request.Component._vaultingModelDebug.ObstacleCalculatorModel.DistanceToMainObstacle:0.###}, obstacle={VectorText(request.Component.MaxWeightPointPosition)}, position={VectorText(observation.Start)}, grid={VectorText(request.Context.VaultingGridRoot.position)}, forward={VectorText(request.Context.PlayerRealForward)}, forwardVelocity={request.Component.AbsoluteForwardVelocity:0.###}, capsule={observation.MinCapsuleHeight:0.###}"));
        }
        catch (Exception) { _motionObservation = null; }
    }

    private static void TickMotionObservation()
    {
        if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) { _motionObservation = null; return; }
        var observation = _motionObservation;
        if (observation == null) return;
        try
        {
            if (!OwnsObservation(observation)) { _motionObservation = null; return; }
            var context = observation.Request.Context;
            observation.PeakRise = Mathf.Max(observation.PeakRise, context.TransformPosition.y - observation.Start.y);
            observation.MinCapsuleHeight = Mathf.Min(observation.MinCapsuleHeight, context.CharacterController.height);
            bool timedOut = Time.unscaledTime - observation.StartedAt >= 8f;
            if (observation.ExitAt < 0f && (!ObservedMoveIsCurrent(observation) || timedOut))
            {
                observation.ExitAt = Time.unscaledTime;
                ObservationLog(observation, timedOut ? "timeout" : "exit", () => MotionSummary(observation));
                if (timedOut) _motionObservation = null;
            }
            else if (observation.ExitAt >= 0f && Time.unscaledTime - observation.ExitAt >= 0.75f)
            {
                ObservationLog(observation, "after-exit", () => MotionSummary(observation));
                _motionObservation = null;
            }
        }
        catch (Exception) { _motionObservation = null; }
    }

    private static string MotionSummary(MotionObservation observation)
    {
        var context = observation.Request.Context;
        return FormattableString.Invariant($"elapsed={Time.unscaledTime - observation.StartedAt:0.###}, state={context.CurrentState?.GetType().Name}, grounded={context.IsGrounded}, steps={observation.Steps}, requested={VectorText(observation.Requested)}, applied={VectorText(observation.Applied)}, displacement={VectorText(context.TransformPosition - observation.Start)}, peakRise={observation.PeakRise:0.###}, capsule={context.CharacterController.height:0.###}, minCapsule={observation.MinCapsuleHeight:0.###}, nativeError={observation.NativeError}");
    }

    private static string VectorText(Vector3 vector)
        => FormattableString.Invariant($"({vector.x:0.###},{vector.y:0.###},{vector.z:0.###})");

    private static void ObservationLog(MotionObservation observation, string phase, Func<string> detail)
    {
        if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) return;
        try { ArenaMode.Diagnostics.ArenaLog.Developer($"Arena mantle motion #{observation.Id} {phase}: {detail()}"); }
        catch (Exception) { } // Diagnostic failure cannot affect native movement.
    }

    private struct MotionSample
    {
        internal MotionObservation Observation;
        internal Vector3 Before, Requested;
    }

    private sealed class ArenaAirVaultObservedMotionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(VaultingMovementState), nameof(VaultingMovementState.ApplyMotion));

        [PatchPrefix]
        private static void Prefix(VaultingMovementState __instance, Vector3 motion, out MotionSample __state)
        {
            __state = default;
            var observation = _motionObservation;
            if (observation == null || !ReferenceEquals(observation.Movement, __instance)) return;
            try
            {
                if (observation.ExitAt >= 0f || !OwnsObservation(observation) || !ObservedMoveIsCurrent(observation)) return;
                __state = new MotionSample { Observation = observation,
                    Before = observation.Request.Context.TransformPosition, Requested = motion };
            }
            catch (Exception) { _motionObservation = null; }
        }

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, MotionSample __state)
        {
            var observation = __state.Observation;
            if (observation == null || !ReferenceEquals(observation, _motionObservation)) return __exception;
            try
            {
                if (!OwnsObservation(observation) || !ObservedMoveIsCurrent(observation)) return __exception;
                observation.Requested += __state.Requested;
                var position = observation.Request.Context.TransformPosition;
                observation.Applied += position - __state.Before;
                observation.PeakRise = Mathf.Max(observation.PeakRise, position.y - observation.Start.y);
                observation.Steps++;
                observation.NativeError |= __exception != null;
            }
            catch (Exception) { _motionObservation = null; }
            return __exception;
        }
    }
}
