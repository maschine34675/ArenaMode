using ArenaMode.Config;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Fresh tap repair and one short, physically witnessed ledge grace. The existing translator owns dispatch.</summary>
internal static class ArenaJumpReliability
{
    internal const float CoyoteWindow = .15f;
    private sealed class Contact
    {
        internal Player Player;
        internal MovementContext Context;
        internal object Controller;
        internal GameWorld World;
        internal AbstractGame Game;
        internal float GroundAt = -1f, PulseAt = -1f;
    }
    private sealed class Sample
    {
        internal Contact Owner;
        internal bool SawContact;
    }
    private static Contact _contact;
    private static Sample _sample;
    private static AccessTools.FieldRef<Player, float> VaultingTiming;
    private static AccessTools.FieldRef<Player, bool> VaultingPressed;
    private static bool _ready, _warned;
    internal static int Epoch { get; private set; }

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            VaultingTiming = AccessTools.FieldRefAccess<Player, float>("_vaultingTiming");
            VaultingPressed = AccessTools.FieldRefAccess<Player, bool>("_isVaultingPressed");
            new ArenaJumpReliabilityMotionPatch().Enable();
            new ArenaJumpReliabilityContactPatch().Enable();
            Settings.MovementEnabled.SettingChanged += Changed;
            RaidLifecycle.OnRaidEnded += Reset;
            _ready = true;
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }
    internal static void Shutdown()
    {
        _ready = false;
        Reset();
        Settings.MovementEnabled.SettingChanged -= Changed;
        RaidLifecycle.OnRaidEnded -= Reset;
    }
    private static void Changed(object sender, EventArgs args) => Reset();
    private static void Reset() { _contact = null; _sample = null; unchecked { Epoch++; } }
    internal static void OnRelocated(Player player)
    {
        if (player != null && (ReferenceEquals(_contact?.Player, player)
            || ReferenceEquals(_sample?.Owner?.Player, player))) Reset();
    }
    internal static bool IsCoyoteCurrent(Player player, int epoch) => epoch == Epoch && Eligible(player);

    private static bool Eligible(Player player)
    {
        if (!_ready || !ArenaMovementScope.IsLocal(player, requireInput: true)) return false;
        var context = player.MovementContext;
        return ReferenceEquals(context.CurrentState, player.CurrentManagedState)
            && context.OverridenState == null && context.OverridenControlsState == null
            && player.CharacterController != null && context.PlayerAnimator != null
            && context.PoseLevel > .6f && !context.IsInPronePose && !context.IsInMountedState
            && !context.IsStationaryWeaponInHands && !context.IsAxesIgnored && context.BlindFire == 0
            && context.CanJump && !context.PlayerAnimatorGetIsVaulting()
            && !ArenaAirVault.HasPendingRequest(player)
            && context.Platform == null && context.PlatformMotion == Vector3.zero
            && !(context.ImpostorCC?.UseAsyncMoveValidation == true);
    }
    private static bool Owns(Contact contact, Player player)
        => contact != null && ReferenceEquals(contact.Player, player) && Eligible(player)
            && ReferenceEquals(contact.Context, player.MovementContext)
            && ReferenceEquals(contact.Controller, player.CharacterController)
            && ReferenceEquals(contact.World, Singleton<GameWorld>.Instance)
            && ReferenceEquals(contact.Game, Singleton<AbstractGame>.Instance);
    private static bool GroundState(MovementState state) => state != null
        && (state.GetType() == typeof(IdlePlayerState) || state.GetType() == typeof(MovePlayerState)
            || state.GetType() == typeof(SprintPlayerState));

    internal static void TickOwner()
    {
        try { if (_contact != null && !Owns(_contact, _contact.Player)) Reset(); }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static Contact NewContact(Player player) => new Contact { Player = player, Context = player.MovementContext,
        Controller = player.CharacterController, World = Singleton<GameWorld>.Instance, Game = Singleton<AbstractGame>.Instance };

    // Player.DisableVaulting resets both fields. Only an unheld stale timer is
    // repaired; held native vault intent, pending mantle and entered vault win.
    internal static void PrepareJump(Player player)
    {
        try
        {
            if (!Eligible(player) || !Singleton<GlobalConfiguration>.Instantiated
                || VaultingPressed(player)) return;
            if (VaultingTiming(player) > Singleton<GlobalConfiguration>.Instance.VaultingSettings.VaultingInputTime)
                VaultingTiming(player) = 0f;
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    // Called by the sole command owner after its native/context restrictions.
    // Consuming before callbacks prevents a second command from claiming grace.
    internal static bool TryConsumeCoyote(Player player)
    {
        var contact = _contact;
        if (!Owns(contact, player) || _sample != null || !Finite(Time.time)
            || contact.GroundAt < 0f || Time.time < contact.GroundAt
            || Time.time - contact.GroundAt > CoyoteWindow || player.MovementContext.IsGrounded) return false;
        var state = player.CurrentManagedState;
        bool falling = state?.GetType() == typeof(FallDown)
            && ((FallDown)state)._jumpState == JumpPlayerState.EJumpState.Jump
            && ((FallDown)state)._currentLiftforce.y <= 0f;
        if (!GroundState(state) && !falling) return false;
        contact.GroundAt = contact.PulseAt = -1f;
        return true;
    }

    internal static void JumpEntered(JumpPlayerState jump)
    {
        // FallDown calls Jump.Enter too, but passive falling must retain grace.
        if (jump?.GetType() == typeof(JumpPlayerState)
            && ReferenceEquals(_contact?.Player?.MovementContext, jump.MovementContext))
        { _contact = null; _sample = null; }
    }

    internal static void ObserveGround(MovementContext context)
    {
        try
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || !ReferenceEquals(player.MovementContext, context)) return;
            if (!Eligible(player)) { Reset(); return; }
            if (!Owns(_contact, player))
                _contact = NewContact(player);
            if (_sample != null || !GroundState(player.CurrentManagedState) || !context.IsGrounded
                || !context.GroundHit.hasHit || context.GroundHit.normal.y < .65f || !Finite(Time.time)) return;
            bool pulse = _contact.PulseAt >= 0f && Time.time >= _contact.PulseAt && Time.time - _contact.PulseAt <= .1f;
            if (player.CharacterController.isGrounded || pulse)
            {
                // A delayed observer cannot extend the 150 ms physical window.
                _contact.GroundAt = player.CharacterController.isGrounded ? Time.time : _contact.PulseAt;
                _contact.PulseAt = -1f;
            }
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private sealed class ArenaJumpReliabilityMotionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));
        [PatchPrefix]
        private static void Prefix(MovementContext __instance, float deltaTime, out Sample __state)
        {
            __state = null;
            try
            {
                if (!ArenaMovementScope.TryGetPlayer(__instance, out var player, requireInput: true)) return;
                if (!Eligible(player) || !Finite(deltaTime) || deltaTime <= 0f || deltaTime > .1f || _sample != null)
                { Reset(); return; }
                if (!GroundState(player.CurrentManagedState)) return;
                if (!Owns(_contact, player)) _contact = NewContact(player);
                _sample = __state = new Sample { Owner = _contact };
            }
            catch (Exception ex) { Reset(); Warn(ex); }
        }
        [PatchPostfix]
        private static void Postfix(bool __runOriginal, Sample __state)
        {
            try
            {
                if (__state == null || !ReferenceEquals(_sample, __state)) return;
                _sample = null;
                if (!__runOriginal || !ReferenceEquals(_contact, __state.Owner) || !Owns(_contact, _contact.Player))
                { Reset(); return; }
                if (__state.SawContact && GroundState(_contact.Player.CurrentManagedState) && Finite(Time.time))
                    _contact.PulseAt = Time.time;
            }
            catch (Exception ex) { Reset(); Warn(ex); }
        }
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, Sample __state)
        {
            if (__exception != null && ReferenceEquals(_contact, __state?.Owner)) Reset();
            return __exception;
        }
    }
    private sealed class ArenaJumpReliabilityContactPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(SimpleCharacterController), nameof(SimpleCharacterController.SingleMove));
        [PatchPostfix]
        private static void Postfix(SimpleCharacterController __instance)
        {
            if (_sample != null && ReferenceEquals(_sample.Owner.Player.CharacterController, __instance)
                && __instance.isGrounded) _sample.SawContact = true;
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena jump reliability failed open: " + ex.GetBaseException().Message);
    }
}
