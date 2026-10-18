using ArenaMode.Areas;
using ArenaMode.Shared;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Pickups;

internal static class ArenaPickupGeometry
{
    internal const float RelocationRadius = 2f;

    // Contact scans run every 75 ms for every live actor. RaycastAll allocated
    // a new hit array each call; reuse one buffer on the main thread.
    private const int HitCapacity = 32;
    private static readonly RaycastHit[] Hits = new RaycastHit[HitCapacity];

    internal sealed class PlacementDiagnostics
    {
        internal int Probes;
        private readonly Dictionary<string, int> _refusals = new(StringComparer.Ordinal);
        internal void Refused(string reason)
        {
            reason = reason ?? "unknown";
            _refusals.TryGetValue(reason, out int count);
            _refusals[reason] = count + 1;
        }
        internal void Merge(PlacementDiagnostics other)
        {
            Probes += other.Probes;
            foreach (var pair in other._refusals)
            {
                _refusals.TryGetValue(pair.Key, out int count);
                _refusals[pair.Key] = count + pair.Value;
            }
        }
        public override string ToString() => string.Join(", ", _refusals.OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + "=" + pair.Value));
    }

    // Only unsurveyed built-in prototypes may move. Custom layouts remain exact.
    // Every candidate runs the original floor, clearance, contour and path checks.
    internal static bool TryPlacePoint(Vector3 requested, Vector3 anchor, bool allowNearby,
        out Vector3 floor, out string reason, out bool relocated, PlacementDiagnostics diagnostics = null)
        => TryResolvePoint(requested, anchor, allowNearby, out floor, out reason, out relocated, diagnostics);

    // A null anchor is only used to survey Factory's fixed seed pool. Those floors
    // are not placement authority until their independent native network is proven.
    private static bool TryResolvePoint(Vector3 requested, Vector3? anchor, bool allowNearby,
        out Vector3 floor, out string reason, out bool relocated, PlacementDiagnostics diagnostics = null)
    {
        relocated = false;
        if (diagnostics != null) diagnostics.Probes++;
        if (TryCandidate(requested, anchor, out floor, out reason)) return true;
        diagnostics?.Refused(reason);
        if (!allowNearby) return false;
        string originalReason = reason;
        for (int ring = 1; ring <= 4; ring++)
        for (int direction = 0; direction < 8; direction++)
        {
            double angle = direction * Math.PI / 4d;
            float radius = ring * .5f;
            var candidate = requested + new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);
            if (diagnostics != null) diagnostics.Probes++;
            if (!TryCandidate(candidate, anchor, out var alternative, out var refusal))
            { diagnostics?.Refused(refusal); continue; }
            var delta = alternative - requested;
            // NavMesh sampling may move an additional .75m; bound the FINAL floor,
            // not just the candidate, and never relocate onto another level.
            if (delta.x * delta.x + delta.z * delta.z > RelocationRadius * RelocationRadius + .0001f
                || Math.Abs(delta.y) > .5f)
            { diagnostics?.Refused("relocation exceeds distance or level bound"); continue; }
            floor = alternative; reason = originalReason; relocated = true;
            return true;
        }
        floor = default;
        reason = originalReason + "; no safe alternative within 2m (32 candidates)";
        return false;
    }

    internal static float BeamLength(Vector3 floor)
    {
        const float maximum = 6f; // 50% taller; ceilings still clip the visible beam.
        float length = maximum;
        int count = QueryHits(floor + Vector3.up * .15f, Vector3.up, maximum, out var hits);
        for (int i = 0; i < count; i++)
            if (!Ignored(hits[i].collider)) length = Math.Min(length, Math.Max(0f, hits[i].distance - .1f));
        return length;
    }

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
        if (!TryFloor(requested, out floor, out var navigation, out reason)) return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(anchor, navigation, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete)
        { reason = "no complete native path from the placement anchor"; return false; }
        var area = ArenaAreaRuntime.CurrentArea;
        if (area != null && !area.Bounds.ContainsPath(new ArenaAreaPoint(anchor.x, anchor.y, anchor.z),
            path.corners, true, p => new ArenaAreaPoint(p.x, p.y, p.z), .1f))
        { reason = "path leaves the active area"; return false; }
        reason = null; return true;
    }

    private static bool TryCandidate(Vector3 requested, Vector3? anchor, out Vector3 floor, out string reason)
        => anchor.HasValue ? TryPlace(requested, anchor.Value, out floor, out reason)
            : TryFloor(requested, out floor, out _, out reason);

    private static bool TryFloor(Vector3 requested, out Vector3 floor, out Vector3 navigation, out string reason)
    {
        floor = default;
        navigation = default;
        reason = "no nearby floor on the navigation mesh";
        if (!NavMesh.SamplePosition(requested, out var nav, .75f, NavMesh.AllAreas)
            || Math.Abs(nav.position.y - requested.y) > .5f) return false;
        navigation = nav.position;
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
        reason = null; return true;
    }

    internal static bool IsFullFactory(ArenaPickupLayout layout) => layout != null && layout.AreaId == null
        && ArenaAreaRuntime.CurrentArea == null
        && (string.Equals(layout.MapId, "factory4_day", StringComparison.OrdinalIgnoreCase)
            || string.Equals(layout.MapId, "factory4_night", StringComparison.OrdinalIgnoreCase));

    // Factory has legitimate human entries on isolated NavMesh islands. A human
    // can jump out, while using that entry as the map-wide path origin rejects
    // every pickup. Prove the reviewed pickup pool's main network independently.
    // No scene-wide search, self-path authority or persistent navigation cache.
    internal static bool TryFactoryAnchor(ArenaPickupLayout layout, out Vector3 anchor)
    {
        anchor = default;
        if (!IsFullFactory(layout) || layout.Points == null) return false;
        var seeds = layout.Points.SelectMany(p => new[] { new ArenaPickupPosition { X = p.X, Y = p.Y, Z = p.Z } }
                .Concat(p.Alternatives ?? new List<ArenaPickupPosition>()))
            .GroupBy(p => (p.X, p.Y, p.Z)).Select(g => g.First())
            .OrderBy(p => p.X).ThenBy(p => p.Z).ThenBy(p => p.Y).Take(9).ToArray();
        // This policy belongs only to the existing eight-position Factory pool.
        if (seeds.Length > 8) return false;
        var nodes = new List<Vector3>();
        foreach (var seed in seeds)
        {
            if (!TryResolvePoint(new Vector3(seed.X, seed.Y, seed.Z), null, true,
                out var floor, out _, out _, null)
                || !NavMesh.SamplePosition(floor, out var nav, .75f, NavMesh.AllAreas)
                || Math.Abs(nav.position.y - floor.y) > .5f) continue;
            if (nodes.Any(p => (p.x - nav.position.x) * (p.x - nav.position.x)
                + (p.z - nav.position.z) * (p.z - nav.position.z) < 9f)) continue;
            nodes.Add(nav.position);
        }
        if (nodes.Count < 3) return false;
        var connections = new int[nodes.Count];
        var path = new NavMeshPath();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            if (!NavMesh.CalculatePath(nodes[i], nodes[j], NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete
                || !NavMesh.CalculatePath(nodes[j], nodes[i], NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete) continue;
            connections[i]++; connections[j]++;
        }
        int best = 0;
        for (int i = 1; i < nodes.Count; i++)
            if (connections[i] > connections[best]) best = i;
        int members = connections[best] + 1;
        if (members < 3 || members * 2 <= nodes.Count) return false;
        anchor = nodes[best];
        return true;
    }

    private static bool Ignored(Collider collider) => collider == null || collider.GetComponentInParent<Player>() != null
        || ArenaPickupRuntime.OwnsCollider(collider);

    internal static bool LineClear(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < .01f) return true;
        int count = QueryHits(from, direction / distance, distance, out var hits);
        for (int i = 0; i < count; i++)
            if (!Ignored(hits[i].collider)) return false;
        return true;
    }

    private static int QueryHits(Vector3 from, Vector3 direction, float distance, out RaycastHit[] hits)
    {
        int count = Physics.RaycastNonAlloc(from, direction, Hits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count < HitCapacity)
        {
            hits = Hits;
            return count;
        }
        hits = Physics.RaycastAll(from, direction, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return hits.Length;
    }
}
