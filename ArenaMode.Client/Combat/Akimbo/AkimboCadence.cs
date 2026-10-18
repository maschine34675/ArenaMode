using AnimationSystem;
using EFT;
using FastAnimatorSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Only firing timers and the exact hands animator, never ArmsUpdate/ballistics.</summary>
internal static class AkimboCadence
{
    internal const float Factor = 2f;
    private static Player.FirearmController.FireOperationBase _operation, _blocked;
    private static UnityAnimatorWrapper _animator;
    private static float _original, _written;
    internal static int AcceleratedUpdates { get; private set; }
    internal static void Enable()
    {
        new ArenaAkimboCadenceTimerPatch(typeof(Player.FirearmController.FireOperation)).Enable();
        new ArenaAkimboCadenceTimerPatch(typeof(Player.FirearmController.AutomaticFireOperation)).Enable();
        new ArenaAkimboCadenceFastAnimatorPatch().Enable(); new ArenaAkimboCadenceControllerPatch().Enable();
        new ArenaAkimboCadenceStatePatch().Enable();
    }

    private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

    // Unity's auto-updated arms require an owned speed value. FastAnimator's
    // speed setter is unused; its actual Update delta is patched separately.
    internal static bool Refresh()
    {
        try
        {
            if (!ArenaAkimbo.TryCadence(out var body, out var op)) { Reset(); return false; }
            var animator = op.Controller.FirearmsAnimator?.Animator;
            if (!ReferenceEquals(body.ArmsAnimatorCommon, animator)) { Reset(); return false; }
            if (!ReferenceEquals(_operation, op) || !ReferenceEquals(_animator, animator)) ReleaseSpeed();
            if (_blocked != null && !ReferenceEquals(_blocked, op)) _blocked = null;
            if (ReferenceEquals(_blocked, op)) return false;
            if (animator is FastAnimatorProcessor) return body.ArmsUpdateMode == Player.EUpdateMode.Manual;
            if (animator is not UnityAnimatorWrapper unity || unity.Animator == null) return false;
            if (_animator != null)
            {
                if (_animator.speed == _written) return true;
                // Another owner changed the speed: preserve their value and do
                // not fight it each frame of this same native operation.
                ReleaseSpeed(); _blocked = op; return false;
            }
            float original = unity.speed;
            if (!Positive(original) || !Positive(original * Factor)) return false;
            _operation = op; _animator = unity; _original = original; _written = original * Factor;
            unity.speed = _written;
            return true;
        }
        catch (Exception ex) { Reset(); ArenaAkimbo.AmmoFailed(ex); return false; }
    }

    internal static void Advance(Player.FirearmController.FireOperationBase op, ref float deltaTime)
    {
        if (op == null || !ArenaAkimbo.OwnsHands(op.Controller) || !Positive(deltaTime)) return;
        if (ArenaAkimbo.TryCadence(out _, out var current) && ReferenceEquals(current, op) && Refresh())
        { deltaTime *= Factor; AcceleratedUpdates++; }
    }
    internal static void AdvanceAnimator(FastAnimatorProcessor animator, ref float deltaTime)
    {
        if (!ReferenceEquals(ArenaAkimbo.OwnedAnimator, animator) || !Positive(deltaTime)) return;
        if (ArenaAkimbo.TryCadence(out var body, out var op) && body.ArmsUpdateMode == Player.EUpdateMode.Manual
            && ReferenceEquals(body.ArmsAnimatorCommon, animator)
            && ReferenceEquals(op.Controller.FirearmsAnimator.Animator, animator)) deltaTime *= Factor;
    }

    internal static void BeforeState(Player.ObjectInHandsOperation operation, Player.EOperationState value)
    {
        if (value != Player.EOperationState.Executing && ReferenceEquals(operation, _operation)) ReleaseSpeed();
    }
    private static void ReleaseSpeed()
    {
        var animator = _animator; float original = _original, written = _written;
        _animator = null; _operation = null;
        if (animator?.Animator != null && animator.speed == written) animator.speed = original;
    }
    internal static void Reset() { ReleaseSpeed(); _blocked = null; }

    private sealed class ArenaAkimboCadenceTimerPatch(Type type) : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(type, "Update", new[] { typeof(float) });
        [PatchPrefix] private static void Prefix(Player.FirearmController.FireOperationBase __instance, ref float deltaTime)
        { try { Advance(__instance, ref deltaTime); } catch (Exception ex) { ArenaAkimbo.AmmoFailed(ex); } }
        [PatchFinalizer] private static Exception Finalizer(Exception __exception, Player.FirearmController.FireOperationBase __instance)
        { if (__exception != null && ArenaAkimbo.OwnsHands(__instance?.Controller)) { Reset(); ArenaAkimbo.AmmoFailed(__exception); } return __exception; }
    }
    private sealed class ArenaAkimboCadenceFastAnimatorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(FastAnimatorProcessor), "Update", new[] { typeof(float) });
        [PatchPrefix] private static void Prefix(FastAnimatorProcessor __instance, ref float deltaTime)
        { try { AdvanceAnimator(__instance, ref deltaTime); } catch (Exception ex) { ArenaAkimbo.AmmoFailed(ex); } }
    }
    private sealed class ArenaAkimboCadenceControllerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Player.FirearmController), "ManualUpdate");
        [PatchPostfix] private static void Postfix(Player.FirearmController __instance)
        {
            // Do not revalidate every bot every frame; only the current leased
            // controller or the exact current firing owner needs this seam.
            try
            {
                if (ReferenceEquals(_operation?.Controller, __instance) || ArenaAkimbo.OwnsHands(__instance)) Refresh();
            }
            catch (Exception ex) { Reset(); ArenaAkimbo.AmmoFailed(ex); }
        }
    }
    private sealed class ArenaAkimboCadenceStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertySetter(typeof(Player.ObjectInHandsOperation), nameof(Player.ObjectInHandsOperation.State));
        [PatchPrefix] private static void Prefix(Player.ObjectInHandsOperation __instance, Player.EOperationState value)
        { try { BeforeState(__instance, value); } catch (Exception ex) { Reset(); ArenaAkimbo.AmmoFailed(ex); } }
    }
}
