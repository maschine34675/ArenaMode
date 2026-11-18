using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ArenaMode.Movement;

/// <summary>
/// Only the trigger operation's movement-state test is widened. Patching the
/// tiny shared SuitableForHandInput getter would also unlock knives, grenades,
/// lights and other hand actions, and its inlined callers could evade Harmony.
/// A separate visual read keeps the firearm ready without changing body motion;
/// native aim setters remain responsible for applying airborne ADS cancellation.
/// </summary>
internal static class ArenaAirFire
{
    private static AccessTools.FieldRef<Player.ItemHandsController, Player> Owner;
    private static bool _enabled, _warned;
    private const int SprintJumpLeaf = 1332272753; // Base Layer.JUMP.Jump_Sprint, installed native controller.

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
            new ArenaAirFireTriggerStatePatch().Enable();
            new ArenaAirFireWeaponPosePatch().Enable();
            new ArenaAirFireAimValuePatch().Enable();
            new ArenaAirFireStateAimPatch().Enable();
            _enabled = true;
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); }
    }

    private static void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena airborne hip fire stayed on native restrictions: " + reason);
    }

    internal static bool SuitableForAirFire(Player.ItemHandsController controller)
    {
        // Preserve the ordinary predicate and its native exceptions, including
        // callers for which airborne support is disabled or irrelevant.
        bool original = controller.SuitableForHandInput;
        if (original || !ArenaSession.IsActive || !_enabled || Settings.AirHipFireEnabled?.Value != true)
            return original;
        try
        {
            if (controller is not Player.FirearmController firearm) return false;
            var player = Owner(controller);
            if (!ArenaMovementScope.IsLocal(player, requireInput: true)
                || !ReferenceEquals(player.HandsController, controller) || firearm.IsAiming) return false;
            return IsHipFireJump(player);
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); return original; }
    }

    private static bool IsHipFireJump(Player player)
    {
        var context = player.MovementContext;
        var jump = player.CurrentManagedState as JumpPlayerState;
        return jump != null && jump.GetType() == typeof(JumpPlayerState)
            && ReferenceEquals(jump.MovementContext, context)
            && player.CurrentState?.Name == EPlayerState.Jump
            && jump._jumpState == JumpPlayerState.EJumpState.Jump
            && !context.IsGrounded && !context.IsInPronePose && !context.IsInMountedState
            && !context.BlockFirearms && !context.PlayerAnimatorGetIsVaulting()
            && !ArenaAirVault.HasPendingRequest(player);
    }

    /// <summary>
    /// VisualPass alone reads this replacement. The body jump clip remains in
    /// charge of motion; its Weapon_3rd curve must not pull the ready first-person
    /// firearm down to the sprint-jump body's Weapon_Root_Third. Native sprint
    /// exit already clears the firearm Sprint bool, so changing that bool again
    /// would leave this independent body-authority path intact. No animator
    /// parameter, transform, operation, velocity or persistent override is written.
    /// </summary>
    internal static float HipFireWeaponAuthority(Player player, int hash)
    {
        float original = player.GetCurveValue(hash);
        if (!ArenaSession.IsActive || !_enabled || Settings.AirHipFireEnabled?.Value != true
            || hash != PlayerAnimator.WEAPON_ROOT_3RD) return original;
        try
        {
            if (!ArenaMovementScope.IsLocal(player, requireInput: true) || !player.FirstPersonPointOfView
                || player.HandsController is not Player.FirearmController firearm
                || !ReferenceEquals(Owner(firearm), player) || firearm.IsAiming || firearm.Blindfire
                || !IsHipFireJump(player) || firearm.IsInInteractionStrictCheck()) return original;
            var animator = player.BodyAnimatorCommon;
            // Read the actual leaf, not _isSprintWasPreviousState: releasing the
            // sprint binding clears that flag without changing the active clip.
            // Admit its incoming blend too, only after the native Jump owns the
            // airborne body; ordinary Idle/Move jumps keep their existing pose.
            if (animator == null || (animator.GetCurrentAnimatorStateInfo(0).fullPathHash != SprintJumpLeaf
                && (!animator.IsInTransition(0) || animator.GetNextAnimatorStateInfo(0).fullPathHash != SprintJumpLeaf))) return original;
            // Only ready conventional firearms: reload/draw/holster, malfunction,
            // utility and launcher operations retain their complete native pose.
            var operation = firearm.CurrentOperation;
            if (operation == null || !ReferenceEquals(operation.Controller, firearm)) return original;
            if (operation is not Player.FirearmController.Idling
                && (operation is not Player.FirearmController.FireOperationBase
                    || operation is Player.FirearmController.LauncherFire)) return original;
            return 0f;
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); return original; }
    }

    internal static bool PreventAirAim(Player.FirearmController firearm)
    {
        if (!ArenaSession.IsActive || !_enabled || firearm == null) return false;
        try
        {
            var player = Owner(firearm);
            return ArenaMovementScope.IsLocal(player) && ReferenceEquals(player.HandsController, firearm)
                && (!player.MovementContext.IsGrounded || player.CurrentState?.Name == EPlayerState.Jump
                    || player.CurrentState?.Name == EPlayerState.FallDown);
        }
        catch (Exception exception) { Warn(exception.GetBaseException().Message); return false; }
    }

    // FireOperationBase/AutomaticFireOperation set IsAiming directly, without
    // Idling's CanAimInState check. Keep the full native setter (slowdown, breath,
    // animation, recoil/sensitivity notifications), coercing only the denied value.
    private sealed class ArenaAirFireAimValuePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertySetter(typeof(Player.FirearmController), nameof(Player.FirearmController.IsAiming));

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance, ref bool value)
        {
            if (value && PreventAirAim(__instance)) value = false;
        }
    }

    private sealed class ArenaAirFireStateAimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.StateChangedHandler));

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance)
        {
            if (__instance.IsAiming && PreventAirAim(__instance)) __instance.IsAiming = false;
        }
    }

    private sealed class ArenaAirFireWeaponPosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.VisualPass));

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            var curve = AccessTools.Method(typeof(Player), nameof(Player.GetCurveValue));
            var authority = AccessTools.Field(typeof(PlayerAnimator), nameof(PlayerAnimator.WEAPON_ROOT_3RD));
            var matches = new List<CodeInstruction>();
            for (int i = 1; i < code.Count; i++)
                if (code[i].Calls(curve) && code[i - 1].opcode == OpCodes.Ldsfld
                    && Equals(code[i - 1].operand, authority)) matches.Add(code[i]);
            if (matches.Count != 1)
            {
                Warn("expected one visual weapon-authority read, found " + matches.Count);
                return code;
            }
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(ArenaAirFire), nameof(HipFireWeaponAuthority));
            return code;
        }
    }

    private sealed class ArenaAirFireTriggerStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player.FirearmController.Idling), "SetTriggerPressed", new[] { typeof(bool) });

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            var predicate = AccessTools.PropertyGetter(typeof(Player.ItemHandsController), nameof(Player.ItemHandsController.SuitableForHandInput));
            var matches = code.Where(instruction => instruction.Calls(predicate)).ToArray();
            if (matches.Length != 1)
            {
                Warn("expected one trigger state predicate, found " + matches.Length);
                return code;
            }
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(ArenaAirFire), nameof(SuitableForAirFire));
            return code;
        }
    }
}
