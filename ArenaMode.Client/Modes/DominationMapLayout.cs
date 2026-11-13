using System;
using System.Collections.Generic;
using ArenaMode.Shared;
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
        { Id = id; Position = new Vector3(x, y, z); Alternates = Array.Empty<Vector3>(); }
        // A slot of a surveyed layout: the primary seed plus its ordered fallbacks.
        internal Point(string id, Vector3 primary, params Vector3[] alternates)
        { Id = id; Position = primary; Alternates = Array.AsReadOnly(alternates ?? Array.Empty<Vector3>()); }
        internal string Id { get; }
        internal Vector3 Position { get; }
        /// <summary>Ordered fallbacks of a surveyed slot; empty for an exact point.</summary>
        internal IReadOnlyList<Vector3> Alternates { get; }
        /// <summary>The primary seed first, then the alternates in order.</summary>
        internal IEnumerable<Vector3> Candidates
        {
            get
            {
                yield return Position;
                if (Alternates == null) yield break;
                foreach (var alternate in Alternates) yield return alternate;
            }
        }
    }

    private DominationMapLayout(bool factory, params Point[] points)
    { Factory = factory; Points = Array.AsReadOnly(points); NetworkSeeds = Array.Empty<Vector3>(); }

    // The third kind: surveyed slots. Each slot is a primary seed with ordered alternates; the layout
    // may carry extra seeds that only join the navigation-network proof and are never points. Internal
    // (not private) so a harness can build a synthetic layout, for example one inside a bounded area.
    internal DominationMapLayout(IReadOnlyList<Vector3> extraNetworkSeeds, params Point[] slots)
    {
        Surveyed = true;
        Points = Array.AsReadOnly(slots);
        NetworkSeeds = extraNetworkSeeds ?? Array.Empty<Vector3>();
    }

    internal bool Factory { get; }
    /// <summary>
    /// Fixed slots, each with ordered alternates, proved against the loaded scene at match start
    /// (see DominationRuntime.Prepare): neither Factory's random draw nor the ports' exact points.
    /// Not Factory, so every Factory-keyed line keeps its meaning.
    /// </summary>
    internal bool Surveyed { get; }
    internal IReadOnlyList<Point> Points { get; }
    /// <summary>Surveyed layouts only: extra walkable seeds for the network proof, never points.</summary>
    internal IReadOnlyList<Vector3> NetworkSeeds { get; }

    internal const float FactoryMinimumSeparation = 12f;
    internal const float SurveyedMinimumSeparation = 15f;

    /// <summary>
    /// Every position of the navigation-network proof of a surveyed layout: all slot candidates,
    /// then the extra seeds. Empty for the other kinds.
    /// </summary>
    internal IReadOnlyList<Vector3> NavigationSeeds
    {
        get
        {
            if (!Surveyed) return Array.Empty<Vector3>();
            var seeds = new List<Vector3>();
            foreach (var slot in Points) seeds.AddRange(slot.Candidates);
            seeds.AddRange(NetworkSeeds);
            return seeds;
        }
    }

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
    // Iceberg level376 (Arena_Sanatorium_CheckPoint_DesignStuff): the CheckPoint
    // CAPTURE_POINT/CAPTURE_POINT_LIGHT_02_A/B/C roots from iceberg.modes.json, the
    // same per-light source whose roots equal the CapturePointEntity anchors on the
    // other ports; the sidecar's centroid is null and would be meaningless. X/Z are the
    // light roots; the heights are the Arena NavMesh floors measured on GPU-2 on
    // 31.10.2026 with UiBridge teleport dry runs (snap 0.0 m at these values). The roots'
    // own heights (A 3.9 in the pool basin, B 2.69, C 2.85) lie .6/.44/.3 m below those
    // floors, and the unchanged native projection (SamplePosition .75 m, |dy| <= .5,
    // support ray, clearance) refused A on the host ("no nearby floor on the navigation
    // mesh"), which aborted the protected match. Do not move X/Z, add a relocation or
    // loosen ArenaPickupGeometry.TryFloor: a refused point must keep aborting the match.
    private static readonly DominationMapLayout Iceberg = new(false,
        new Point("A", 16.77f, 4.5f, -91.39f), new Point("B", 10.614f, 3.13f, -77.727f),
        new Point("C", 21.91f, 3.15f, -47.3f));
    // Chop Shop (arena_autoservice): one point "X", the CAPTURE_POINT/BOWL_CAPTURE_POINT_LIGHT_03
    // light root of autoservice.modes.json (a Bowl leftover name, but the only capture point
    // the scene carries; the same per-light source as the other ports). X/Z and the height
    // are all the root's own: GPU-2 on 12.11.2026 (ordinary PMC raid, UiBridge teleport dry
    // runs; docs/reports/2026-11/GPU-CHOP-SHOP-20261112.md) put the Arena NavMesh floor at
    // y -3.8 for this X/Z, accepted the exact point with a snap below 0.05 m and room to
    // stand, and found complete paths from both pockets (42.2 / 37.1 m). Unlike Iceberg
    // (a6ed8d93) nothing is substituted. The native Domination run the same day verified the
    // point at (-369.290, -3.805, 499.490) with the gates held and the routes from both
    // pockets. If a later map build refuses it, change ChopShopControlPointY below and
    // nothing else. Do not move X/Z, add a relocation or loosen ArenaPickupGeometry.TryFloor:
    // a refused point must keep aborting the protected match.
    private const float ChopShopControlPointY = -3.8f;
    private static readonly DominationMapLayout ChopShop = new(false,
        new Point("X", -369.29f, ChopShopControlPointY, 499.49f));

    // The Lab (laboratory), floor 1, the main hall level. Seeds, not authority: measured natively on
    // GPU-2 on 13.11.2026 (installed master build 0.5.0+c64846fb, Team Deathmatch raids, UiBridge
    // teleport dry runs: NavMesh sample, room to stand, complete walkable path), y is the NavMesh
    // height the dry run reported. DominationRuntime.Prepare proves every point again against the
    // loaded scene (floor, clearance, complete paths both ways from the layout's own network anchor)
    // and takes the first passing candidate of each slot at least 15 m from the earlier slots; a slot
    // without one aborts the match. Every point also had a complete path from floor 2 and to the
    // basement. Path lengths B-A 68.0 m, B-C 95.7 m, A-C 110.9 m.
    //   A north-west atrium: native row cdbfeb06. B centre, loading hall: Coop row 9896b358
    //   (measured floor 0.0). C south: row 530af38b.
    private static readonly DominationMapLayout Laboratory = new(Array.Empty<Vector3>(),
        new Point("A", new Vector3(-224.879f, .082f, -279.867f), new Vector3(-231.636f, .082f, -286.694f)),
        new Point("B", new Vector3(-171.54f, 0f, -312.18f), new Vector3(-175.02f, .1f, -311.28f),
            new Vector3(-166.02f, .1f, -314.36f)),
        new Point("C", new Vector3(-213.124f, 0f, -378.755f), new Vector3(-205.29f, .1f, -390.55f),
            new Vector3(-202.231f, .1f, -393.884f)));
    // The Labyrinth (labyrinth), single level, ONE point (user decision 2026-11-13). Measured the same
    // way and on the same day as The Lab above. X centre: row b99646de. The three further walkable rows
    // are seeds for the network proof only and never points. Paths from the centre: 12 to 39 m to the
    // other rows, 73 to 85 m to four of the five entrances; the fifth entrance (-37.4, 0.5, 73.8) is a
    // separate NavMesh region and is deliberately not seeded.
    private static readonly DominationMapLayout Labyrinth = new(
        new[] { new Vector3(-23.83f, .1f, 20.08f), new Vector3(-10.91f, .1f, 6.08f), new Vector3(15.23f, .1f, 27.16f) },
        new Point("X", new Vector3(-9.92f, .1f, 23.92f), new Vector3(5.92f, .1f, 20.22f),
            new Vector3(-11.18f, .2f, 35.7f)));

    /// <summary>
    /// Domination's own resolution, by (map, area): the whole map (<paramref name="areaId"/> null,
    /// empty or <see cref="WholeMapAreaId"/>) of Factory, the six ports, The Lab and The Labyrinth.
    /// <see cref="Resolve"/> and <see cref="ControlPointCount"/> keep their meaning and map list
    /// because Tactical's capture point reads them: Tactical gets no point on The Lab, The
    /// Labyrinth or Customs (docs/reviews/TACTICAL-OBJECTIVE-20261108.md).
    /// </summary>
    internal static DominationMapLayout ResolveDomination(string map, string areaId)
    {
        if (IsWholeMap(areaId))
        {
            if (string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase)) return Laboratory;
            if (string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase)) return Labyrinth;
            return Resolve(map);
        }
        // STAGE 3 (Customs areas): the three area rows are resolved here by (map, areaId).
        throw new InvalidOperationException("No fixed Domination layout exists for this map and area.");
    }

    /// <summary>
    /// The menu's Domination objective count: 0 for null or any (map, area) without a layout, which
    /// is never passed to <see cref="ResolveDomination"/>.
    /// </summary>
    internal static int DominationPointCount(string map, string areaId)
    {
        if (map == null) return 0;
        if (IsWholeMap(areaId))
        {
            if (string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase))
                return ResolveDomination(map, areaId).Points.Count;
            return ControlPointCount(map);
        }
        // STAGE 3 (Customs areas): the area counts are answered here.
        return 0;
    }

    /// <summary>ArenaAreaCatalog.FullMapId, repeated because harnesses link this file without that catalogue.</summary>
    internal const string WholeMapAreaId = "full-map";

    private static bool IsWholeMap(string areaId)
        => string.IsNullOrWhiteSpace(areaId) || string.Equals(areaId, WholeMapAreaId, StringComparison.Ordinal);

    internal static DominationMapLayout Resolve(string map) => map?.ToLowerInvariant() switch
    {
        "factory4_day" or "factory4_night" => FactoryLayout,
        "arena_equator" => Equator,
        "arena_bay5" => Bay5,
        "arena_airpit" => AirPit,
        "arena_bowl" => Bowl,
        "arena_iceberg" => Iceberg,
        "arena_autoservice" => ChopShop,
        _ => throw new InvalidOperationException("No fixed Domination layout exists for this map.")
    };

    /// <summary>
    /// Objective count for the mode menu: 0 for null or any map without a fixed
    /// layout, which is never passed to <see cref="Resolve"/>.
    /// </summary>
    internal static int ControlPointCount(string map)
    {
        // No exception-driven polling before a supported map has been selected.
        if (map == null || !(ArenaMapCatalog.IsFactoryVariant(map)
            || ArenaMapCatalog.IsEquator(map) || ArenaMapCatalog.IsBay5(map)
            || ArenaMapCatalog.IsAirPit(map) || ArenaMapCatalog.IsBowl(map)
            || ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map))) return 0;
        return Resolve(map).Points.Count;
    }
}
