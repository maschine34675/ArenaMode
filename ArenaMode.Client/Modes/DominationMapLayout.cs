using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Fixed objective anchors. Port coordinates come from individual Arena
/// CapturePointEntity transforms, not the legacy modes.json light centroid:
/// that centroid incorrectly merges AirPit's and Bowl's two separate points.
/// Every anchor still needs the current scene's floor and navigation proof.
/// </summary>
internal sealed class DominationMapLayout
{
    internal readonly struct Point
    {
        internal Point(string id, float x, float y, float z)
        { Id = id; Position = new Vector3(x, y, z); }
        internal string Id { get; }
        internal Vector3 Position { get; }
    }

    private DominationMapLayout(bool factory, params Point[] points)
    { Factory = factory; Points = Array.AsReadOnly(points); }

    internal bool Factory { get; }
    internal IReadOnlyList<Point> Points { get; }

    internal const float FactoryMinimumSeparation = 12f;

    // Inputs are final floors already proved against the loaded scene. Enumerate
    // the small authored pool instead of accepting the first random greedy triple.
    internal static Vector3[] SelectFactory(IReadOnlyList<Vector3> floors, System.Random random, IReadOnlyList<Vector3> previous)
    {
        if (floors == null || floors.Count > 8 || random == null)
            throw new InvalidOperationException("Invalid Factory objective candidate pool.");
        var choices = new List<Vector3[]>();
        for (int a = 0; a < floors.Count; a++)
        for (int b = a + 1; b < floors.Count; b++)
        for (int c = b + 1; c < floors.Count; c++)
            if (Separated(floors[a], floors[b]) && Separated(floors[a], floors[c]) && Separated(floors[b], floors[c]))
                choices.Add(new[] { floors[a], floors[b], floors[c] });
        if (choices.Count == 0) throw new InvalidOperationException("No three separated, reachable Factory control points are available.");
        if (choices.Count > 1 && previous != null)
            choices.RemoveAll(choice => SameSet(choice, previous));
        return choices[random.Next(choices.Count)];
    }

    private static bool Separated(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x, z = a.z - b.z;
        return x * x + z * z >= FactoryMinimumSeparation * FactoryMinimumSeparation;
    }

    private static bool SameSet(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var p in a)
        {
            bool found = false;
            foreach (var q in b)
                if (Math.Abs(p.x - q.x) < .01f && Math.Abs(p.y - q.y) < .01f && Math.Abs(p.z - q.z) < .01f)
                { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    private static readonly DominationMapLayout FactoryLayout = new(true,
        new Point("A", -5.89f, .08f, -27.46f), new Point("B", -6.77f, .17f, 10.01f),
        new Point("C", -20.756f, .166f, 22.769f));
    // Original entities: level57/2771 and level88/3348, labelled X.
    private static readonly DominationMapLayout Equator = new(false, new Point("X", 1.28f, .25f, 4.22f));
    private static readonly DominationMapLayout Bay5 = new(false, new Point("X", 91.26f, -4.366f, 29.44f));
    // level28/2484 (A, east), /2507 (B, west): opposite sides of the airliner.
    private static readonly DominationMapLayout AirPit = new(false,
        new Point("A", 18.96f, -.1f, -1.24f), new Point("B", -45.38f, 0f, 3.82f));
    // level48/2580 (A, west), /2603 (B, east): not the centroid under the Mi-26.
    private static readonly DominationMapLayout Bowl = new(false,
        new Point("A", -20.51f, 0f, -37.12f), new Point("B", 16.99f, -.1f, -30.72f));

    internal static DominationMapLayout Resolve(string map) => map?.ToLowerInvariant() switch
    {
        "factory4_day" or "factory4_night" => FactoryLayout,
        "arena_equator" => Equator,
        "arena_bay5" => Bay5,
        "arena_airpit" => AirPit,
        "arena_bowl" => Bowl,
        _ => throw new InvalidOperationException("No fixed Domination layout exists for this map.")
    };
}
