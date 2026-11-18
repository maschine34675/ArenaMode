using ArenaMode.Areas;
using ArenaMode.Movement;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Combat.Translocator;

/// <summary>
/// Same-body relocation. Native Teleport owns camera/environment/fall origins;
/// only position-dependent prediction is translated. Momentum, animator phase,
/// freefall time and consumed jump credit remain native. Old controller contact
/// is invalidated, then native collision sensing resolves the new support.
/// </summary>
internal static class ArenaTranslocatorTeleport
{
    private static readonly Collider[] Neighbours = new Collider[96];
    private static bool _executing, _checking, _warned;

    internal static bool TryTeleport(Player player, Vector3 target, out string reason)
    {
        reason = "teleport-busy";
        if (_executing) return false;
        if (!TryValidateDestination(player, target, out reason)) return false;
        var before = new SpatialHistory(player);
        _executing = true;
        ArenaAreaTranslocatorGate.Ticket ticket = null;
        Exception nativeError = null;
        try
        {
            ticket = ArenaAreaTranslocatorGate.Begin(player, target);
            if (ticket == null) { reason = "teleport-authorization-unavailable"; return false; }
            try { player.Teleport(target); }
            catch (Exception error) { nativeError = error; }
            if (!before.IsCurrent()) { reason = "teleport-owner-changed"; return false; }
            Vector3 actual = player.Position;
            if (!ArenaTranslocatorTeleportPolicy.Finite(actual)) { reason = "teleport-invalid-readback"; return false; }
            Vector3 delta = actual - before.Position;
            // Even a foreign postfix throwing after movement must not leave an
            // old jump-height prediction capable of adding a huge false lift.
            if (delta.sqrMagnitude > 0f)
            {
                before.Rebase(delta);
                if (!before.RefreshContact()) { reason = "teleport-owner-changed"; return false; }
                actual = player.Position;
            }
            if (nativeError != null) { Warn(nativeError); reason = "native-teleport-error"; return false; }
            if (!ticket.Consumed || !ArenaTranslocatorTeleportPolicy.Arrived(actual, target)
                || !ArenaTranslocatorRuntime.IsOwnedDestination(player, target))
            { reason = "teleport-not-confirmed"; return false; }
            reason = null;
            return true;
        }
        catch (Exception error) { Warn(error); reason = "teleport-validation-error"; return false; }
        finally { ticket?.Dispose(); _executing = false; }
    }

    internal static bool TryValidateDestination(Player player, Vector3 target, out string reason)
    {
        reason = "destination-unavailable";
        if (_checking) return false;
        _checking = true;
        try
        {
            if (!ArenaAreaNavigationPatches.IsReady) { reason = "teleport-hook-unavailable"; return false; }
            if (!ArenaTranslocatorTeleportPolicy.Finite(target)
                || !ArenaTranslocatorRuntime.IsLivePlayer(player)
                || !ArenaTranslocatorRuntime.IsOwnedDestination(player, target)) return false;
            var context = player.MovementContext;
            var state = player.CurrentManagedState;
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance;
            var area = ArenaAreaRuntime.CurrentArea;
            if (context == null || state == null || !ReferenceEquals(context.CurrentState, state)
                || context.OverridenState != null || context.OverridenControlsState != null
                || context.IsInPronePose || context.IsInMountedState || context.IsStationaryWeaponInHands
                || context.IsAxesIgnored || context.BlindFire != 0 || context.PlayerAnimatorGetIsVaulting()
                || ArenaAirVault.HasRelocationConflict(player) || context.Platform != null
                || context.PlatformMotion != Vector3.zero || context.ImpostorCC?.UseAsyncMoveValidation == true
                || player.GetComponent<ArenaSlideController>()?.IsSliding == true
                || !SupportedState(state)) { reason = "movement-busy"; return false; }
            if (!(player.CharacterController is SimpleCharacterController controller)
                || controller._mainCollider == null || controller._colliders == null
                || controller._colliders.Length > 16 || controller._collisionMask.value == 0
                || !Active(controller._mainCollider)) { reason = "player-collider-unavailable"; return false; }
            if (!ArenaTranslocatorTeleportPolicy.Finite(player.Position)) return false;
            Physics.SyncTransforms(); // Explicit click: a door moved this frame must already block admission.
            Vector3 delta = target - player.Position;
            var capsule = controller._mainCollider;
            if (!TryCapsule(capsule, delta, out var bottom, out var top, out var radius)
                || !ArenaAreaTranslocatorGate.ContainsCapsule(bottom, top, radius))
            { reason = "outside-arena"; return false; }
            if (!ClearSolid(player, capsule, delta, controller._collisionMask.value))
            { reason = "target-obstructed"; return false; }
            foreach (var collider in controller._colliders)
                if (Active(collider) && !ReferenceEquals(collider, capsule)
                    && !ClearSolid(player, collider, delta, controller._collisionMask.value))
                { reason = "target-obstructed"; return false; }
            if (world?.AllAlivePlayersList == null) return false;
            if (!ClearActors(world, player, capsule, delta)) { reason = "target-occupied"; return false; }
            foreach (var collider in controller._colliders)
                if (Active(collider) && !ReferenceEquals(collider, capsule) && !ClearActors(world, player, collider, delta))
                { reason = "target-occupied"; return false; }
            if (!ArenaTranslocatorRuntime.IsLivePlayer(player)
                || !ArenaTranslocatorRuntime.IsOwnedDestination(player, target)
                || !ReferenceEquals(context, player.MovementContext) || !ReferenceEquals(state, player.CurrentManagedState)
                || !ReferenceEquals(state, context.CurrentState) || !ReferenceEquals(controller, player.CharacterController)
                || !ReferenceEquals(capsule, controller._mainCollider) || !ReferenceEquals(area, ArenaAreaRuntime.CurrentArea)
                || !ReferenceEquals(world, Singleton<GameWorld>.Instance) || !ReferenceEquals(game, Singleton<AbstractGame>.Instance)) return false;
            reason = null;
            return true;
        }
        catch (Exception error) { Warn(error); reason = "destination-check-failed"; return false; }
        finally { Array.Clear(Neighbours, 0, Neighbours.Length); _checking = false; }
    }

