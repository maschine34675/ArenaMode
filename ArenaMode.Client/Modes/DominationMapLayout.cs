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
