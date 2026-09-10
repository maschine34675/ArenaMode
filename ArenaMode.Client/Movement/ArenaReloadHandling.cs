using ArenaMode.Config;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ArenaMode.Movement;

/// <summary>Leases only SpeedReload during reviewed live-local firearm reload operations.</summary>
internal static partial class ArenaReloadHandling
{
    private sealed class ValueLease
    {
        internal IAnimator Animator;
        internal float Original, Written;
        internal bool Active = true;
    }
    private sealed class ReloadLease
    {
        internal Player.FirearmController Controller;
        internal Player.FirearmController.FirearmOperation Operation;
        internal FirearmsAnimator ArmsOwner;
        internal PlayerAnimator BodyOwner;
        internal ValueLease Arms, Body;
        internal bool Blocked;
    }
    private sealed class PendingWrite
    {
        internal ReloadLease Reload;
        internal ValueLease Value;
        internal float Original, Written;
    }
    private static ConditionalWeakTable<Player.FirearmController, ReloadLease> Leases = new();
    private static readonly List<WeakReference<Player.FirearmController>> Owners = new();
    private static readonly ModulePatch[] Patches =
    {
        new ReloadStatePatch(), new RefreshPatch(), new DeathPatch(),
        new MagazineBoundaryPatch(typeof(Player.FirearmController.InsertMagOperation), "SwitchToIdlingState"),
        new MagazineBoundaryPatch(typeof(Player.FirearmController.InsertMagOperation), "HideWeapon"),
        new MagazineBoundaryPatch(typeof(Player.FirearmController.InsertMagOperation), "Reset"),
        new MagazineBoundaryPatch(typeof(Player.FirearmController.PullOutMagOperation), "OnMagPuttedToRig"),
        new MagazineBoundaryPatch(typeof(Player.FirearmController.PullOutMagOperation), "Reset")
    };
    private static int _attempted;
    private static bool _enabled, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        Shutdown();
        // A failed removal must not become a duplicate patch on a later enable.
        if (_attempted != 0) return;
        try
        {
            for (int i = 0; i < Patches.Length; i++)
            {
                _attempted = i + 1;
                Patches[i].Enable();
            }
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); Shutdown(); }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        ResetPerRaidState();
        while (_attempted > 0)
        {
            try { Patches[_attempted - 1].Disable(); _attempted--; }
            catch (Exception ex) { Warn(ex); break; }
        }
    }

    internal static void ResetPerRaidState()
    {
        foreach (var weak in Owners.ToArray())
            if (weak.TryGetTarget(out var controller)) Release(controller);
        Owners.Clear();
        Leases = new ConditionalWeakTable<Player.FirearmController, ReloadLease>();
        ResetDiagnostics();
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        try { Plugin.ModLogger?.LogWarning("Arena firearm reload speed failed open: " + ex.Message); }
        catch { /* A logging failure must not escape a gameplay hook. */ }
    }

    private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;

    private static bool TryFactor(Player.FirearmController controller,
        Player.FirearmController.FirearmOperation operation, out float factor)
    {
        factor = Settings.WeaponReloadSpeedMultiplier?.Value ?? 1f;
        if (!_enabled || !(factor > 1f && factor <= 3f) || operation == null
            || operation.State != Player.EOperationState.Executing
            || !ArenaMovementScope.IsLocal(operation.Player)
            || !ReferenceEquals(operation.Controller, controller)
            || !ReferenceEquals(operation.Player.HandsController, controller)
            || !ReferenceEquals(controller.CurrentOperation, operation)
            || !ReferenceEquals(controller.FirearmsAnimator, operation.FirearmsAnimator)
            || operation.Weapon == null || operation.Weapon.IsStationaryWeapon) return false;
        Type type = operation.GetType();
        // Native inventory magazine actions also implement UIFixes' normal-R
        // in-place reload when a rig has no empty slot. Only these two exact
        // families join regular reloads; chamber actions and subclasses do not.
        return type == typeof(Player.FirearmController.ReloadExternalMagOperation)
            || type == typeof(Player.FirearmController.ReloadInternalMagOperation)
            || type == typeof(Player.FirearmController.ReloadInternalMagWithOpenBoltOperation)
            || type == typeof(Player.FirearmController.ReloadCylinderMagOperation)
            || type == typeof(Player.FirearmController.ReloadSingleBarrelOperation)
            || type == typeof(Player.FirearmController.ReloadMultiBarrelOperation)
            || IsMagazineOperation(operation);
    }

    private static bool IsMagazineOperation(Player.FirearmController.FirearmOperation operation)
        => operation?.GetType() == typeof(Player.FirearmController.InsertMagOperation)
            || operation?.GetType() == typeof(Player.FirearmController.PullOutMagOperation);

    private static bool SameOwners(ReloadLease lease)
        => ReferenceEquals(lease.Controller.FirearmsAnimator, lease.ArmsOwner)
            && ReferenceEquals(lease.ArmsOwner?.Animator, lease.Arms?.Animator)
            && ReferenceEquals(lease.Operation.Player.MovementContext?.PlayerAnimator, lease.BodyOwner)
            && ReferenceEquals(lease.BodyOwner?.Animator, lease.Body?.Animator);

    // Player.ArmsUpdate calls FirearmController.ManualUpdate before advancing
    // the native arms animator. The existing handling tick invokes this here.
    // Native events and inventory transactions still own the complete reload.
    internal static void OnControllerUpdate(Player.FirearmController controller)
    {
        if (controller == null) return;
        try
        {
            if (Leases.TryGetValue(controller, out var lease)
                && !ReferenceEquals(controller.CurrentOperation, lease.Operation))
            { Release(controller); lease = null; }
            ObserveReload(controller);
            if (!TryFactor(controller, controller.CurrentOperation, out float factor))
            {
                TraceReload(controller, "native-setting-or-ineligible-operation");
                SampleReload(controller);
                if (lease != null) Block(lease);
                return;
            }
            if (lease != null)
            {
                if (lease.Blocked) return;
                if (!SameOwners(lease))
                { TraceReload(controller, "blocked-animator-owner-changed"); Block(lease); return; }
                if (!StillOwned(lease.Arms) || !StillOwned(lease.Body))
                { TraceReload(controller, "blocked-speed-readback-changed"); Block(lease); return; }
                Write(lease.Arms, lease.Arms.Original * factor);
                Write(lease.Body, lease.Body.Original * factor);
                SampleReload(controller);
                return;
            }
            var operation = controller.CurrentOperation;
            var arms = controller.FirearmsAnimator;
            var body = operation.Player.MovementContext.PlayerAnimator;
            if (arms?.Animator == null || body?.Animator == null
                || !arms.HasParameter(AnimationControllerParametersTable.FLOAT_SPEEDRELOAD)
                || !body.Animator.HasParameter(AnimationControllerParametersTable.FLOAT_SPEEDRELOAD))
            { TraceReload(controller, "missing-animator-or-reload-parameter"); return; }
            // Shared wrappers would cause a duplicate multiplier / ambiguous
            // restoration. Treat that runtime arrangement as unsupported.
            if (ReferenceEquals(arms.Animator, body.Animator))
            { TraceReload(controller, "shared-arms-body-animator"); return; }
            float armsBase = AnimationControllerParametersTable.GetFloatSpeedReload(arms.Animator);
            float bodyBase = AnimationControllerParametersTable.GetFloatSpeedReload(body.Animator);
            if (!Positive(armsBase) || !Positive(bodyBase)
                || !Positive(armsBase * factor) || !Positive(bodyBase * factor))
            { TraceReload(controller, "invalid-native-baseline"); return; }
            lease = new ReloadLease
            {
                Controller = controller, Operation = operation, ArmsOwner = arms, BodyOwner = body,
                Arms = new ValueLease { Animator = arms.Animator, Original = armsBase, Written = armsBase },
                Body = new ValueLease { Animator = body.Animator, Original = bodyBase, Written = bodyBase }
            };
            Leases.Add(controller, lease);
            Owners.Add(new WeakReference<Player.FirearmController>(controller));
            Write(lease.Arms, armsBase * factor);
            Write(lease.Body, bodyBase * factor);
            TraceReload(controller, "write-requested");
        }
        catch (Exception ex)
        {
            TraceReload(controller, "blocked-speed-access-error");
            if (Leases.TryGetValue(controller, out var failed)) Block(failed);
            Warn(ex);
        }
    }

    private static bool StillOwned(ValueLease value)
        => value.Active && AnimationControllerParametersTable.GetFloatSpeedReload(value.Animator) == value.Written;

    private static void Write(ValueLease value, float changed)
    {
        if (!Positive(changed)) throw new InvalidOperationException("Non-finite reload animation speed.");
        if (changed == value.Written) return;
        try
        {
            AnimationControllerParametersTable.SetSpeedReload(value.Animator, changed);
            value.Written = changed;
        }
        catch
        {
            if (AnimationControllerParametersTable.GetFloatSpeedReload(value.Animator) == changed) value.Written = changed;
            throw;
        }
    }

    private static void Restore(ValueLease value, IAnimator current)
    {
        if (value == null || !ReferenceEquals(value.Animator, current)) return;
        try
        {
            if (StillOwned(value)) AnimationControllerParametersTable.SetSpeedReload(value.Animator, value.Original);
        }
        catch (Exception ex) { Warn(ex); }
        finally { value.Active = false; }
    }

    private static void Block(ReloadLease lease)
    {
        // Do not reacquire a foreign/failed write every frame of this operation.
        lease.Blocked = true;
        try { Restore(lease.Arms, lease.ArmsOwner?.Animator); }
        catch (Exception ex) { Warn(ex); }
        try { Restore(lease.Body, lease.BodyOwner?.Animator); }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Release(Player.FirearmController controller)
    {
        EndReloadDiagnostic(controller, null, "released");
        if (controller == null || !Leases.TryGetValue(controller, out var lease)) return;
        Leases.Remove(controller);
        Owners.RemoveAll(weak => !weak.TryGetTarget(out var target) || ReferenceEquals(target, controller));
        Block(lease);
    }

    private static void BeforeReloadState(Player.FirearmController.BaseReloadOperation operation, Player.EOperationState next)
    {
        if (next != Player.EOperationState.Executing)
            EndReloadDiagnostic(operation?.Controller, operation, "native-state-" + next);
        // Release before the native Finished callback and subsequent idle/fire
        // transition; native animator events continue without an Arena override.
        if (next != Player.EOperationState.Executing && operation?.Controller != null
            && Leases.TryGetValue(operation.Controller, out var lease)
            && ReferenceEquals(lease.Operation, operation)) Release(operation.Controller);
    }

    private static void BeforeMagazineBoundary(Player.FirearmController.FirearmOperation operation)
    {
        // The native endpoint can immediately call an inventory callback that
        // starts the next operation. Restore before it, never a frame later.
        if (!IsMagazineOperation(operation) || operation.Controller == null) return;
        EndReloadDiagnostic(operation.Controller, operation, "native-magazine-boundary");
        if (Leases.TryGetValue(operation.Controller, out var lease))
        {
            if (!ReferenceEquals(lease.Operation, operation)) return;
            Release(operation.Controller);
        }
    }

    private static PendingWrite PrepareNativeWrite(object owner, ref float reload)
    {
        // The native refresh site has no controller argument for its setters.
        // Only exact already-owned wrappers can match; there is normally one.
        foreach (var weak in Owners.ToArray())
        {
            if (!weak.TryGetTarget(out var controller) || !Leases.TryGetValue(controller, out var lease) || lease.Blocked) continue;
            ValueLease value = ReferenceEquals(owner, lease.ArmsOwner) ? lease.Arms
                : ReferenceEquals(owner, lease.BodyOwner) ? lease.Body : null;
            if (value == null) continue;
            if (!TryFactor(controller, lease.Operation, out float factor) || !SameOwners(lease)
                || !StillOwned(value) || !Positive(reload) || !Positive(reload * factor))
            { TraceReload(controller, "blocked-native-refresh-validation"); Block(lease); return null; }
            var pending = new PendingWrite { Reload = lease, Value = value, Original = reload, Written = reload * factor };
            reload = pending.Written;
            return pending;
        }
        return null;
    }

    private static void CompleteNativeWrite(PendingWrite pending, bool succeeded)
    {
        if (pending == null) return;
        try
        {
            float actual = AnimationControllerParametersTable.GetFloatSpeedReload(pending.Value.Animator);
            if (actual == pending.Written)
            {
                pending.Value.Original = pending.Original;
                pending.Value.Written = pending.Written;
            }
            if (!succeeded || actual != pending.Written)
            { TraceReload(pending.Reload.Controller, "blocked-native-refresh-readback"); Block(pending.Reload); }
        }
        catch (Exception ex) { Block(pending.Reload); Warn(ex); }
    }

    private static void AbortNativeWrite(object owner)
    {
        foreach (var weak in Owners.ToArray())
            if (weak.TryGetTarget(out var controller) && Leases.TryGetValue(controller, out var lease)
                && (ReferenceEquals(owner, lease.ArmsOwner) || ReferenceEquals(owner, lease.BodyOwner))) Block(lease);
    }

    internal static void SetFirearmSpeeds(FirearmsAnimator animator, float reload, float draw)
    {
        PendingWrite pending = null;
        float original = reload;
        try { pending = PrepareNativeWrite(animator, ref reload); }
        catch (Exception ex) { reload = original; AbortNativeWrite(animator); Warn(ex); }
        bool succeeded = false;
        try { animator.SetSpeedParameters(reload, draw); succeeded = true; }
        finally { CompleteNativeWrite(pending, succeeded); }
    }

    internal static void SetBodySpeeds(PlayerAnimator animator, float reload, float draw)
    {
        PendingWrite pending = null;
        float original = reload;
        try { pending = PrepareNativeWrite(animator, ref reload); }
        catch (Exception ex) { reload = original; AbortNativeWrite(animator); Warn(ex); }
        bool succeeded = false;
        try { animator.SetSpeedParameters(reload, draw); succeeded = true; }
        finally { CompleteNativeWrite(pending, succeeded); }
    }

    private sealed class ReloadStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertySetter(typeof(Player.FirearmController.BaseReloadOperation), nameof(Player.FirearmController.BaseReloadOperation.State));
        [PatchPrefix] private static void Prefix(Player.FirearmController.BaseReloadOperation __instance, Player.EOperationState value)
            => BeforeReloadState(__instance, value);
    }

    private sealed class DeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.OnPlayerDead), Type.EmptyTypes);
        [PatchPrefix] private static void Prefix(Player.FirearmController __instance) => Release(__instance);
    }

    private sealed class MagazineBoundaryPatch : ModulePatch
    {
        private readonly Type _type;
        private readonly string _method;
        internal MagazineBoundaryPatch(Type type, string method)
            : base("ArenaReload." + type.Name + "." + method)
        { _type = type; _method = method; }
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(_type, _method);
        [PatchPrefix] private static void Prefix(Player.FirearmController.FirearmOperation __instance)
            => BeforeMagazineBoundary(__instance);
    }

    private sealed class RefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.SetAnimatorAndProceduralValues), Type.EmptyTypes);
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            foreach (var entry in new[] { (typeof(FirearmsAnimator), nameof(SetFirearmSpeeds), 3), (typeof(PlayerAnimator), nameof(SetBodySpeeds), 2) })
            {
                var target = AccessTools.Method(entry.Item1, "SetSpeedParameters", new[] { typeof(float), typeof(float) });
                var calls = result.Where(instruction => instruction.Calls(target)).ToArray();
                if (calls.Length != entry.Item3) throw new InvalidOperationException("Native reload speed refresh call sites changed.");
                foreach (var call in calls)
                {
                    call.opcode = OpCodes.Call;
                    call.operand = AccessTools.Method(typeof(ArenaReloadHandling), entry.Item2);
                }
            }
            return result;
        }
    }
}
