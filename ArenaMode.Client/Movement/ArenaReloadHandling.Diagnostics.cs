using AnimationSystem;
using ArenaMode.Config;
using EFT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArenaMode.Movement;

internal static partial class ArenaReloadHandling
{
    private sealed class ReloadDiagnostic
    {
        internal Player.FirearmController.FirearmOperation Operation;
        internal long Started;
        internal int Tick;
        internal float Factor;
        internal readonly HashSet<string> Events = new();
    }

    private static ConditionalWeakTable<Player.FirearmController, ReloadDiagnostic> Diagnostics = new();
    private static int _diagnosticLines;
    private const int DiagnosticLimit = 96;

    private static void ResetDiagnostics()
    {
        Diagnostics = new();
        _diagnosticLines = 0;
    }

    // This observer never admits a speed lease. Even factor=1 reloads are useful
    // controls; other players and ordinary raids must not acquire diagnostics.
    private static void ObserveReload(Player.FirearmController controller)
    {
        try
        {
            if (!_enabled || Plugin.ModLogger == null || _diagnosticLines >= DiagnosticLimit) return;
            var operation = controller.CurrentOperation;
            if ((operation is not Player.FirearmController.BaseReloadOperation && !IsMagazineOperation(operation))
                || operation.State != Player.EOperationState.Executing
                || !ArenaMovementScope.IsLocal(operation.Player)
                || !ReferenceEquals(operation.Controller, controller)
                || !ReferenceEquals(operation.Player.HandsController, controller)) return;
            float factor = Settings.WeaponReloadSpeedMultiplier?.Value ?? 1f;
            if (!Diagnostics.TryGetValue(controller, out var diagnostic)
                || !ReferenceEquals(diagnostic.Operation, operation))
            {
                EndReloadDiagnostic(controller, null, "operation-changed");
                diagnostic = new ReloadDiagnostic { Operation = operation, Started = Stopwatch.GetTimestamp(), Factor = factor };
                Diagnostics.Add(controller, diagnostic);
                TraceReload(controller, "begin");
            }
            diagnostic.Tick++;
            if (!factor.Equals(diagnostic.Factor))
            {
                diagnostic.Factor = factor;
                // Factor changes are globally bounded too, without growing a
                // set of arbitrary float strings throughout a long reload.
                TraceReload(controller, "factor-changed", once: false);
            }
        }
        catch { /* Diagnostics may never change reload admission or cleanup. */ }
    }

    private static void SampleReload(Player.FirearmController controller)
    {
        try
        {
            if (!Diagnostics.TryGetValue(controller, out var diagnostic)) return;
            // Subsequent frames verify readback after native animation advances.
            // Later samples are needed when the first tick still shows IDLE.
            if (diagnostic.Tick == 2 || diagnostic.Tick == 8 || diagnostic.Tick == 30)
                TraceReload(controller, "readback-tick-" + diagnostic.Tick);
        }
        catch { }
    }

    private static void EndReloadDiagnostic(Player.FirearmController controller,
        Player.FirearmController.FirearmOperation operation, string reason)
    {
        try
        {
            if (controller == null || !Diagnostics.TryGetValue(controller, out var diagnostic)
                || (operation != null && !ReferenceEquals(operation, diagnostic.Operation))) return;
            TraceReload(controller, reason);
            Diagnostics.Remove(controller);
        }
        catch { }
    }

    private static string AnimatorDiagnostic(IAnimator animator, int layer)
    {
        if (animator == null) return "missing";
        try
        {
            string info = FormattableString.Invariant($"{animator.GetType().Name}/reload={AnimationControllerParametersTable.GetFloatSpeedReload(animator):0.###}");
            if (layer >= 0 && animator is UnityAnimatorWrapper unity && unity.Animator != null && unity.Animator.layerCount > layer)
            {
                // EFT's generic state wrapper discards Unity speedMultiplier.
                // Read the real value, never interpret state.speed alone as it.
                var state = unity.Animator.GetCurrentAnimatorStateInfo(layer);
                info += FormattableString.Invariant($"/state={state.fullPathHash}/time={state.normalizedTime:0.###}/speed={state.speed:0.###}/mult={state.speedMultiplier:0.###}");
            }
            return info;
        }
        catch { return "unavailable"; }
    }

    private static void TraceReload(Player.FirearmController controller, string reason, bool once = true)
    {
        try
        {
            if (Plugin.ModLogger == null || _diagnosticLines >= DiagnosticLimit || controller == null
                || !Diagnostics.TryGetValue(controller, out var diagnostic)
                || (once && !diagnostic.Events.Add(reason))) return;
            _diagnosticLines++;
            var operation = diagnostic.Operation;
            Leases.TryGetValue(controller, out var lease);
            double elapsed = (Stopwatch.GetTimestamp() - diagnostic.Started) * 1000d / Stopwatch.Frequency;
            string baseline = lease == null ? "none" : FormattableString.Invariant($"{lease.Arms.Original:0.###}/{lease.Body.Original:0.###}");
            string written = lease == null ? "none" : FormattableString.Invariant($"{lease.Arms.Written:0.###}/{lease.Body.Written:0.###}");
            Plugin.ModLogger?.LogInfo($"Arena reload diagnostic: {reason}; weapon={operation.Weapon?.TemplateId}; "
                + FormattableString.Invariant($"operation={operation.GetType().Name}; factor={diagnostic.Factor:0.###}; tick={diagnostic.Tick}; observedMs={elapsed:0}; ")
                + $"baseline={baseline}; written={written}; arms=[{AnimatorDiagnostic(controller.FirearmsAnimator?.Animator, 1)}]; "
                + $"body=[{AnimatorDiagnostic(operation.Player?.MovementContext?.PlayerAnimator?.Animator, -1)}]"
                + (_diagnosticLines == DiagnosticLimit ? "; per-raid diagnostic limit reached" : ""));
        }
        catch { /* Missing objects or a broken log sink are not gameplay errors. */ }
    }
}
