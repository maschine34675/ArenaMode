#nullable disable
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Adds only horizontal root-motion displacement to live Arena AI. Normalized
/// movement/animator targets saturate; speed must be applied before native
/// collision instead. No path, pause, sprint, stamina or animation state writes.
/// </summary>
internal static class ArenaBotMovement
{
    private static AccessTools.FieldRef<MovementContext, Player> _player;
    private static bool _enabled, _warned;
    [ThreadStatic] private static HashSet<MovementContext> _active;
    private static WeakReference _loggedWorld;
    private static float _nextDiagnostic;
    private static int _boosted, _cornerLimited, _stepLimited;

    internal sealed class MotionLease
    {
        internal MovementContext Context;
        internal ICharacterController Controller;
        internal float OriginalLimit, WrittenLimit;
        internal bool LimitWritten;
    }

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            _player = AccessTools.FieldRefAccess<MovementContext, Player>("_player");
            ArenaBotMovementSain.Enable();
            new ArenaBotMovementMotionPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot movement unavailable: " + ex.GetBaseException().Message);
    }

    // Reuse the modes' existing read-only live-phase admission. No new match
    // lifecycle or timing rules are introduced for movement.
    private static bool Live(IArenaMode mode) => mode != null && !mode.IsComplete && (mode switch
    {
        RoundsMode rounds => rounds.AssistScoringOpen,
        TacticalMode tactical => tactical.AssistScoringOpen,
        TeamDeathmatchMode teams => teams.AssistScoringOpen,
        GunGameMode gunGame => gunGame.AssistScoringOpen,
        _ => false
    });

    private static bool TryBot(MovementContext context, out Player player, out BotOwner owner, out GameWorld world)
    {
        player = null; owner = null; world = null;
        if (!_enabled || !ArenaSession.IsActive || Settings.BotMovementEnabled?.Value != true
            || context == null || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !Live(ArenaMatchService.Current?.Mode) || ArenaMatchService.TeamStartAbortPending
            || Areas.ArenaAreaRuntime.IsPending) return false;
        world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        if (world == null || game == null || game.Status != GameStatus.Started
            || !ArenaSession.IsExactActiveRaid(game, world)) return false;
        player = _player(context);
        // SAIN overrides MovementContext.IsAI to false. Use the actual body and
        // owning BotOwner, never that patched convenience getter or a profile alias.
        if (player == null || !player.IsAI || player.IsYourPlayer
            || ReferenceEquals(world.MainPlayer, player) || player.HealthController?.IsAlive != true
            || !ReferenceEquals(player.MovementContext, context)
            || string.IsNullOrEmpty(player.ProfileId)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)) return false;
        owner = player.AIData?.BotOwner;
        return owner != null && owner.BotState == EBotState.Active && ReferenceEquals(owner.GetPlayer, player)
            && ReferenceEquals(player.CharacterController, context.CharacterController);
    }

    // One admission contract for displacement and the optional SAIN tempo adapter.
    internal static bool IsLiveBot(BotOwner owner)
    {
        var body = owner == null ? null : owner.GetPlayer;
        return body != null && TryBot(body.MovementContext, out var actual, out var actualOwner, out _)
            && ReferenceEquals(body, actual) && ReferenceEquals(owner, actualOwner);
    }

    internal static bool TryGetLiveBot(MovementContext context, out Player body, out BotOwner owner)
        => TryBot(context, out body, out owner, out _);

    internal static MotionLease Begin(MovementContext context, ref Vector3 motion, float deltaTime)
    {
        MotionLease lease = null;
        Vector3 incoming = motion;
        try
        {
            if (!Finite(deltaTime) || deltaTime <= 0f || Time.timeScale <= 0f
                || !Finite(motion.x) || !Finite(motion.y) || !Finite(motion.z)
                || (_active?.Contains(context) == true)
                || !TryBot(context, out var player, out var owner, out var world)) return null;
            var state = context.CurrentState?.Name ?? EPlayerState.None;
            if ((state != EPlayerState.Run && state != EPlayerState.Sprint) || !context.IsGrounded
                || context.IsInPronePose || context.IsInMountedState || context.PlatformMotion != Vector3.zero
                || context.StationaryWeapon != null || owner.DoorOpener?.Interacting == true
                || owner.DoorOpener?.NearDoor == true || owner.Mover?.CurrentState == EBotMoverState.NearDoor
                || !ArenaBotMovementSain.AllowsBoost(owner, player)) return null;

            float requested = (state == EPlayerState.Sprint ? Settings.BotSprintSpeedMult : Settings.BotWalkSpeedMult)?.Value ?? 1f;
            if (!Finite(requested) || requested <= 1f) return null;
            double factor = Math.Min(requested, 1.5f);
            bool cornerLimited = false;
            var mover = owner.Mover;
            // SAIN may stop/pause the native mover and drive Player.Move itself.
            // Only use a native corner when native movement actually owns a path;
            // do not reject a valid SAIN strafe because the old native path is idle.
            if (mover != null && !mover.Pause && mover.HasPathAndNoComplete)
            {
                Vector3 remaining = mover.CurrentCornerPoint - player.Position;
                if (!Finite(remaining.x) || !Finite(remaining.z)) return null;
                double distance = Math.Sqrt((double)remaining.x * remaining.x + (double)remaining.z * remaining.z);
                double blend = Math.Max(0d, Math.Min(1d, (distance - 0.75d) / 1.25d));
                factor = 1d + (factor - 1d) * blend;
                cornerLimited = blend < 1d;
            }

            double horizontalSquared = (double)motion.x * motion.x + (double)motion.z * motion.z;
            double verticalSquared = (double)motion.y * motion.y;
            const double maximumStepSquared = 0.3d * 0.3d;
            // The alternate native Impostor mover accounts for at most 0.3m per
            // update. Reduce only our bonus, never a larger native step or Y.
            if (horizontalSquared <= 0d) return null;
            if (horizontalSquared + verticalSquared >= maximumStepSquared)
            { Diagnose(world, false, cornerLimited, true); return null; }
            bool stepLimited = horizontalSquared * factor * factor + verticalSquared > maximumStepSquared;
            factor = Math.Min(factor, Math.Sqrt((maximumStepSquared - verticalSquared) / horizontalSquared));
            float effective = (float)factor;
            if (!Finite(effective) || effective <= 1f)
            { Diagnose(world, false, cornerLimited, stepLimited); return null; }
            var controller = context.CharacterController;
            if (controller == null) return null;
            float limit = controller.SpeedLimit;
            if (!Finite(limit) || limit == 0f) return null;
            float raised = limit > 0f ? limit * effective : limit;
            if (!Finite(raised)) return null;
            lease = new MotionLease { Context = context, Controller = controller, OriginalLimit = limit, WrittenLimit = raised };
            (_active ??= new HashSet<MovementContext>()).Add(context);
            if (limit > 0f)
            {
                // Mark before the setter: a throwing modded setter may already
                // have written the value and must still receive cleanup.
                lease.LimitWritten = true;
                controller.SpeedLimit = raised;
            }
            motion.x *= effective;
            motion.z *= effective;
            Diagnose(world, true, cornerLimited, stepLimited);
            return lease;
        }
        catch (Exception ex)
        {
            motion = incoming;
            End(lease);
            Warn(ex);
            return null;
        }
    }

    private static void Diagnose(GameWorld world, bool boosted, bool corner, bool step)
    {
        if (!ReferenceEquals(_loggedWorld?.Target, world))
        {
            _loggedWorld = new WeakReference(world); _nextDiagnostic = Time.time + 60f;
            _boosted = _cornerLimited = _stepLimited = 0;
            Plugin.ModLogger.LogDebug($"Arena bot movement active: walk x{Settings.BotWalkSpeedMult.Value:0.##}, sprint x{Settings.BotSprintSpeedMult.Value:0.##}; both teams, grounded motion only, native collision retained.");
        }
        if (boosted) _boosted++;
        if (corner) _cornerLimited++;
        if (step) _stepLimited++;
        if (Time.time < _nextDiagnostic) return;
        _nextDiagnostic = Time.time + 60f;
        Plugin.ModLogger.LogDebug($"Arena bot movement (last interval): boostedCalls={_boosted}, cornerLimitedCalls={_cornerLimited}, stepLimitedCalls={_stepLimited}; overlapping counts, 0.3m safety ceiling unchanged.");
        _boosted = _cornerLimited = _stepLimited = 0;
    }

    internal static void End(MotionLease lease)
    {
        if (lease == null) return;
        try
        {
            // Restore the actual captured controller even after a body/raid swap.
            // A different value written by another owner is not ours to undo.
            if (lease.LimitWritten && lease.Controller.SpeedLimit == lease.WrittenLimit)
                lease.Controller.SpeedLimit = lease.OriginalLimit;
        }
        catch (Exception ex) { Warn(ex); }
        finally { _active?.Remove(lease.Context); }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed class ArenaBotMovementMotionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion),
                new[] { typeof(Vector3), typeof(float) });
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(MovementContext __instance, ref Vector3 motion, float deltaTime, out MotionLease __state)
            => __state = Begin(__instance, ref motion, deltaTime);
        [PatchFinalizer, HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception, MotionLease __state)
        {
            End(__state);
            return __exception;
        }
    }
}
