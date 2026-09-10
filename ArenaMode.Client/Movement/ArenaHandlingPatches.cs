using ArenaMode.Config;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ArenaMode.Movement;

/// <summary>Local landing duration and operation-owned firearm SpeedDraw, never animator.speed.</summary>
internal static class ArenaHandlingPatches
{
    private sealed class DrawLease
    {
        internal Player.FirearmController.FirearmOperation Operation;
        internal IAnimator Animator;
        internal float Original, Written;
    }
    private sealed class NativeSpeedWrite
    {
        internal FirearmsAnimator Owner;
        internal DrawLease Lease;
        internal float Original, Written;
    }
    private static ConditionalWeakTable<FirearmsAnimator, DrawLease> _draws = new();
    private static readonly List<WeakReference<FirearmsAnimator>> DrawOwners = new();
    private static bool _enabled, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            new LandingPatch().Enable();
            new DrawStartPatch().Enable();
            new HolsterStartPatch().Enable();
            new DrawCompletePatch().Enable();
            new SpeedParametersPatch().Enable();
            new ControllerUpdatePatch().Enable();
            new ControllerDestroyPatch().Enable();
            ArenaReloadHandling.Enable();
            RaidLifecycle.OnRaidEnded += ResetPerRaidState;
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        ArenaReloadHandling.Shutdown();
        RaidLifecycle.OnRaidEnded -= ResetPerRaidState;
        ResetPerRaidState();
    }

    internal static void ResetPerRaidState()
    {
        ArenaReloadHandling.ResetPerRaidState();
        foreach (var weak in DrawOwners.ToArray())
            if (weak.TryGetTarget(out var animator)) Release(animator);
        DrawOwners.Clear();
        _draws = new ConditionalWeakTable<FirearmsAnimator, DrawLease>();
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena landing/handling hook failed open: " + ex.Message);
    }

    private static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

    internal static void ApplyLandingLimit(MovementContext context, float speed, Player.ESpeedLimit cause, float duration)
    {
        // This wrapper replaces ONLY the call inside Player.LandingAdjustments.
        // OnJumpEnd also uses Fall, and must keep its ordinary jump penalty.
        try
        {
            if (_enabled && cause == Player.ESpeedLimit.Fall && FinitePositive(duration)
                && ArenaMovementScope.TryGetPlayer(context, out _))
            {
                float factor = Settings.LandingRecoveryMultiplier?.Value ?? 1f;
                if (factor >= .1f && factor <= 1f) duration *= factor;
            }
        }
        catch (Exception ex) { Warn(ex); }
        context.ChangeSpeedLimit(speed, cause, duration);
    }

    private static bool TryFactor(Player.FirearmController.FirearmOperation operation, out float factor)
    {
        factor = 1f;
        if (!_enabled || operation == null || operation.State != Player.EOperationState.Executing
            || !ArenaMovementScope.IsLocal(operation.Player)
            || !ReferenceEquals(operation.Player.HandsController, operation.Controller)
            || !ReferenceEquals(operation.Controller?.CurrentOperation, operation)
            || !ReferenceEquals(operation.Controller.FirearmsAnimator, operation.FirearmsAnimator)
            || operation.Weapon == null || operation.Weapon.IsStationaryWeapon) return false;
        // Restrict to the reviewed native operations; future/specialized states
        // do not inherit timing changes solely through a common base class.
        if (operation.GetType() == typeof(Player.FirearmController.SpawnOperation))
            factor = Settings.WeaponDrawSpeedMultiplier?.Value ?? 1f;
        else if (operation.GetType() == typeof(Player.FirearmController.Remove))
            factor = Settings.WeaponHolsterSpeedMultiplier?.Value ?? 1f;
        else return false;
        return factor >= 1f && factor <= 3f;
    }

    internal static void BeginHandlingOperation(Player.ObjectInHandsOperation operation)
    {
        // The common nonvirtual Start is tiny and may inline on Mono. Replace
        // its calls only in the two large reviewed operation Start methods.
        operation.Start();
        OnOperationStarted(operation);
    }

    internal static void OnOperationStarted(Player.ObjectInHandsOperation operation)
    {
        if (operation is not Player.FirearmController.FirearmOperation firearm) return;
        var animator = firearm.FirearmsAnimator;
        if (animator == null) return;
        try
        {
            // Release a prior/reentered acquisition before using a fresh base.
            Release(animator);
            if (!TryFactor(firearm, out float factor) || factor == 1f) return;
            var native = animator.Animator;
            if (native == null || !animator.HasParameter(AnimationControllerParametersTable.FLOAT_SPEEDDRAW)) return;
            float baseline = AnimationControllerParametersTable.GetFloatSpeedDraw(native);
            float changed = baseline * factor;
            if (!FinitePositive(baseline) || !FinitePositive(changed)) return;
            var lease = new DrawLease { Operation = firearm, Animator = native, Original = baseline, Written = baseline };
            _draws.Add(animator, lease);
            DrawOwners.Add(new WeakReference<FirearmsAnimator>(animator));
            WriteOwned(lease, changed);
        }
        catch (Exception ex) { Release(animator); Warn(ex); }
    }

    private static NativeSpeedWrite OnNativeSpeedParameters(FirearmsAnimator animator, ref float draw)
    {
        if (animator == null || !_draws.TryGetValue(animator, out var lease)) return null;
        try
        {
            // The native setter supplies a fresh unscaled value (skills, injury
            // or fast-switch recalculation). Never multiply our prior output.
            if (!ReferenceEquals(animator.Animator, lease.Animator)
                || !TryFactor(lease.Operation, out float factor) || factor == 1f
                || !FinitePositive(draw) || !FinitePositive(draw * factor))
            { Release(animator); return null; }
            var pending = new NativeSpeedWrite { Owner = animator, Lease = lease, Original = draw, Written = draw * factor };
            draw = pending.Written;
            return pending;
        }
        catch (Exception ex) { Release(animator); Warn(ex); }
        return null;
    }

    private static void CompleteNativeSpeedParameters(NativeSpeedWrite pending, Exception failure)
    {
        if (pending == null || !_draws.TryGetValue(pending.Owner, out var lease)
            || !ReferenceEquals(lease, pending.Lease)) return;
        try
        {
            if (!ReferenceEquals(pending.Owner.Animator, lease.Animator)) { Release(pending.Owner); return; }
            float current = AnimationControllerParametersTable.GetFloatSpeedDraw(lease.Animator);
            if (current == pending.Written)
            {
                lease.Original = pending.Original;
                lease.Written = pending.Written;
            }
            // Preserve ownership of the prior value if native reload/draw write
            // threw before completing. Do not swallow the native exception.
            if (failure != null || current != pending.Written) Release(pending.Owner);
        }
        catch (Exception ex) { Release(pending.Owner); Warn(ex); }
    }

    private static void WriteOwned(DrawLease lease, float value)
    {
        try
        {
            AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, value);
            lease.Written = value;
        }
        catch
        {
            // Some wrappers can throw after forwarding the write. Retain the
            // observed owned value so caller cleanup can still restore it.
            if (AnimationControllerParametersTable.GetFloatSpeedDraw(lease.Animator) == value) lease.Written = value;
            throw;
        }
    }

    internal static void OnControllerUpdate(Player.FirearmController controller)
    {
        ArenaReloadHandling.OnControllerUpdate(controller);
        var animator = controller?.FirearmsAnimator;
        if (animator == null || !_draws.TryGetValue(animator, out var lease)) return;
        try
        {
            if (!ReferenceEquals(animator.Animator, lease.Animator)
                || !TryFactor(lease.Operation, out float factor) || factor == 1f)
            { Release(animator); return; }
            float current = AnimationControllerParametersTable.GetFloatSpeedDraw(lease.Animator);
            if (current != lease.Written)
            {
                // Another writer owns the parameter now. Do not erase or keep
                // re-multiplying its value on subsequent frame updates.
                Release(animator);
                return;
            }
            float changed = lease.Original * factor;
            if (!FinitePositive(changed)) { Release(animator); return; }
            if (changed == lease.Written) return;
            WriteOwned(lease, changed);
        }
        catch (Exception ex) { Release(animator); Warn(ex); }
    }

    internal static void Release(FirearmsAnimator animator)
    {
        if (animator == null || !_draws.TryGetValue(animator, out var lease)) return;
        _draws.Remove(animator);
        DrawOwners.RemoveAll(weak => !weak.TryGetTarget(out var key) || ReferenceEquals(key, animator));
        try
        {
            // Native animator getters can change when a pooled object is reused.
            // Restore only the exact native instance and value we still own.
            if (ReferenceEquals(animator.Animator, lease.Animator)
                && AnimationControllerParametersTable.GetFloatSpeedDraw(lease.Animator) == lease.Written)
                AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, lease.Original);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private sealed class LandingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.LandingAdjustments), new[] { typeof(float) });

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            var target = AccessTools.Method(typeof(MovementContext), nameof(MovementContext.ChangeSpeedLimit),
                new[] { typeof(float), typeof(Player.ESpeedLimit), typeof(float) });
            var calls = result.Where(x => x.Calls(target)).ToArray();
            if (calls.Length != 1) throw new InvalidOperationException("Expected one native hard-landing speed limit call.");
            calls[0].opcode = OpCodes.Call;
            calls[0].operand = AccessTools.Method(typeof(ArenaHandlingPatches), nameof(ApplyLandingLimit));
            return result;
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceOperationStarts(IEnumerable<CodeInstruction> instructions, int expected)
    {
        var result = instructions.ToList();
        var target = AccessTools.Method(typeof(Player.ObjectInHandsOperation), nameof(Player.ObjectInHandsOperation.Start), Type.EmptyTypes);
        var calls = result.Where(x => x.Calls(target)).ToArray();
        if (calls.Length != expected) throw new InvalidOperationException("Native firearm operation Start call count changed.");
        foreach (var call in calls)
        {
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.Method(typeof(ArenaHandlingPatches), nameof(BeginHandlingOperation));
        }
        return result;
    }
    private sealed class DrawStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController.SpawnOperation), nameof(Player.FirearmController.SpawnOperation.Start), new[] { typeof(Action) });
        [PatchTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => ReplaceOperationStarts(instructions, 1);
    }
    private sealed class HolsterStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController.Remove), nameof(Player.FirearmController.Remove.Start), new[] { typeof(Action), typeof(bool), typeof(Item) });
        [PatchTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => ReplaceOperationStarts(instructions, 2);
    }
    private sealed class DrawCompletePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController.SpawnOperation), nameof(Player.FirearmController.SpawnOperation.WeaponAppeared), Type.EmptyTypes);
        [PatchPrefix] private static void Prefix(Player.FirearmController.SpawnOperation __instance) => Release(__instance?.FirearmsAnimator);
    }
    private sealed class SpeedParametersPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(FirearmsAnimator), nameof(FirearmsAnimator.SetSpeedParameters), new[] { typeof(float), typeof(float) });
        [PatchPrefix] private static void Prefix(FirearmsAnimator __instance, ref float draw, out NativeSpeedWrite __state)
            => __state = OnNativeSpeedParameters(__instance, ref draw);
        [PatchFinalizer] private static Exception Finalizer(Exception __exception, NativeSpeedWrite __state)
        { CompleteNativeSpeedParameters(__state, __exception); return __exception; }
    }
    private sealed class ControllerUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ManualUpdate), new[] { typeof(float) });
        [PatchPrefix] private static void Prefix(Player.FirearmController __instance) => OnControllerUpdate(__instance);
    }
    private sealed class ControllerDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.Destroy), Type.EmptyTypes);
        [PatchPrefix] private static void Prefix(Player.FirearmController __instance)
        { ArenaReloadHandling.Release(__instance); Release(__instance?.FirearmsAnimator); }
    }
}