    private static bool SupportedState(MovementState state)
        => state.GetType() == typeof(IdlePlayerState) || state.GetType() == typeof(MovePlayerState)
            || state.GetType() == typeof(SprintPlayerState) || state.GetType() == typeof(JumpPlayerState)
            || state.GetType() == typeof(FallDown);

    private static bool Active(Collider collider) => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

    private static bool TryCapsule(CapsuleCollider capsule, Vector3 delta, out Vector3 bottom, out Vector3 top, out float radius)
    {
        bottom = top = default; radius = 0f;
        var scale = capsule.transform.lossyScale;
        scale = new Vector3(Math.Abs(scale.x), Math.Abs(scale.y), Math.Abs(scale.z));
        Vector3 axis; float lengthScale, widthScale;
        switch (capsule.direction)
        {
            case 0: axis = Vector3.right; lengthScale = scale.x; widthScale = Math.Max(scale.y, scale.z); break;
            case 1: axis = Vector3.up; lengthScale = scale.y; widthScale = Math.Max(scale.x, scale.z); break;
            case 2: axis = Vector3.forward; lengthScale = scale.z; widthScale = Math.Max(scale.x, scale.y); break;
            default: return false;
        }
        radius = capsule.radius * widthScale;
        float height = capsule.height * lengthScale;
        if (!ArenaTranslocatorTeleportPolicy.Finite(radius) || !ArenaTranslocatorTeleportPolicy.Finite(height)
            || radius <= 0f || height < radius * 2f) return false;
        Vector3 center = capsule.transform.TransformPoint(capsule.center) + delta;
        Vector3 half = capsule.transform.TransformDirection(axis).normalized * (height * .5f - radius);
        bottom = center - half; top = center + half;
        return ArenaTranslocatorTeleportPolicy.Finite(bottom) && ArenaTranslocatorTeleportPolicy.Finite(top);
    }

