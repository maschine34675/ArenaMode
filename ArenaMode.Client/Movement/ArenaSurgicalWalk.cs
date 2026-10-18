using ArenaMode.Session;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Extend only the native walking predicates. HealingLegs/UsingMeds still belong
/// to EFT and continue to block sprint, jump, prone and vaulting during surgery.
/// No speed-limit slot, medical effect or persistent player state is rewritten.
/// </summary>
internal static class ArenaSurgicalWalk
{
    internal const float MaximumSpeed = 1f;
    private static bool _enabled, _idleBound, _moveBound, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            new ArenaSurgicalWalkIdleWalkPatch().Enable();
            new ArenaSurgicalWalkMovingWalkPatch().Enable();
            _enabled = _idleBound && _moveBound;
            if (_enabled) Plugin.ModLogger.LogDebug("Arena surgical walking hooks ready (maximum 1 m/s).");
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); }
    }

    private static void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena surgical walking kept native restrictions: " + reason);
    }

    private static bool IsSurgery(MovementContext context, bool requireInput)
    {
        if (!ArenaSession.IsActive || !_enabled
            || !ArenaMovementScope.TryGetPlayer(context, out var player, requireInput)) return false;
        if (!context.PhysicalConditionIs(EPhysicalCondition.HealingLegs)
            || !context.PhysicalConditionIs(EPhysicalCondition.UsingMeds)
            || player.HandsController is not Player.MedsController controller
            || controller.GetItem() is not Meds item
            || item.HealthEffectsComponent?.AffectsAny(EDamageEffectType.DestroyedPart) != true) return false;

        var health = player.ActiveHealthController;
        var effect = health?.FindExistingEffect<ActiveHealthController.MedEffect>();
        return effect != null && effect.Existing
            && ReferenceEquals(effect.HealthController, health)
            && ReferenceEquals(effect.MedItem, item);
    }

    internal static bool CanWalk(MovementContext context)
    {
        bool original = context.CanWalk;
        if (original || !ArenaSession.IsActive) return original;
        try
        {
            if (!IsSurgery(context, requireInput: true)
                || context.OverridenState != null || context.OverridenControlsState != null
                || context.IsInPronePose || context.IsInMountedState || context.PlayerAnimatorGetIsVaulting()) return original;
            var state = context.CurrentState;
            if (state == null || (state.GetType() != typeof(IdlePlayerState)
                && state.GetType() != typeof(MovePlayerState))) return original;
            // The native getter's other veto must still run. Never bypass walls.
            return context.ObstacleCollisionFacade?.CanMove() == true;
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); return original; }
    }

    // Called AFTER Arena's root-motion bonus and slide contribution, before the
    // one existing CharacterController speed-limit bracket. Also works at 1x.
    internal static void LimitMotion(MovementContext context, ref Vector3 motion, float deltaTime)
    {
        if (!ArenaSession.IsActive) return;
        try
        {
            if (IsSurgery(context, requireInput: false)) ClampHorizontal(ref motion, deltaTime);
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); }
    }

    internal static void ClampHorizontal(ref Vector3 motion, float deltaTime)
    {
        double distance = Math.Sqrt((double)motion.x * motion.x + (double)motion.z * motion.z);
        double maximum = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
            ? 0 : Math.Max(0, deltaTime) * MaximumSpeed;
        if (double.IsNaN(distance) || double.IsInfinity(distance) || maximum == 0)
        { motion.x = motion.z = 0; return; }
        if (distance <= maximum) return;
        float ratio = (float)(maximum / distance);
        motion.x *= ratio;
        motion.z *= ratio;
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, out bool bound)
    {
        var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        var predicate = AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.CanWalk));
        var matches = code.Where(instruction => instruction.Calls(predicate)).ToArray();
        bound = matches.Length == 1;
        if (!bound)
        { Warn("expected one walking predicate, found " + matches.Length); return code; }
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = AccessTools.Method(typeof(ArenaSurgicalWalk), nameof(CanWalk));
        return code;
    }

    private sealed class ArenaSurgicalWalkIdleWalkPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(IdlePlayerState), nameof(IdlePlayerState.Move), new[] { typeof(Vector2) });
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => Rewrite(instructions, out _idleBound);
    }

    private sealed class ArenaSurgicalWalkMovingWalkPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovePlayerState), nameof(MovePlayerState.ManualAnimatorMoveUpdate), new[] { typeof(float) });
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => Rewrite(instructions, out _moveBound);
    }
}
