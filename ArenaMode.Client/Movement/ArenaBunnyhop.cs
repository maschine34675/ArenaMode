using ArenaMode.Config;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Engine;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Timed ground hops. Native Enter owns lift, stamina and animation; we carry only applied XZ speed.</summary>
internal static class ArenaBunnyhop
{
    internal const float InputWindow = .15f, GroundWindow = .12f, TransitionWindow = .5f;
    internal const float Gain = 1.06f, MaxGain = 1.25f, AbsoluteCap = 12f;

    private sealed class Flight
    {
        internal Player Player;
        internal GameWorld World;
        internal AbstractGame Game;
        internal JumpPlayerState Jump;
        internal Vector3 Velocity, Carry;
        internal float BaseSpeed, Began, SampleAt, LaunchY, LandedAt = -1f, BufferedAt = -1f, DispatchAt = -1f, ContactAt = -1f;
        internal MovementState SignalOwner;
        internal bool LeftGround, HasCarry, Sampling, SawContact;
        internal int Hops;
    }

    private struct MotionSample
    {
        internal Flight Flight;
        internal Vector3 Position;
        internal float Dt;
    }

    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".Bunnyhop");
    private static Flight _flight;
    private static bool _ready, _warned;

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            Patches.Patch(AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter)),
                prefix: Hook(nameof(BeforeEntry)), postfix: Hook(nameof(AfterEntry)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.CheckFlying)),
                postfix: Hook(nameof(Tick)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion)),
                prefix: Hook(nameof(BeforeMotion)), postfix: Hook(nameof(AfterMotion)), finalizer: Hook(nameof(MotionFailed)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(SimpleCharacterController), nameof(SimpleCharacterController.SingleMove)),
                postfix: Hook(nameof(AfterSubstep)));
            Settings.BunnyhopEnabled.SettingChanged += Changed;
            Settings.MovementEnabled.SettingChanged += Changed;
            Settings.WalkSpeedMult.SettingChanged += Changed;
            Settings.SprintSpeedMult.SettingChanged += Changed;
            Settings.ExtraMotionScale.SettingChanged += Changed;
            RaidLifecycle.OnRaidEnded += Reset;
            _ready = true;
            Plugin.ModLogger.LogInfo("Arena bunnyhop: timed landing buffer ready (6% per hop, 25% chain cap, 12 m/s absolute cap).");
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }

    private static HarmonyMethod Hook(string name) => new(typeof(ArenaBunnyhop).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic));

    internal static void Shutdown()
    {
        Reset();
        _ready = false;
        Settings.BunnyhopEnabled.SettingChanged -= Changed;
        Settings.MovementEnabled.SettingChanged -= Changed;
        Settings.WalkSpeedMult.SettingChanged -= Changed;
        Settings.SprintSpeedMult.SettingChanged -= Changed;
        Settings.ExtraMotionScale.SettingChanged -= Changed;
        RaidLifecycle.OnRaidEnded -= Reset;
        Patches.UnpatchSelf();
    }

    private static void Changed(object sender, EventArgs args) => Reset();
    internal static void TickOwner()
    {
        if (_flight == null) return;
        try
        {
            if (!Owns(_flight, _flight.Player?.MovementContext)) Reset();
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }
    private static void ReleaseSignal(Flight f)
    {
        if (f?.SignalOwner == null) return;
        var owner = f.SignalOwner;
        f.SignalOwner = null;
        // Only our still-pending request on the same body and native state.
        if (f.Player != null && ReferenceEquals(f.Player.MovementContext, owner.MovementContext)
            && ReferenceEquals(f.Player.CurrentManagedState, owner)
            && owner.MovementContext.PlayerAnimator.IsJumpSetted())
            owner.MovementContext.PlayerAnimatorEnableJump(false);
    }

    private static void Reset()
    {
        var f = _flight;
        _flight = null;
        ReleaseSignal(f);
    }

    private static bool Eligible(MovementContext context, out Player player)
    {
        player = null;
        if (!_ready || Settings.BunnyhopEnabled?.Value != true || !ArenaAirControl.Ready
            || !ArenaMovementScope.TryGetPlayer(context, out player, requireInput: true)) return false;
        return ReferenceEquals(context.CurrentState, player.CurrentManagedState)
            && context.OverridenState == null && context.OverridenControlsState == null
            && player.CharacterController != null && context.PlayerAnimator != null
            && context.PoseLevel > .6f && !context.IsInPronePose && !context.IsInMountedState
            && !context.IsStationaryWeaponInHands && !context.IsAxesIgnored && context.BlindFire == 0
            && context.CanJump && context.CanWalk && !context.PlayerAnimatorGetIsVaulting()
            && !ArenaAirVault.HasPendingRequest(player)
            && player.GetComponent<ArenaSlideController>()?.IsSliding != true
            && context.Platform == null && context.PlatformMotion == Vector3.zero
            && !(context.ImpostorCC?.UseAsyncMoveValidation == true);
    }

    private static bool Owns(Flight f, MovementContext context)
        => f != null && Eligible(context, out var player) && ReferenceEquals(f.Player, player)
            && ReferenceEquals(f.World, Singleton<GameWorld>.Instance)
            && ReferenceEquals(f.Game, Singleton<AbstractGame>.Instance);

    private static bool GroundState(MovementState state) => state != null
        && (state.GetType() == typeof(IdlePlayerState) || state.GetType() == typeof(MovePlayerState)
            || state.GetType() == typeof(SprintPlayerState));

    private static bool Contact(Player player)
        => player.MovementContext.IsGrounded && player.CharacterController.isGrounded;

    private static bool Supported(Player player)
        => player.MovementContext.IsGrounded && player.MovementContext.GroundHit.hasHit
            && player.MovementContext.GroundHit.normal.y >= .65f;

    // Shared with DoubleJump: only committed physical contact, never the
    // proximity ray alone. A fresh native entry replaces this entire witness.
    internal static bool HasRecentPhysicalContact(MovementContext context)
    {
        var f = _flight;
        if (f == null || f.Sampling || !f.LeftGround || !Owns(f, context)
            || f.ContactAt < 0f || Time.time < f.ContactAt || Time.time - f.ContactAt > .1f
            || !Supported(f.Player)) return false;
        var state = f.Player.CurrentManagedState;
        return GroundState(state) || state?.GetType() == typeof(JumpLandingState)
            || (ReferenceEquals(state, f.Jump) && f.Jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround
                && f.Jump._currentLiftforce.y <= 0f);
    }

    // SimpleCC clears its contact bit at EACH substep. Keep physical contact
    // only inside our exact player's successful native motion transaction.
    private static void AfterSubstep(SimpleCharacterController __instance)
    {
        var f = _flight;
        if (f?.Sampling == true && ReferenceEquals(f.Player.CharacterController, __instance)
            && __instance.isGrounded) f.SawContact = true;
    }

    // Called before DoubleJump observes the same command. Vanilla translation is
    // never suppressed: Jump/JumpLanding are native no-ops; ground Jump still runs.
    internal static bool TryBufferJump(Player player)
    {
        try
        {
            var f = _flight;
            var context = player?.MovementContext;
            if (!Owns(f, context) || !ReferenceEquals(f.Player, player) || !f.LeftGround
                || !player.Physical.CanJump || Time.time - f.SampleAt > .2f) return false;
            var state = player.CurrentManagedState;
            bool onGround = Contact(player) || (f.LandedAt >= 0f && Supported(player));
            bool landing = f.LandedAt >= 0f && Time.time - f.LandedAt <= GroundWindow;
            bool descending = ReferenceEquals(state, f.Jump) && f.Jump._jumpState == JumpPlayerState.EJumpState.Jump
                && f.Jump._currentLiftforce.y < -.1f && Time.time - f.Began >= .12f;
            var hit = context.GroundHit;
            float gap = context.PlayerColliderCenter.y - hit.point.y - Math.Max(player.CharacterController.height / 2f,
                player.CharacterController.radius) - player.CharacterController.skinWidth;
            bool imminent = descending && hit.hasHit && hit.normal.y >= .65f && gap >= -.1f
                && gap <= .45f && gap <= -f.Jump._currentLiftforce.y * InputWindow;
            if (!(onGround && (landing || descending)) && !imminent) return false;
            if (f.BufferedAt < 0f) f.BufferedAt = Time.time; // Repeated presses cannot extend the request.
            return !GroundState(state); // A ground press belongs to the native Jump call.
        }
        catch (Exception ex) { Reset(); Warn(ex); return false; }
    }

    private static void Tick(MovementContext __instance)
    {
        var f = _flight;
        if (f == null || !ReferenceEquals(f.Player?.MovementContext, __instance)) return;
        try
        {
            if (!Owns(f, __instance)) { Reset(); return; }
            var state = f.Player.CurrentManagedState;
            if (state.GetType() != typeof(JumpPlayerState) && state.GetType() != typeof(JumpLandingState) && !GroundState(state))
            { Reset(); return; }
            bool descending = state is not JumpPlayerState jump || (jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround
                && jump._currentLiftforce.y <= 0f);
            if (f.DispatchAt >= 0f && (Time.time - f.DispatchAt > .25f || !Supported(f.Player)))
            { Reset(); return; }
            if (f.LandedAt >= 0f && !Supported(f.Player)) { Reset(); return; }
            bool pulse = HasRecentPhysicalContact(__instance);
            bool physicalContact = Contact(f.Player) || pulse;
            if (f.LandedAt < 0f && !physicalContact)
            { if (!__instance.IsGrounded) f.LeftGround = true; return; }
            if (!f.LeftGround || !descending) return;
            if (f.LandedAt < 0f)
            {
                f.LandedAt = pulse ? f.ContactAt : Time.time;
                if (f.BufferedAt >= 0f && f.LandedAt - f.BufferedAt > InputWindow) f.BufferedAt = -1f;
            }
            if ((f.DispatchAt < 0f && Time.time - f.LandedAt > TransitionWindow)
                || (f.BufferedAt < 0f && Time.time - f.LandedAt > GroundWindow))
            { Reset(); return; }
            if (f.DispatchAt >= 0f)
            {
                if (Time.time - f.DispatchAt > .25f) Reset();
                return;
            }
            if (f.BufferedAt < 0f || !GroundState(state) || !Supported(f.Player) || !f.Player.Physical.CanJump
                || __instance.PlayerAnimator.IsJumpSetted()) return;
            f.DispatchAt = Time.time;
            f.SignalOwner = state;
            try { f.Player.Jump(); } // Also retains Player's vault-input guard. Enter alone owns the debit.
            finally
            {
                // TryJump emits noise BEFORE setting IsJumping. A synchronous
                // callback may cancel us during that noise and then native code
                // writes the bool anyway; settle our original owner once more.
                if (!ReferenceEquals(_flight, f)) { f.SignalOwner = state; ReleaseSignal(f); }
            }
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void BeforeEntry(JumpPlayerState __instance, out Flight __state)
    {
        __state = null;
        try
        {
            var f = _flight;
            if (__instance.GetType() != typeof(JumpPlayerState) || !Eligible(__instance.MovementContext, out var player))
            { if (ReferenceEquals(f?.Player?.MovementContext, __instance.MovementContext)) Reset(); return; }
            // Native ground Jump has already been admitted. SimpleCC's contact
            // bit describes its LAST depenetration substep and may be false at
            // Enter even on a flat floor. Seed from native ground locomotion;
            // a chain still requires the separately witnessed physical landing.
            if (!__instance.MovementContext.IsGrounded || !GroundState(__instance.MovementContext.PreviousState as MovementState))
            { Reset(); return; }
            bool chained = Owns(f, __instance.MovementContext) && f.LeftGround && f.BufferedAt >= 0f
                && f.LandedAt >= 0f && (f.DispatchAt >= 0f ? Time.time - f.DispatchAt <= .25f
                    : Time.time - f.LandedAt <= TransitionWindow) && f.BaseSpeed >= 1f;
            var next = new Flight { Player = player, World = Singleton<GameWorld>.Instance,
                Game = Singleton<AbstractGame>.Instance, Jump = __instance, Began = Time.time, SampleAt = Time.time,
                LaunchY = __instance.MovementContext.TransformPosition.y };
            if (chained)
            {
                next.BaseSpeed = f.BaseSpeed;
                float speed = Mathf.Min(Planar(f.Velocity) * Gain, Mathf.Min(f.BaseSpeed * MaxGain, AbsoluteCap));
                if (speed >= 1f)
                {
                    next.Carry = f.Velocity.normalized * speed;
                    next.HasCarry = true;
                    next.Hops = f.Hops + 1;
                }
            }
            // Enter consumed our bool. Never clear it on the new Jump state.
            if (f != null) f.SignalOwner = null;
            _flight = next;
            __state = next;
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void AfterEntry(JumpPlayerState __instance, Flight __state)
    {
        if (__state == null || !ReferenceEquals(_flight, __state)) return;
        if (!Owns(__state, __instance.MovementContext)) { Reset(); return; }
        if (__state.HasCarry)
            Plugin.ModLogger.LogDebug($"Arena bunnyhop: hop {__state.Hops}, carry {Planar(__state.Carry):F2} m/s, cap {Mathf.Min(__state.BaseSpeed * MaxGain, AbsoluteCap):F2} m/s.");
    }

    // Invoked before AirControl's steering and native LimitMotion, never after collision.
    internal static void ApplyCarry(MovementState state, ref Vector3 motion, float dt)
    {
        var f = _flight;
        if (f?.HasCarry != true || !ReferenceEquals(state, f.Jump)) return;
        if (!Owns(f, state.MovementContext)) { Reset(); return; }
        if (!Finite(dt) || dt <= 0f || dt > .1f || f.LandedAt >= 0f) return;
        if (f.Jump._jumpState == JumpPlayerState.EJumpState.Bumbped) { Reset(); return; }
        float scale = ArenaMovementPatches.MotionScale(state.MovementContext);
        motion.x = f.Carry.x * dt / scale;
        motion.z = f.Carry.z * dt / scale;
    }

    internal static bool CarryNeedsSpeedBracket(MovementContext context)
        => _flight?.HasCarry == true && _flight.LandedAt < 0f
            && ReferenceEquals(context.CurrentState, _flight.Jump) && Owns(_flight, context);

    // Only the native hard-fall call site uses this notification. Do not touch
    // either Fall speed limit globally; OnJumpEnd retains all vanilla side effects.
    internal static void HardLanding(MovementContext context)
    {
        // EFT applies this slowdown even for 0.3m ordinary jumps. Only a drop
        // below our takeoff floor breaks a hop; Jump Boots landing at that floor
        // remains eligible. Native damage and the actual Fall limit still run.
        if (ReferenceEquals(_flight?.Player?.MovementContext, context)
            && context.TransformPosition.y < _flight.LaunchY - .35f) Reset();
    }

    private static void BeforeMotion(MovementContext __instance, float deltaTime, out MotionSample __state)
    {
        __state = default;
        var f = _flight;
        if (f == null || !ReferenceEquals(f.Player?.MovementContext, __instance)) return;
        try
        {
            if (!Owns(f, __instance)) { Reset(); return; }
            if (!Finite(deltaTime) || deltaTime <= 0f || deltaTime > .1f) { Reset(); return; }
            f.Sampling = true;
            f.SawContact = false;
            __state = new MotionSample { Flight = f, Position = __instance.TransformPosition, Dt = deltaTime };
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void AfterMotion(MovementContext __instance, bool __runOriginal, MotionSample __state)
    {
        var f = __state.Flight;
        if (f != null) f.Sampling = false;
        if (f == null || !ReferenceEquals(f, _flight)) return;
        try
        {
            if (!__runOriginal || !Owns(f, __instance)) { Reset(); return; }
            if (f.SawContact && f.LeftGround && f.Jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround
                && f.Jump._currentLiftforce.y <= 0f) f.ContactAt = Time.time;
            var velocity = (__instance.TransformPosition - __state.Position) / __state.Dt;
            velocity.y = 0f;
            float speed = Planar(velocity);
            if (!Finite(speed) || speed > AbsoluteCap * 2f) { Reset(); return; }
            // A blocked native move cannot bank pre-wall momentum for a later hop.
            float requested = Planar(__instance.InputMotion);
            if (requested > 1f && speed < requested * .6f) { Reset(); return; }
            if (f.LandedAt >= 0f) return;
            if (!ReferenceEquals(f.Jump, __instance.CurrentState)) return;
            f.SampleAt = Time.time;
            f.Velocity = velocity;
            if (f.BaseSpeed == 0f && speed >= 1f && !Contact(f.Player)) f.BaseSpeed = Mathf.Min(speed, AbsoluteCap);
            if (f.HasCarry && speed < Planar(f.Carry)) f.Carry = velocity;
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static float Planar(Vector3 value) => Mathf.Sqrt(value.x * value.x + value.z * value.z);
    private static Exception MotionFailed(Exception __exception, MotionSample __state)
    {
        if (__state.Flight != null) __state.Flight.Sampling = false;
        if (__exception != null && ReferenceEquals(_flight, __state.Flight)) Reset();
        return __exception;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bunnyhop failed open: " + ex.GetBaseException().Message);
    }
}
