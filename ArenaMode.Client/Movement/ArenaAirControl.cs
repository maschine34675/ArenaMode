using ArenaMode.Config;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Bounded steering before native collision; never adds lift or planar speed.</summary>
internal static class ArenaAirControl
{
    private sealed class Flight
    {
        internal Player Player;
        internal WeakReference<GameWorld> World;
        internal WeakReference<AbstractGame> Game;
        internal Vector3 Heading;
        internal bool HasHeading;
        internal Vector2 Input;
        internal MovementState InputState;
        internal int InputFrame = -1;
    }

    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".AirControl");
    private static ConditionalWeakTable<MovementContext, Flight> _flights = new();
    private static bool _ready, _bound, _warned;
    private static readonly MethodInfo Limit = AccessTools.DeclaredMethod(typeof(MovementState), nameof(MovementState.LimitMotion));

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            _bound = false;
            Patches.Patch(AccessTools.DeclaredMethod(typeof(JumpPlayerState), nameof(JumpPlayerState.ProcessJumpMovement)),
                transpiler: Hook(nameof(Transpile)));
            if (!_bound) throw new InvalidOperationException("unique native jump collision call not found");
            Patches.Patch(AccessTools.DeclaredMethod(typeof(Player), nameof(Player.Move)),
                prefix: Hook(nameof(BeforeMove)), postfix: Hook(nameof(AfterMove)));
            Patches.Patch(AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.CheckFlying)), postfix: Hook(nameof(AfterFlying)));
            _ready = true;
            Plugin.ModLogger.LogInfo("Arena air control: native jump/fall steering ready (planar speed preserved).");
        }
        catch (Exception ex) { Shutdown(); Warn(ex); }
    }

    private static HarmonyMethod Hook(string name)
        => new(typeof(ArenaAirControl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

    internal static void Shutdown()
    {
        _ready = _bound = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex) { Warn(ex); }
        finally { _flights = new(); }
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var matches = code.Where(x => x.opcode == OpCodes.Callvirt && Equals(x.operand, Limit)).ToArray();
        if (matches.Length != 1) { _bound = false; return code; }
        // The stack already holds state, ref motion, dt. Retain labels/blocks.
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = typeof(ArenaAirControl).GetMethod(nameof(SteerThenLimit), BindingFlags.NonPublic | BindingFlags.Static);
        _bound = true;
        return code;
    }

    private static bool Eligible(MovementState state, out Player player)
    {
        player = null;
        if (!_ready || Settings.AirControlEnabled?.Value != true || state == null
            || (state.GetType() != typeof(JumpPlayerState) && state.GetType() != typeof(FallDown))
            || !ArenaMovementScope.TryGetPlayer(state.MovementContext, out player)
            || !ReferenceEquals(player.CurrentManagedState, state) || !ReferenceEquals(state.MovementContext.CurrentState, state)) return false;
        var context = state.MovementContext;
        return context.OverridenState == null && context.OverridenControlsState == null
            && player.CharacterController != null && !context.IsInPronePose && !context.IsInMountedState
            && !context.IsStationaryWeaponInHands && !context.PlayerAnimatorGetIsVaulting()
            && !ArenaAirVault.HasPendingRequest(player) && player.GetComponent<ArenaSlideController>()?.IsSliding != true;
    }

    private static Flight Observe(Player player)
    {
        var context = player.MovementContext;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance;
        if (_flights.TryGetValue(context, out var flight)
            && ReferenceEquals(flight.Player, player)
            && flight.World.TryGetTarget(out var oldWorld) && ReferenceEquals(oldWorld, world)
            && flight.Game.TryGetTarget(out var oldGame) && ReferenceEquals(oldGame, game)) return flight;
        _flights.Remove(context);
        flight = new Flight { Player = player, World = new WeakReference<GameWorld>(world), Game = new WeakReference<AbstractGame>(game) };
        _flights.Add(context, flight);
        return flight;
    }

    private static void BeforeMove(Player __instance, out MovementState __state)
    {
        __state = null;
        try
        {
            if (_ready && ArenaMovementScope.IsLocal(__instance, requireInput: true))
                __state = __instance.CurrentManagedState;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void AfterMove(Player __instance, Vector2 __0, bool __runOriginal, MovementState __state)
    {
        try
        {
            var context = __instance?.MovementContext;
            if (context == null) return;
            if (_flights.TryGetValue(context, out var old)) old.InputFrame = -1;
            var state = __instance.CurrentManagedState;
            if (!__runOriginal || __state == null || !ReferenceEquals(__state, state)
                || !Eligible(state, out var player) || !ReferenceEquals(player, __instance)
                || !ArenaMovementScope.IsLocal(player, requireInput: true) || context.IsAxesIgnored || context.BlindFire != 0
                || !Finite(__0.x) || !Finite(__0.y)) return;
            var flight = Observe(player);
            flight.Input = __0;
            flight.InputState = state;
            flight.InputFrame = Time.frameCount;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void AfterFlying(MovementContext __instance)
    {
        try
        {
            if (!_flights.TryGetValue(__instance, out var flight)) return;
            if (!_ready || Settings.AirControlEnabled?.Value != true || !ArenaMovementScope.IsLocal(flight.Player)
                || !ReferenceEquals(flight.Player.MovementContext, __instance)) { _flights.Remove(__instance); return; }
            var state = flight.Player.CurrentManagedState;
            bool landedPhase = state is not JumpPlayerState jump
                || (jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround && jump._currentLiftforce.y <= 0f);
            // IsGrounded includes a proximity ray; upward near-ground samples
            // and double-jump Enter are not new flights. Require real contact.
            if (__instance.IsGrounded && flight.Player.CharacterController?.isGrounded == true && landedPhase
                && !__instance.PlayerAnimatorGetIsVaulting() && !ArenaAirVault.HasPendingRequest(flight.Player)) _flights.Remove(__instance);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void SteerThenLimit(MovementState state, ref Vector3 motion, float dt)
    {
        var original = motion;
        try { Steer(state, ref motion, dt); }
        catch (Exception ex) { motion = original; Warn(ex); }
        // Outside our catch: native collision errors are never suppressed and
        // physical clipping is never undone. The existing speed scaler runs later.
        state.LimitMotion(ref motion, dt);
    }

    private static void Steer(MovementState state, ref Vector3 motion, float dt)
    {
        if (!Eligible(state, out var player) || !Finite(dt) || dt <= 0f || dt > 0.25f
            || !Finite(motion.x) || !Finite(motion.y) || !Finite(motion.z)) return;
        var context = state.MovementContext;
        var jump = (JumpPlayerState)state;
        if (jump._jumpState != JumpPlayerState.EJumpState.Jump || context.IsGrounded || player.CharacterController.isGrounded) return;
        float degrees = Settings.AirControlDegreesPerSecond.Value;
        if (!Finite(degrees) || degrees <= 0f) { _flights.Remove(context); return; }
        var flight = Observe(player);
        double length = Math.Sqrt((double)motion.x * motion.x + (double)motion.z * motion.z);
        if (length <= 0.000001 || length > float.MaxValue) return; // No new standing-jump impulse.
        if (!flight.HasHeading)
        {
            flight.Heading = new Vector3((float)(motion.x / length), 0f, (float)(motion.z / length));
            flight.HasHeading = true;
        }
        if (flight.InputFrame == Time.frameCount && ReferenceEquals(flight.InputState, state)
            && ArenaMovementScope.IsLocal(player, requireInput: true) && !context.IsAxesIgnored && context.BlindFire == 0
            && flight.Input.sqrMagnitude > 0.01f)
        {
            var desired = context.TransformVector(new Vector3(flight.Input.x, 0f, flight.Input.y));
            if (Finite(desired.x) && Finite(desired.z) && ((double)desired.x * desired.x + (double)desired.z * desired.z) > 0.000001)
            {
                double current = Math.Atan2(flight.Heading.x, flight.Heading.z);
                double target = Math.Atan2(desired.x, desired.z);
                double delta = Math.Atan2(Math.Sin(target - current), Math.Cos(target - current));
                double max = Math.Min(degrees, 180f) * Math.PI / 180d * Math.Min(dt, 0.1f);
                double angle = current + Math.Max(-max, Math.Min(max, delta));
                flight.Heading = new Vector3((float)Math.Sin(angle), 0f, (float)Math.Cos(angle));
            }
        }
        // Heading persists without input; otherwise releasing WASD would snap
        // back to the native takeoff direction. Preserve the native XZ magnitude.
        motion.x = (float)(flight.Heading.x * length);
        motion.z = (float)(flight.Heading.z * length);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena air control unavailable: " + ex.GetBaseException().Message);
    }
}
