using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// One additional vertical impulse within a genuine local Arena ground jump.
/// Leaves the original input dispatch, horizontal motion, character collision,
/// animator state and fall-damage accounting owned by EFT. FallDown derives from
/// JumpPlayerState but has zero lift; it deliberately does not grant a rescue jump.
/// </summary>
internal static class ArenaDoubleJump
{
    private sealed class Credit
    {
        internal Player Player;
        internal GameWorld World;
        internal bool Spent;
    }

    private sealed class Request
    {
        internal JumpPlayerState Jump;
        internal Credit Credit;
        internal Player Player;
    }

    private static ConditionalWeakTable<JumpPlayerState, Credit> _credits = new();
    private static AccessTools.FieldRef<Player, float> VaultingTiming;
    private static bool _enabled;
    private static bool _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            VaultingTiming = AccessTools.FieldRefAccess<Player, float>("_vaultingTiming");
            new JumpEnterPatch().Enable();
            new JumpExitPatch().Enable();
            new JumpCommandPatch().Enable();
            Settings.DoubleJumpEnabled.SettingChanged += ClearCredits;
            Settings.MovementEnabled.SettingChanged += ClearCredits;
            RaidLifecycle.OnRaidEnded += ResetCredits;
            _enabled = true;
        }
        catch (Exception exception)
        {
            _enabled = false;
            Warn(exception);
        }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        Settings.DoubleJumpEnabled.SettingChanged -= ClearCredits;
        Settings.MovementEnabled.SettingChanged -= ClearCredits;
        RaidLifecycle.OnRaidEnded -= ResetCredits;
        ResetCredits();
    }

    private static void ResetCredits()
    {
        _credits = new ConditionalWeakTable<JumpPlayerState, Credit>();
    }

    private static void ClearCredits(object sender, EventArgs args) => ResetCredits();

    private static void Warn(Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning($"Arena double jump callback failed: {exception.GetBaseException().Message}");
    }

    private static bool TryGetLocalPlayer(MovementContext context, out Player player)
    {
        player = null;
        if (!ArenaSession.IsActive || !_enabled
            || !Settings.MovementEnabled.Value || !Settings.DoubleJumpEnabled.Value
            || context == null || !Singleton<GameWorld>.Instantiated
            || !Singleton<AbstractGame>.Instantiated
            || !ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)
            || ArenaRespawnDriver.IsRespawnInProgress || ArenaSpectatorService.IsActive
            || ArenaInputBlocker.Blocked)
            return false;

        player = Singleton<GameWorld>.Instance.MainPlayer;
        return player != null && player.IsYourPlayer && !player.IsAI
            && !player.IsInventoryOpened
            && player.HealthController != null && player.HealthController.IsAlive
            && ReferenceEquals(player.MovementContext, context);
    }

    private static bool OwnsCredit(JumpPlayerState jump, Player expectedPlayer, Credit expectedCredit)
    {
        if (jump == null || jump.GetType() != typeof(JumpPlayerState)
            || !TryGetLocalPlayer(jump.MovementContext, out Player player)
            || !ReferenceEquals(player, expectedPlayer)
            || !ReferenceEquals(player.CurrentManagedState, jump)
            || !_credits.TryGetValue(jump, out Credit credit)
            || !ReferenceEquals(credit, expectedCredit)
            || !ReferenceEquals(credit.Player, player)
            || !ReferenceEquals(credit.World, Singleton<GameWorld>.Instance))
            return false;

        return true;
    }

    private static bool CanUse(JumpPlayerState jump, Player player, Credit credit)
    {
        if (credit == null || credit.Spent || !OwnsCredit(jump, player, credit)) return false;
        return IsAirborneActionAllowed(jump, player) && player.Physical.CanJump;
    }

    private static bool IsAirborneActionAllowed(JumpPlayerState jump, Player player)
    {
        MovementContext context = jump.MovementContext;
        // Exactly the native Player.Jump vault guard, plus physical restrictions
        // from MovementContext.TryJump. Do not fake IsGrounded or CanJump.
        return !context.IsGrounded
            && jump._jumpState == JumpPlayerState.EJumpState.Jump
            && !context.IsInPronePose && !context.IsInMountedState
            && context.CanJump && player.Physical != null
            && Singleton<GlobalConfiguration>.Instantiated
            && !(VaultingTiming(player) > Singleton<GlobalConfiguration>.Instance.VaultingSettings.VaultingInputTime)
            && !context.PlayerAnimatorGetIsVaulting();
    }

    private sealed class JumpEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter));

        [PatchPostfix]
        private static void Postfix(JumpPlayerState __instance, bool isFromSameState)
        {
            try
            {
                // State objects are reused by EFT. Only a fresh grounded entry
                // can create a credit; an airborne re-entry must never refill it.
                _credits.Remove(__instance);
                if (__instance.GetType() == typeof(JumpPlayerState) && !isFromSameState
                    && __instance.MovementContext.IsGrounded
                    && TryGetLocalPlayer(__instance.MovementContext, out Player player))
                    _credits.Add(__instance, new Credit { Player = player, World = Singleton<GameWorld>.Instance });
            }
            catch (Exception exception) { Warn(exception); }
        }
    }

    private sealed class JumpExitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Exit));

        [PatchPrefix]
        private static void Prefix(JumpPlayerState __instance) => _credits.Remove(__instance);
    }

    private sealed class JumpCommandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateCommand));

        // MovementState.Jump is empty and may be inlined by Mono. Hook the real
        // non-empty command translator, downstream of native UI input blocking,
        // without swallowing or synthesizing the user's configured jump binding.
        [PatchPrefix]
        private static void Prefix(MoveInputTranslator __instance, ECommand command, out Request __state)
        {
            __state = null;
            if (command != ECommand.Jump) return;
            try
            {
                var jump = __instance._controller?.CurrentManagedState as JumpPlayerState;
                if (jump != null && _credits.TryGetValue(jump, out Credit credit)
                    && CanUse(jump, __instance._controller, credit))
                    __state = new Request { Jump = jump, Player = __instance._controller, Credit = credit };
            }
            catch (Exception exception) { Warn(exception); }
        }

        [PatchPostfix]
        private static void Postfix(MoveInputTranslator __instance, Request __state)
        {
            if (__state == null) return;
            try
            {
                // Capture before vanilla, revalidate after: the initial ground
                // press cannot become a second press through an animator change.
                JumpPlayerState jump = __state.Jump;
                if (!ReferenceEquals(__instance._controller, __state.Player)
                    || !CanUse(jump, __state.Player, __state.Credit)) return;
                float height = Settings.DoubleJumpHeightMult.Value;
                if (float.IsNaN(height) || float.IsInfinity(height)) return;
                Vector3 lift = jump._liftForce * Mathf.Sqrt(Mathf.Clamp(height, 0.25f, 1.5f));
                Vector3 velocity = lift * jump._skilledBoost;
                float originY = jump.MovementContext.TransformPosition.y;
                if (!FinitePositive(velocity.y) || !FinitePositive(lift.y)
                    || !Finite(lift.x) || !Finite(lift.z) || !Finite(velocity.x) || !Finite(velocity.z)
                    || !Finite(originY) || !Finite(jump._liftDelay)) return;

                __state.Credit.Spent = true; // Before callbacks: no repeated debit/impulse on failure or re-entry.
                __state.Player.Physical.OnJump(true);
                // Stamina consumption invokes synchronous subscribers. They may
                // end the raid, change bodies or re-enter this reused state.
                // Do not check CanJump again after paying its cost, but require
                // the same ownership and an uninterrupted airborne jump.
                if (!OwnsCredit(jump, __state.Player, __state.Credit)
                    || !IsAirborneActionAllowed(jump, __state.Player)) return;

                // Restart only the native vertical clock. Preserve horizontal
                // momentum, skilled boost and the already-applied JumpHeightMult.
                // Native HeadBump, LimitMotion and ApplyMotion still do all movement.
                jump._liftForce = lift;
                jump._stateTime = jump._liftDelay;
                jump._currentLiftforce = velocity;
                jump._potentialH = originY;
                jump._timeDescending = 0f;
                jump._framesWithoutMotion = 0;
                jump._hasLastHeight = false;
                jump.MovementContext.EmitJumpNoise();
                Plugin.ModLogger.LogDebug("Arena double jump: renewed local vertical impulse.");
            }
            catch (Exception exception) { Warn(exception); }
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool FinitePositive(float value) => Finite(value) && value > 0f;
}
