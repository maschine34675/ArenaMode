using ArenaMode.Areas;
using ArenaMode.Pickups;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Combat.Fire;

internal static class ArenaFireGeometry
{
    private static readonly RaycastHit[] Hits = new RaycastHit[48];
    private static bool Ignore(Collider c) => c == null || c.GetComponentInParent<EFT.Player>() != null
        || c.GetComponentInParent<EFT.Grenade>() != null || ArenaPickupRuntime.OwnsCollider(c);

    internal static bool Floor(Vector3 requested, float up, float down, out Vector3 point)
    {
        point = default;
        int count = Physics.RaycastNonAlloc(requested + Vector3.up * up, Vector3.down, Hits, up + down,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == Hits.Length) return false; // Never guess through saturated geometry.
        float nearest = float.MaxValue;
        int best = -1;
        for (int i = 0; i < count; i++)
            if (!Ignore(Hits[i].collider) && Hits[i].distance < nearest) { nearest = Hits[i].distance; best = i; }
        if (best < 0 || Hits[best].normal.y < .7f) return false;
        point = Hits[best].point;
        var area = ArenaAreaRuntime.CurrentArea;
        return area == null || area.Bounds.Contains(point.x, point.y, point.z, .1f);
    }

    internal static bool Clear(Vector3 from, Vector3 to)
    {
        var delta = to - from; float distance = delta.magnitude;
        if (distance < .01f) return true;
        int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == Hits.Length) return false;
        for (int i = 0; i < count; i++) if (!Ignore(Hits[i].collider)) return false;
        return true;
    }

    internal static List<Vector3> Build(Vector3 impact)
    {
        var cells = new List<Vector3>(29);
        if (!Floor(impact, .15f, 1.3f, out var origin)) return cells;
        cells.Add(origin);
        // Radial sample with a ground trace AND an unobstructed low connection
        // to the original floor. It cannot propagate through a wall or down stairs.
        for (int ring = 1; ring <= 3; ring++)
        for (int i = 0; i < ring * 8; i++)
        {
            float angle = i * Mathf.PI * 2f / (ring * 8);
            float radius = ring * .78f;
            var requested = origin + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            if (!Floor(requested, .35f, .35f, out var floor) || Math.Abs(floor.y - origin.y) > .3f
                || !Clear(origin + Vector3.up * .18f, floor + Vector3.up * .18f)) continue;
            cells.Add(floor);
        }
        return cells;
    }
    internal static bool Contains(IReadOnlyList<Vector3> cells, Vector3 feet, float padding = 0f)
    {
        foreach (var cell in cells)
        {
            var d = feet - cell;
            if (ArenaFirePolicy.Contact(d.x, d.y, d.z, padding)
                && Clear(cell + Vector3.up * .3f, feet + Vector3.up * .3f)) return true;
        }
        return false;
    }
}
