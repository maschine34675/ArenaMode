using ArenaMode.Areas;
using ArenaMode.Pickups;
using ArenaMode.Shared;
using EFT;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace ArenaMode.Combat.Translocator;

internal static class ArenaTranslocatorGeometry
{
    private static readonly RaycastHit[] Hits = new RaycastHit[64];
    private static readonly Collider[] Overlaps = new Collider[64];
    private static readonly ArenaAreaPoint[] Segment = new ArenaAreaPoint[1];
    internal static Vector3 Unity(NVector p) => new Vector3(p.X, p.Y, p.Z);
    internal static NVector Numerics(Vector3 p) => new NVector(p.x, p.y, p.z);

    internal static bool Inside(Vector3 from, Vector3 to)
    {
        if (!ArenaTranslocatorDisc.Finite(Numerics(from)) || !ArenaTranslocatorDisc.Finite(Numerics(to))) return false;
        if (ArenaAreaRuntime.IsPending) return false;
        var area = ArenaAreaRuntime.CurrentArea;
        if (area == null) return true;
        if (!ArenaAreaRuntime.IsReady) return false;
        Segment[0] = new ArenaAreaPoint(to.x, to.y, to.z);
        return area.Bounds.ContainsPath(new ArenaAreaPoint(from.x, from.y, from.z), Segment,
            true, ArenaTranslocatorDisc.Radius + ArenaTranslocatorDisc.Skin);
    }

    private static bool Ignore(Collider collider, Player owner)
        => collider == null || !collider.enabled || collider.isTrigger
            || ReferenceEquals(collider.GetComponentInParent<Player>(), owner)
            || ArenaPickupRuntime.OwnsCollider(collider);

    internal static bool Empty(Vector3 center, Player owner)
    {
        int count = Physics.OverlapSphereNonAlloc(center, ArenaTranslocatorDisc.Radius,
            Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count >= Overlaps.Length) return false;
        for (int i = 0; i < count; i++) if (!Ignore(Overlaps[i], owner)) return false;
        return true;
    }

    internal static ArenaTranslocatorDisc.Trace Sweep(NVector from, NVector to, Player owner)
    {
        var a = Unity(from); var b = Unity(to);
        if (!Empty(a, owner)) return new ArenaTranslocatorDisc.Trace(ArenaTranslocatorDisc.TraceKind.Invalid, from);
        var delta = b - a; float distance = delta.magnitude;
        if (distance < .000001f)
            return new ArenaTranslocatorDisc.Trace(ArenaTranslocatorDisc.TraceKind.Clear, to);
        int count = Physics.SphereCastNonAlloc(a, ArenaTranslocatorDisc.Radius,
            delta / distance, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count >= Hits.Length) return new ArenaTranslocatorDisc.Trace(ArenaTranslocatorDisc.TraceKind.Invalid, from);
        int closest = -1; float nearest = distance + .01f;
        for (int i = 0; i < count; i++)
            if (!Ignore(Hits[i].collider, owner) && Hits[i].distance >= 0f && Hits[i].distance < nearest)
            { closest = i; nearest = Hits[i].distance; }
        if (closest < 0)
            return new ArenaTranslocatorDisc.Trace(ArenaTranslocatorDisc.TraceKind.Clear, to);
        return new ArenaTranslocatorDisc.Trace(ArenaTranslocatorDisc.TraceKind.Hit,
            Numerics(a + delta / distance * nearest), Numerics(Hits[closest].normal));
    }

    internal static bool Supported(NVector point, Player owner)
    {
        var hit = Sweep(point, point - new NVector(0, .035f, 0), owner);
        return hit.Kind == ArenaTranslocatorDisc.TraceKind.Hit && hit.Normal.Y >= .65f;
    }

    internal static bool TryLaunch(Player player, out ArenaTranslocatorDisc disc)
    {
        disc = null;
        if (player.CameraPosition == null) return false;
        var eye = player.CameraPosition.position;
        var direction = player.LookDirection.normalized;
        if (!ArenaTranslocatorDisc.Finite(Numerics(direction)) || direction.sqrMagnitude < .9f) return false;
        var position = eye + direction * .22f;
        if (!Inside(player.Position, eye) || !Inside(eye, position)) return false;
        var trace = Sweep(Numerics(eye), Numerics(position), player);
        if (trace.Kind != ArenaTranslocatorDisc.TraceKind.Clear || !Empty(position, player)) return false;
        disc = new ArenaTranslocatorDisc(Numerics(position), Numerics(direction * ArenaTranslocatorDisc.LaunchSpeed + Vector3.up * 2f));
        return true;
    }
}
