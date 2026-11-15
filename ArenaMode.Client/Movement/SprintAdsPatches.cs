using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Movement;

/// <summary>
/// ADS straight out of a sprint (modern-shooter transit). Three gates block it
/// in vanilla and each gets a per-player patch:
///  1. The aim input reaches Idling.SetAiming but is rejected while the ANIMATOR
///     state is still Sprint/Transition (EFTHardSettings.NO_AIM_STATES).
///  2. Sprint keeps coasting for several animator frames after the aim press.
///  3. Even a forced aim is ripped off again by StateChangedHandler when the
///     Sprint→Transition state change fires.
/// Deliberately NOT patched: EFTHardSettings.CanAimInState (shared with every
/// bot) — the relevant clauses are reimplemented at the one local call site.
/// A real sprint START still cancels ADS (SprintStateChangedEvent path).
/// </summary>
internal static class SprintAdsPatches
{
    private static readonly AccessTools.FieldRef<Player.FirearmController, Player> ControllerPlayerField =
        AccessTools.FieldRefAccess<Player.FirearmController, Player>("_player");

    private static Player.FirearmController _bypassing;

    /// <summary>
    /// Safety net for shutdown/frame boundaries. The synchronous finalizer also
    /// releases the exact controller on exceptions and restores a nested caller.
    /// </summary>
    public static void ClearBypassSafety() => _bypassing = null;

    public static void Enable()
    {
        new ArenaSprintAdsAimCancelsSprintPatch().Enable();
        new ArenaSprintAdsAimStateGatePatch().Enable();
        new ArenaSprintAdsKeepAimThroughSprintExitPatch().Enable();
    }

    private static bool IsArenaFirearm(Player.FirearmController controller)
    {
        if (!ArenaSession.IsActive || Settings.SprintToAds?.Value != true || controller == null)
            return false;

        try
        {
            var player = ControllerPlayerField(controller);
            return ArenaMovementScope.IsLocal(player, requireInput: true)
                && ReferenceEquals(player.HandsController, controller)
                && player.MovementContext.IsGrounded
                && player.CurrentState != null && player.CurrentManagedState != null
                && player.CurrentManagedState is not JumpPlayerState
                && player.CurrentState?.Name != EPlayerState.Jump
                && player.CurrentState?.Name != EPlayerState.FallDown;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Aim press while sprinting: kill the sprint and open the bypass window.</summary>
    private sealed class ArenaSprintAdsAimCancelsSprintPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ToggleAim));

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance, out Player.FirearmController __state)
        {
            __state = _bypassing;
            if (!IsArenaFirearm(__instance) || __instance.IsAiming)
                return;

            var movementContext = ControllerPlayerField(__instance).MovementContext;
            if (movementContext.IsSprintEnabled)
            {
                movementContext.EnableSprint(false);
                movementContext.PlayerAnimatorEnableSprint(false);
            }

            _bypassing = __instance;
        }

        [PatchFinalizer]
        private static Exception Finalizer(Player.FirearmController __state, Exception __exception)
        {
            _bypassing = __state;
            return __exception;
        }
    }

    /// <summary>Let the aim request through while the animator still says Sprint/Transition.</summary>
    private sealed class ArenaSprintAdsAimStateGatePatch : ModulePatch
    {
        private static readonly FieldInfo IdlingTimeField =
            AccessTools.Field(typeof(Player.FirearmController.Idling), "_idlingTime");

        private static readonly FieldInfo OverlapValueField =
            AccessTools.Field(typeof(Player.FirearmController), "_overlapValue");

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController.Idling), "SetAiming");

        [PatchPrefix]
        private static bool Prefix(Player.FirearmController.Idling __instance, bool isAiming)
        {
            if (!isAiming)
                return true;

            var controller = __instance.Controller as Player.FirearmController;
            // Grounded Sprint/Transition admission must never become an airborne
            // ADS exception, even during animation lag or nested aim requests.
            if (controller != null && ArenaAirFire.PreventAirAim(controller)) return false;
            if (controller == null || !ReferenceEquals(_bypassing, controller) || !IsArenaFirearm(controller))
                return true;

            var player = ControllerPlayerField(controller);
            var stateName = player.CurrentState.Name;
            if (stateName != EPlayerState.Sprint && stateName != EPlayerState.Transition)
                return true;

            // Mirror the remaining vanilla clauses that must still deny the aim.
            if (controller.Blindfire)
                return true;

            // Vanilla also denies while the firearm animator is mid-animation for
            // OnlyBarrel-reload weapons (double barrels) and belt machine guns —
            // granting aim there glitches those weapon classes.
            var animatorIdling = controller.FirearmsAnimator?.IsIdling() ?? false;
            if (!animatorIdling && controller.Item is Weapon weapon)
            {
                if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel && !(weapon is RocketLauncher))
                    return true;

                if (weapon.IsBeltMachineGun)
                    return true;
            }

            if (OverlapValueField?.GetValue(controller) is float overlap
                && overlap > EFTHardSettings.Instance.STOP_AIMING_AT
                && !player.MovementContext.IsInMountedState)
            {
                return true;
            }

            controller.IsAiming = true;
            IdlingTimeField?.SetValue(__instance, 0f);
            return false;
        }
    }

    /// <summary>
    /// Keep the Sprint→Transition state-change from ripping the freshly granted
    /// ADS back off. Only while coasting OUT of a sprint — a real sprint start
    /// cancels ADS through the separate SprintStateChangedEvent path, untouched.
    /// </summary>
    private sealed class ArenaSprintAdsKeepAimThroughSprintExitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.StateChangedHandler));

        [PatchPrefix]
        private static bool Prefix(Player.FirearmController __instance, EPlayerState nextstate)
        {
            if (!IsArenaFirearm(__instance) || !__instance.IsAiming)
                return true;

            if (nextstate != EPlayerState.Sprint && nextstate != EPlayerState.Transition)
                return true;

            return ControllerPlayerField(__instance).MovementContext.IsSprintEnabled;
        }
    }
}
