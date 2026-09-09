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
/// </summary>
internal static class ArenaAirFire
{
    private static AccessTools.FieldRef<Player.ItemHandsController, Player> Owner;
    private static bool _enabled, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
            new TriggerStatePatch().Enable();
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
        catch (Exception exception) { Warn(exception.GetBaseException().Message); return original; }
    }

    private sealed class TriggerStatePatch : ModulePatch
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
