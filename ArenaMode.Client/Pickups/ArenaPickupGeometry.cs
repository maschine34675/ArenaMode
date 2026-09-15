using ArenaMode.Areas;
using ArenaMode.Shared;
using EFT;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Pickups;

internal static class ArenaPickupGeometry
{
    internal static bool TryAnchor(GameWorld world, out Vector3 anchor)
    {
        anchor = default;
        var player = world?.MainPlayer;
        if (!ArenaPickupRuntime.IsLiveActor(player) || player.MovementContext?.IsGrounded != true
            || !NavMesh.SamplePosition(player.Position, out var nav, .75f, NavMesh.AllAreas)
            || Math.Abs(nav.position.y - player.Position.y) > .5f) return false;
        anchor = nav.position;
        return true;
    }

    internal static bool TryPlace(Vector3 requested, Vector3 anchor, out Vector3 floor, out string reason)
    {
        floor = default;
        reason = "no nearby floor on the navigation mesh";
        if (!NavMesh.SamplePosition(requested, out var nav, .75f, NavMesh.AllAreas)
            || Math.Abs(nav.position.y - requested.y) > .5f) return false;
        var origin = nav.position + Vector3.up * .65f;
        RaycastHit? support = null;
        var hits = Physics.RaycastAll(origin, Vector3.down, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (Ignored(hit.collider)) continue;
            support = hit; break;
        }
        if (!support.HasValue || support.Value.normal.y < .7f || Math.Abs(support.Value.point.y - nav.position.y) > .3f) return false;
        floor = support.Value.point;
        var area = ArenaAreaRuntime.CurrentArea;
        if (area != null && !area.Bounds.Contains(floor.x, floor.y, floor.z, .5f)) { reason = "outside the active area"; return false; }
        foreach (var collider in Physics.OverlapCapsule(floor + Vector3.up * .4f, floor + Vector3.up * 1.1f,
            .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!Ignored(collider)) { reason = "pedestal clearance obstructed"; return false; }
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(anchor, nav.position, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete)
        { reason = "no complete native path from the local actor"; return false; }
        if (area != null && !area.Bounds.ContainsPath(new ArenaAreaPoint(anchor.x, anchor.y, anchor.z),
            path.corners, true, p => new ArenaAreaPoint(p.x, p.y, p.z), .1f))
        { reason = "path leaves the active area"; return false; }
        reason = null; return true;
    }

    private static bool Ignored(Collider collider) => collider == null || collider.GetComponentInParent<Player>() != null
        || ArenaPickupRuntime.OwnsCollider(collider);

    internal static bool LineClear(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < .01f) return true;
        foreach (var hit in Physics.RaycastAll(from, direction / distance, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!Ignored(hit.collider)) return false;
        return true;
    }
}