    private static bool ClearSolid(Player player, Collider shape, Vector3 delta, int mask)
    {
        if (!(shape is CapsuleCollider) && !(shape is SphereCollider) && !(shape is BoxCollider)) return false;
        Bounds bounds = shape.bounds;
        Vector3 center = bounds.center + delta;
        Vector3 extent = bounds.extents + Vector3.one * .01f;
        if (!ArenaTranslocatorTeleportPolicy.Finite(center) || !ArenaTranslocatorTeleportPolicy.Finite(extent)) return false;
        int count = Physics.OverlapBoxNonAlloc(center, extent, Neighbours, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
        if (count >= Neighbours.Length) return false; // Saturation is unknown space, never clearance.
        for (int i = 0; i < count; i++)
        {
            var obstacle = Neighbours[i];
            if (!Active(obstacle) || obstacle.isTrigger || ReferenceEquals(obstacle, shape)
                || obstacle.transform.IsChildOf(player.Transform.Original)) continue;
            if (Penetrates(shape, delta, obstacle)) return false;
        }
        return true;
    }

    private static bool Penetrates(Collider shape, Vector3 delta, Collider obstacle)
        => Physics.ComputePenetration(shape, shape.transform.position + delta, shape.transform.rotation,
            obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float distance)
            && (!ArenaTranslocatorTeleportPolicy.Finite(distance) || distance > ArenaTranslocatorTeleportPolicy.ContactTolerance);

    private static bool ClearActors(GameWorld world, Player player, Collider shape, Vector3 delta)
    {
        var targetBounds = shape.bounds; targetBounds.center += delta;
        foreach (var actor in world.AllAlivePlayersList)
        {
            if (!(actor is Player other) || ReferenceEquals(other, player) || other.HealthController?.IsAlive != true) continue;
            var parts = other.PlayerBones?.BodyPartCollidersHashSet;
            if (parts == null || parts.Count == 0) return false;
            foreach (var body in parts)
                // Hurt colliders may be triggers: occupancy is deliberately
                // separate from native solid masks and never a telefrag.
                if (Active(body) && targetBounds.Intersects(body.bounds) && Penetrates(shape, delta, body)) return false;
        }
        return true;
    }

    private sealed class SpatialHistory
    {
        internal readonly Vector3 Position;
        private readonly Player _player;
        private readonly MovementContext _context;
        private readonly MovementState _state;
        private readonly object _controller;
        private readonly GameWorld _world;
        private readonly AbstractGame _game;
        internal SpatialHistory(Player player)
        {
            _player = player; Position = player.Position; _context = player.MovementContext;
            _state = player.CurrentManagedState; _controller = player.CharacterController;
            _world = Singleton<GameWorld>.Instance; _game = Singleton<AbstractGame>.Instance;
        }
        private bool IsBodyCurrent() => ArenaTranslocatorRuntime.IsLivePlayer(_player)
            && ReferenceEquals(_player.MovementContext, _context) && ReferenceEquals(_player.CharacterController, _controller)
            && ReferenceEquals(Singleton<GameWorld>.Instance, _world) && ReferenceEquals(Singleton<AbstractGame>.Instance, _game);
        internal bool IsCurrent() => IsBodyCurrent()
            && ReferenceEquals(_player.CurrentManagedState, _state) && ReferenceEquals(_context.CurrentState, _state);
        internal bool RefreshContact()
        {
            if (!IsCurrent() || !(_controller is SimpleCharacterController controller)) return false;
            // SimpleCC.Move skips zero motion, retaining its old isGrounded bit.
            // Native UpdateGroundCollision ORs that bit with the fresh ground ray;
            // discard the old witness first or an idle air destination never falls.
            // No Move/impulse, fabricated support, jump entry or phase reset.
            controller.isGrounded = false;
            _context.IsGrounded = false;
            // Retire both old witnesses even if synchronization/query fails;
            // a following input must never reuse support from the old location.
            Physics.SyncTransforms();
            if (!IsCurrent()) return false;
            _context.UpdateGroundCollision(0f);
            return IsBodyCurrent();
        }
        internal void Rebase(Vector3 delta)
        {
            if (_state.Destination.HasValue) _state.Destination += delta;
            if (_context._hasLastHeight) _context._lastPlayerHeight += delta.y;
            if (_state is JumpPlayerState jump)
            {
                jump._potentialH += delta.y;
                if (jump._hasLastHeight) jump._lastPlayerHeight += delta.y;
            }
            ArenaJumpReliability.OnRelocated(_player);
            ArenaBunnyhop.OnRelocated(_player, delta);
            ArenaDoubleJump.OnRelocated(_player);
            ArenaSlideTransitions.OnRelocated(_player);
            ArenaAirControl.OnRelocated(_player);
            ArenaAirVault.OnRelocated(_player);
        }
    }

    private static void Warn(Exception error)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena translocator teleport failed: " + error.GetBaseException().Message);
    }
}
