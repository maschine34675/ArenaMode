using ArenaMode.Config;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using Redeploy.Core.Engine;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Observes native input/contact/entry; never owns a jump command or calls Move.</summary>
internal static class ArenaSlideTransitions
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SlideTransitions");
    private static ArenaSlideController _slide;
    private static GameWorld _world;
    private static AbstractGame _game;
    private static MoveInputTranslator _input;
    private static object _controller;
    private static long _epoch;
    private static bool _ready, _warned, _sampling, _contact;

    private struct MotionSample
    {
        internal ArenaSlideController Slide;
        internal Vector3 Position;
        internal float Dt;
        internal long Epoch;
    }

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            Patches.Patch(AccessTools.DeclaredMethod(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateCommand)),
                postfix: Hook(nameof(AfterCommand)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(PlayerOwner), nameof(PlayerOwner.CreateInputTranslators)),
                prefix: Hook(nameof(InputReplaced)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter)),
                postfix: Hook(nameof(AfterJump)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion)),
                prefix: Hook(nameof(BeforeMotion)), postfix: Hook(nameof(AfterMotion)), finalizer: Hook(nameof(MotionFailed)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(SimpleCharacterController), nameof(SimpleCharacterController.SingleMove)),
                postfix: Hook(nameof(AfterSubstep)));
            Settings.SlideEnabled.SettingChanged += Changed;
            Settings.MovementEnabled.SettingChanged += Changed;
            Settings.WalkSpeedMult.SettingChanged += Changed;
            Settings.SprintSpeedMult.SettingChanged += Changed;
            Settings.ExtraMotionScale.SettingChanged += Changed;
            RaidLifecycle.OnRaidEnded += Reset;
            _ready = true;
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }

    private static HarmonyMethod Hook(string method) => new(typeof(ArenaSlideTransitions).GetMethod(method,
        BindingFlags.NonPublic | BindingFlags.Static));

    internal static void Shutdown()
    {
        _ready = false;
        Reset();
        Settings.SlideEnabled.SettingChanged -= Changed;
        Settings.MovementEnabled.SettingChanged -= Changed;
        Settings.WalkSpeedMult.SettingChanged -= Changed;
        Settings.SprintSpeedMult.SettingChanged -= Changed;
        Settings.ExtraMotionScale.SettingChanged -= Changed;
        RaidLifecycle.OnRaidEnded -= Reset;
        Patches.UnpatchSelf();
    }

    private static void Changed(object sender, EventArgs args) => Reset();
    private static void Reset()
    {
        unchecked { _epoch++; }
        if (_slide != null) { _slide.Clear(); _slide.enabled = false; }
        _slide = null;
        _world = null;
        _game = null;
        _input = null;
        _controller = null;
        _sampling = _contact = false;
    }

    internal static void Bind(ArenaSlideController slide)
    {
        Reset();
        _slide = slide;
        _world = Singleton<GameWorld>.Instance;
        _game = Singleton<AbstractGame>.Instance;
        _controller = slide.Player?.CharacterController;
        slide.enabled = true;
    }

    internal static bool Owns(ArenaSlideController slide)
    {
        var p = slide?.Player;
        var c = slide?.Context;
        return _ready && ReferenceEquals(slide, _slide) && Settings.SlideEnabled?.Value == true
            && ArenaMovementScope.IsLocal(p, requireInput: true)
            && ReferenceEquals(c, p.MovementContext) && ReferenceEquals(c.CurrentState, p.CurrentManagedState)
            && ReferenceEquals(_world, Singleton<GameWorld>.Instance) && ReferenceEquals(_game, Singleton<AbstractGame>.Instance)
            && (_input == null || ReferenceEquals(_input._controller, p))
            && c.OverridenState == null && c.OverridenControlsState == null && p.CharacterController != null
            && ReferenceEquals(_controller, p.CharacterController)
            && c.PlayerAnimator != null && !c.IsInPronePose && !c.IsInMountedState && !c.IsStationaryWeaponInHands
            && !c.IsAxesIgnored && c.BlindFire == 0 && c.CanWalk && c.CanJump
            && !c.PlayerAnimatorGetIsVaulting() && !ArenaAirVault.HasPendingRequest(p)
            && c.Platform == null && c.PlatformMotion == Vector3.zero && !(c.ImpostorCC?.UseAsyncMoveValidation == true);
    }

    internal static bool GroundState(BaseMovementState state) => state != null
        && (state.GetType() == typeof(IdlePlayerState) || state.GetType() == typeof(MovePlayerState)
            || state.GetType() == typeof(SprintPlayerState));
    internal static bool AirState(BaseMovementState state) => state?.GetType() == typeof(JumpPlayerState) || state?.GetType() == typeof(FallDown);
    internal static bool SlideState(BaseMovementState state) => GroundState(state) || state?.GetType() == typeof(JumpLandingState);
    internal static bool Supported(MovementContext c) => c.IsGrounded && c.GroundHit.hasHit && c.GroundHit.normal.y >= .65f;
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool ValidDelta(float dt) => Finite(dt) && dt > 0f && dt <= .1f;
    internal static long OwnerEpoch => _epoch;
    internal static bool HasActiveSlide(Player player) => _slide != null && ReferenceEquals(_slide.Player, player)
        && _slide.IsSliding && Owns(_slide) && _slide.IsSlideState(_slide.Context.CurrentState) && Supported(_slide.Context);
    internal static bool NeedsBufferedJump(Player player) => HasActiveSlide(player) && _slide.NeedsBufferedJump;
    internal static float BufferedJumpWindow(Player player) => HasActiveSlide(player) ? _slide.BufferedJumpWindow : 0f;

    internal static void TickOwner()
    {
        if (_slide == null) return;
        try { if (!Owns(_slide)) Reset(); }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    internal static bool TryApplyMotion(MovementContext context, ref Vector3 motion, float deltaTime)
    {
        if (_slide == null || !ReferenceEquals(_slide.Context, context)) return false;
        try { return _slide.ApplyMotion(ref motion, deltaTime); }
        catch (Exception ex) { Reset(); Warn(ex); return false; }
    }

    internal static void PrepareJump(Player player)
    {
        if (_slide == null || !ReferenceEquals(_slide.Player, player)) return;
        try { _slide.PrepareJump(); }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void InputReplaced(PlayerOwner __instance)
    {
        if (_slide != null && ReferenceEquals(__instance.Player, _slide.Player)) Reset();
    }

    private static void AfterCommand(MoveInputTranslator __instance, ECommand command, bool __runOriginal)
    {
        if (_slide == null || !ReferenceEquals(__instance?._controller, _slide.Player)) return;
        try
        {
            if (!__runOriginal || !Owns(_slide)) { Reset(); return; }
            if (_input != null && !ReferenceEquals(_input, __instance)) { Reset(); return; }
            _input = __instance;
            if (command == ECommand.ToggleDuck || command == ECommand.PreviousWalkPose) _slide.CrouchIntent();
            else if (command == ECommand.RestorePose || command == ECommand.NextWalkPose
                || command == ECommand.ToggleProne || command == ECommand.Vaulting || command == ECommand.VaultingEnd)
                _slide.CancelInputIntent();
            else if (command == ECommand.Jump) _slide.CancelLanding();
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void AfterJump(JumpPlayerState __instance, bool __runOriginal)
    {
        if (_slide == null || !ReferenceEquals(_slide.Context, __instance.MovementContext)) return;
        try
        {
            if (!__runOriginal || !Owns(_slide)) { Reset(); return; }
            _slide.JumpEntered(__instance);
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void BeforeMotion(MovementContext __instance, float deltaTime, out MotionSample __state)
    {
        __state = default;
        if (_slide == null || !ReferenceEquals(_slide.Context, __instance)) return;
        try
        {
            if (!Owns(_slide) || !ValidDelta(deltaTime) || _sampling) { Reset(); return; }
            _sampling = true;
            _contact = false;
            __state = new MotionSample { Slide = _slide, Position = __instance.TransformPosition, Dt = deltaTime, Epoch = _epoch };
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static void AfterSubstep(SimpleCharacterController __instance)
    {
        if (_sampling && _slide != null && ReferenceEquals(_slide.Player.CharacterController, __instance)
            && __instance.isGrounded) _contact = true;
    }

    private static void AfterMotion(MovementContext __instance, bool __runOriginal, MotionSample __state)
    {
        if (__state.Slide == null || !ReferenceEquals(_slide, __state.Slide) || __state.Epoch != _epoch) return;
        _sampling = false;
        try
        {
            if (!__runOriginal || !Owns(_slide)) { Reset(); return; }
            var velocity = (__instance.TransformPosition - __state.Position) / __state.Dt;
            var requested = __instance.InputMotion;
            velocity.y = requested.y = 0f;
            if (!Finite(requested.sqrMagnitude)) { Reset(); return; }
            // Depenetration/platform-like external displacement cannot create
            // momentum beyond this native transaction's requested XZ speed.
            velocity = Vector3.ClampMagnitude(velocity, requested.magnitude);
            _slide.AppliedMotion(velocity, _contact, requested.magnitude);
        }
        catch (Exception ex) { Reset(); Warn(ex); }
    }

    private static Exception MotionFailed(Exception __exception, MotionSample __state)
    {
        if (__state.Epoch != _epoch || __state.Slide == null || !ReferenceEquals(_slide, __state.Slide)) return __exception;
        _sampling = false;
        if (__exception != null) Reset();
        return __exception;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena slide transitions failed open: " + ex.GetBaseException().Message);
    }
}
