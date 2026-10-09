#nullable enable
using System;
using System.Collections.Generic;

namespace ArenaMode.Shared;

/// <summary>Immutable, versioned geometry shared by the client and exact server raid.</summary>
public sealed class ArenaAreaDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string MapId { get; }
    public ArenaAreaBounds Bounds { get; }
    public float SpawnMinY { get; }
    public float SpawnMaxY { get; }
    public int MaxTeamSize { get; }
    public ArenaTeamStarts? TeamStarts { get; }
    private readonly HashSet<string> _excludedSpawnIds;

    public ArenaAreaDefinition(string id, string displayName, string mapId,
        ArenaAreaBounds bounds, float spawnMinY, float spawnMaxY, int maxTeamSize,
        IEnumerable<string>? excludedSpawnIds = null, ArenaTeamStarts? teamStarts = null)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(mapId)
            || !ArenaAreaBounds.Finite(spawnMinY) || !ArenaAreaBounds.Finite(spawnMaxY)
            || spawnMinY > spawnMaxY || spawnMinY < bounds.MinY || spawnMaxY > bounds.MaxY
            || !bounds.IsValid
            || maxTeamSize < 2)
            throw new ArgumentException("Invalid Arena area definition.");
        Id = id;
        DisplayName = displayName;
        MapId = mapId;
        Bounds = bounds;
        SpawnMinY = spawnMinY;
        SpawnMaxY = spawnMaxY;
        MaxTeamSize = maxTeamSize;
        TeamStarts = teamStarts;
        // Never retain a caller-owned mutable collection. Identities are exact:
        // an exclusion must not broaden into a zone, category or height filter.
        _excludedSpawnIds = new HashSet<string>(StringComparer.Ordinal);
        if (excludedSpawnIds != null)
            foreach (string spawnId in excludedSpawnIds)
            {
                if (string.IsNullOrWhiteSpace(spawnId))
                    throw new ArgumentException("Excluded spawn identities must not be empty.", nameof(excludedSpawnIds));
                _excludedSpawnIds.Add(spawnId);
            }
    }

    public bool IsExcludedSpawn(string? spawnId)
        => spawnId != null && _excludedSpawnIds.Contains(spawnId);

    // Legacy foot-point bands remain separate from enclosing volumes. A
    // height-unlimited contour applies only horizontal clearance and exact IDs.
    public bool AllowsSpawn(float x, float y, float z, string? spawnId = null)
        => Bounds.Contains(x, y, z, ArenaAreaCatalog.SpawnInset)
           && (!Bounds.LimitsHeight || y >= SpawnMinY && y <= SpawnMaxY)
           && !IsExcludedSpawn(spawnId);
}

/// <summary>
/// A finite simple XZ polygon with optional vertical containment. Its extents are an
/// acceleration bound, not the playable shape. The original box constructor
/// retains its inclusive float comparisons and convex path fast path.
/// </summary>
public readonly struct ArenaAreaBounds
{
    public const int MaxVertices = 64;
    public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
    /// <summary>False means MinY/MaxY are construction hints, never actor/spawn limits.</summary>
    public readonly bool LimitsHeight;
    private readonly IReadOnlyList<ArenaAreaVertex>? _outline;
    private readonly IReadOnlyList<int>? _floorTriangles;
    private readonly bool _rectangle;

    /// <summary>Defensive copy, counterclockwise in the X/Z plane.</summary>
    public IReadOnlyList<ArenaAreaVertex> Outline => _outline ?? Array.Empty<ArenaAreaVertex>();
    /// <summary>CCW index triples into Outline; collinear vertices may be unused.</summary>
    public IReadOnlyList<int> FloorTriangles => _floorTriangles ?? Array.Empty<int>();
    public bool IsValid => _outline != null && _floorTriangles != null;

    public ArenaAreaBounds(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        : this(BoxOutline(minX, minZ, maxX, maxZ), minY, maxY)
    {
        _rectangle = true;
    }

    public ArenaAreaBounds(IReadOnlyList<ArenaAreaVertex> outline, float minY, float maxY,
        bool limitsHeight = true)
    {
        if (outline == null || outline.Count < 3 || outline.Count > MaxVertices
            || !Finite(minY) || !Finite(maxY) || minY >= maxY)
            throw new ArgumentException("Arena polygon requires 3..64 vertices and finite ordered heights.");

        var copy = new ArenaAreaVertex[outline.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = outline[i];
            if (!Finite(copy[i].X) || !Finite(copy[i].Z))
                throw new ArgumentException("Arena polygon coordinates must be finite.");
            for (int j = 0; j < i; j++)
                if (copy[i].X == copy[j].X && copy[i].Z == copy[j].Z)
                    throw new ArgumentException("Arena polygon vertices must be distinct; do not repeat its closing point.");
        }
        ValidateSimple(copy);
        double area = SignedDoubleArea(copy);
        if (area == 0)
            throw new ArgumentException("Arena polygon has no enclosed area.");
        if (area < 0) Array.Reverse(copy);
        var triangles = Triangulate(copy);

        float minX = copy[0].X, maxX = copy[0].X, minZ = copy[0].Z, maxZ = copy[0].Z;
        for (int i = 1; i < copy.Length; i++)
        {
            minX = Math.Min(minX, copy[i].X); maxX = Math.Max(maxX, copy[i].X);
            minZ = Math.Min(minZ, copy[i].Z); maxZ = Math.Max(maxZ, copy[i].Z);
        }
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        LimitsHeight = limitsHeight;
        _outline = Array.AsReadOnly(copy);
        _floorTriangles = Array.AsReadOnly(triangles);
        _rectangle = false;
    }

    /// <summary>Boundary belongs to the area at zero inset; positive inset is Euclidean edge clearance.</summary>
    public bool Contains(float x, float y, float z, float horizontalInset = 0f)
    {
        if (!IsValid || !Finite(x) || !Finite(y) || !Finite(z) || !Finite(horizontalInset)
            || horizontalInset < 0 || LimitsHeight && (y < MinY || y > MaxY)) return false;
        if (_rectangle)
            return x >= MinX + horizontalInset && x <= MaxX - horizontalInset
                && z >= MinZ + horizontalInset && z <= MaxZ - horizontalInset;
        if (x < MinX || x > MaxX || z < MinZ || z > MaxZ || !ContainsXZ(x, z)) return false;
        if (horizontalInset == 0) return true;
        double requiredSquared = (double)horizontalInset * horizontalInset;
        for (int i = 0; i < Outline.Count; i++)
        {
            var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
            if (PointSegmentDistanceSquared(x, z, a.X, a.Z, b.X, b.Z) < requiredSquared) return false;
        }
        return true;
    }

    public bool ContainsPath(ArenaAreaPoint origin, IReadOnlyList<ArenaAreaPoint> path,
        bool complete, float horizontalInset = 0f)
        => ContainsPath(origin, path, complete, point => point, horizontalInset);

    public bool ContainsPath<T>(ArenaAreaPoint origin, IReadOnlyList<T>? path,
        bool complete, Func<T, ArenaAreaPoint> position, float horizontalInset = 0f)
    {
        if (!complete || path == null || path.Count == 0 || position == null
            || !Contains(origin.X, origin.Y, origin.Z, horizontalInset)) return false;
        for (int i = 0; i < path.Count; i++)
        {
            var point = position(path[i]);
            if (!Contains(point.X, point.Y, point.Z, horizontalInset)
                || (!_rectangle && !ContainsSegment(origin, point, horizontalInset))) return false;
            origin = point;
        }
        return true;
    }

    private bool ContainsSegment(ArenaAreaPoint from, ArenaAreaPoint to, float inset)
    {
        // Endpoints were checked by Contains. Both an optional height interval
        // and unlimited height are convex, so only the horizontal segment needs more work.
        if (from.X == to.X && from.Z == to.Z) return true;
        if (inset > 0)
        {
            double requiredSquared = (double)inset * inset;
            for (int i = 0; i < Outline.Count; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
                if (SegmentsTouch(from.X, from.Z, to.X, to.Z, a.X, a.Z, b.X, b.Z)
                    || PointSegmentDistanceSquared(from.X, from.Z, a.X, a.Z, b.X, b.Z) < requiredSquared
                    || PointSegmentDistanceSquared(to.X, to.Z, a.X, a.Z, b.X, b.Z) < requiredSquared
                    || PointSegmentDistanceSquared(a.X, a.Z, from.X, from.Z, to.X, to.Z) < requiredSquared
                    || PointSegmentDistanceSquared(b.X, b.Z, from.X, from.Z, to.X, to.Z) < requiredSquared)
                    return false;
            }
            return true;
        }

        // At zero inset tangencies and travel along the boundary are legal.
        // Split at every actual segment/edge intersection, including collinear
        // endpoints. Interior membership cannot change inside any resulting
        // open interval. One test per interval is therefore exact subdivision,
        // not fixed-spacing sampling that could jump across a narrow notch.
        var cuts = new List<double>(Outline.Count + 2) { 0, 1 };
        double rx = (double)to.X - from.X, rz = (double)to.Z - from.Z;
        double lengthSquared = rx * rx + rz * rz;
        for (int i = 0; i < Outline.Count; i++)
        {
            var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
            // Any strict crossing already leaves the polygon. Reject it before
            // computing parameters, even if an extremely short outside interval
            // would round to the same parameter at the far end of a long path.
            if (Opposite(Cross(from.X, from.Z, to.X, to.Z, a.X, a.Z), Cross(from.X, from.Z, to.X, to.Z, b.X, b.Z))
                && Opposite(Cross(a.X, a.Z, b.X, b.Z, from.X, from.Z), Cross(a.X, a.Z, b.X, b.Z, to.X, to.Z)))
                return false;
            double sx = (double)b.X - a.X, sz = (double)b.Z - a.Z;
            double qx = (double)a.X - from.X, qz = (double)a.Z - from.Z;
            double denominator = rx * sz - rz * sx;
            if (denominator != 0)
            {
                double t = (qx * sz - qz * sx) / denominator;
                double u = (qx * rz - qz * rx) / denominator;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) cuts.Add(t);
            }
            else if (qx * rz - qz * rx == 0)
            {
                double t0 = (qx * rx + qz * rz) / lengthSquared;
                double t1 = (((double)b.X - from.X) * rx + ((double)b.Z - from.Z) * rz) / lengthSquared;
                if (t0 > 0 && t0 < 1) cuts.Add(t0);
                if (t1 > 0 && t1 < 1) cuts.Add(t1);
            }
        }
        cuts.Sort();
        for (int i = 1; i < cuts.Count; i++)
        {
            if (cuts[i] <= cuts[i - 1]) continue;
            double t = cuts[i - 1] + (cuts[i] - cuts[i - 1]) * 0.5;
            if (!ContainsXZ(from.X + t * rx, from.Z + t * rz)) return false;
        }
        return true;
    }

    private bool ContainsXZ(double x, double z)
    {
        int winding = 0;
        for (int i = 0; i < Outline.Count; i++)
        {
            var a = Outline[i]; var b = Outline[(i + 1) % Outline.Count];
            double cross = Cross(a.X, a.Z, b.X, b.Z, x, z);
            if (cross == 0 && Between(x, z, a.X, a.Z, b.X, b.Z)) return true;
            if (a.Z <= z && b.Z > z && cross > 0) winding++;
            else if (a.Z > z && b.Z <= z && cross < 0) winding--;
        }
        return winding != 0;
    }

    private static ArenaAreaVertex[] BoxOutline(float minX, float minZ, float maxX, float maxZ)
    {
        if (!Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ) || minX >= maxX || minZ >= maxZ)
            throw new ArgumentException("Arena area bounds must be finite and ordered.");
        return new[] { new ArenaAreaVertex(minX, minZ), new ArenaAreaVertex(maxX, minZ),
            new ArenaAreaVertex(maxX, maxZ), new ArenaAreaVertex(minX, maxZ) };
    }

    private static void ValidateSimple(ArenaAreaVertex[] vertices)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            var a = vertices[(i + vertices.Length - 1) % vertices.Length];
            var b = vertices[i]; var c = vertices[(i + 1) % vertices.Length];
            if (Cross(a.X, a.Z, b.X, b.Z, c.X, c.Z) == 0
                && ((double)b.X - a.X) * ((double)c.X - b.X) + ((double)b.Z - a.Z) * ((double)c.Z - b.Z) <= 0)
                throw new ArgumentException("Arena polygon adjacent edges overlap or reverse direction.");
            for (int j = i + 1; j < vertices.Length; j++)
            {
                if (j == i + 1 || (i == 0 && j == vertices.Length - 1)) continue;
                var d = vertices[j]; var e = vertices[(j + 1) % vertices.Length];
                if (SegmentsTouch(b.X, b.Z, c.X, c.Z, d.X, d.Z, e.X, e.Z))
                    throw new ArgumentException("Arena polygon edges must not cross or touch non-adjacent edges.");
            }
        }
    }

    private static double SignedDoubleArea(ArenaAreaVertex[] vertices)
    {
        // Translate before summing to avoid cancellation from a large map origin.
        double area = 0;
        for (int i = 1; i < vertices.Length - 1; i++)
            area += Cross(vertices[0].X, vertices[0].Z, vertices[i].X, vertices[i].Z, vertices[i + 1].X, vertices[i + 1].Z);
        return area;
    }

    private static int[] Triangulate(ArenaAreaVertex[] vertices)
    {
        var remaining = new List<int>(vertices.Length);
        for (int i = 0; i < vertices.Length; i++) remaining.Add(i);
        var triangles = new List<int>((vertices.Length - 2) * 3);
        while (remaining.Count > 3)
        {
            bool removed = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                int current = remaining[i], next = remaining[(i + 1) % remaining.Count];
                var a = vertices[previous]; var b = vertices[current]; var c = vertices[next];
                double cross = Cross(a.X, a.Z, b.X, b.Z, c.X, c.Z);
                if (cross < 0) continue;
                if (cross == 0)
                {
                    // Preserve the immutable original contour but do not emit
                    // zero-area cap triangles for straight intermediate points.
                    remaining.RemoveAt(i); removed = true; break;
                }
                bool occupied = false;
                foreach (int other in remaining)
                {
                    if (other == previous || other == current || other == next) continue;
                    var p = vertices[other];
                    if (Cross(a.X, a.Z, b.X, b.Z, p.X, p.Z) >= 0
                        && Cross(b.X, b.Z, c.X, c.Z, p.X, p.Z) >= 0
                        && Cross(c.X, c.Z, a.X, a.Z, p.X, p.Z) >= 0)
                    { occupied = true; break; }
                }
                if (occupied) continue;
                triangles.Add(previous); triangles.Add(current); triangles.Add(next);
                remaining.RemoveAt(i); removed = true; break;
            }
            if (!removed) throw new ArgumentException("Arena polygon could not be triangulated safely.");
        }
        var first = vertices[remaining[0]]; var second = vertices[remaining[1]]; var third = vertices[remaining[2]];
        if (Cross(first.X, first.Z, second.X, second.Z, third.X, third.Z) <= 0)
            throw new ArgumentException("Arena polygon triangulation has a degenerate final face.");
        triangles.AddRange(remaining);
        return triangles.ToArray();
    }

    private static bool SegmentsTouch(double ax, double az, double bx, double bz,
        double cx, double cz, double dx, double dz)
    {
        double c = Cross(ax, az, bx, bz, cx, cz), d = Cross(ax, az, bx, bz, dx, dz);
        double a = Cross(cx, cz, dx, dz, ax, az), b = Cross(cx, cz, dx, dz, bx, bz);
        return (Opposite(c, d) && Opposite(a, b))
            || (c == 0 && Between(cx, cz, ax, az, bx, bz))
            || (d == 0 && Between(dx, dz, ax, az, bx, bz))
            || (a == 0 && Between(ax, az, cx, cz, dx, dz))
            || (b == 0 && Between(bx, bz, cx, cz, dx, dz));
    }

    private static bool Opposite(double a, double b) => (a < 0 && b > 0) || (a > 0 && b < 0);
    private static bool Between(double x, double z, double ax, double az, double bx, double bz)
        => x >= Math.Min(ax, bx) && x <= Math.Max(ax, bx) && z >= Math.Min(az, bz) && z <= Math.Max(az, bz);
    private static double Cross(double ax, double az, double bx, double bz, double cx, double cz)
        => (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
    private static double PointSegmentDistanceSquared(double x, double z, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, lengthSquared = dx * dx + dz * dz;
        double t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / lengthSquared));
        double ex = x - (ax + t * dx), ez = z - (az + t * dz);
        return ex * ex + ez * ez;
    }

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>One horizontal outline vertex; validation belongs to the complete polygon.</summary>
public readonly struct ArenaAreaVertex
{
    public readonly float X, Z;
    public ArenaAreaVertex(float x, float z) { X = x; Z = z; }
}

public readonly struct ArenaAreaPoint
{
    public readonly float X, Y, Z;
    public ArenaAreaPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
}

public static class ArenaAreaCatalog
{
    public const string FullMapId = "full-map";
    // Change this versioned ID when geometry/rules change: a mismatched client
    // must not silently pair its old boundary with a newer server spawn filter.
    public const string FactorySouthId = "factory-south-v1";
    public const string CustomsFactoryCenterId = "customs-factory-center-v1";
    public const string CustomsFactoryCenterPolygonId = "customs-factory-center-polygon-v1";
    public const string CustomsFactoryCenterTeamsId = "customs-factory-center-polygon-v2";
    public const string CustomsFactoryEastPolygonId = "customs-factory-side-polygon-v2";
    public const string CustomsYardId = "customs-yard-polygon-v1";
    public const float SpawnInset = 0.75f;
    public const float NavigationInset = 0.4f;

    // Retired prototype geometry retained only for historical regression checks;
    // TryResolve and the native menu no longer admit these identities.
    // Spawn coordinates establish a candidate, not live navigation evidence.
    // Tunnel spawns are excluded. The bottom barrier closes descent below the
    // ground-level arena; the ceiling leaves headroom for jumping and vaulting.
    public static readonly ArenaAreaDefinition FactorySouth = new ArenaAreaDefinition(
        FactorySouthId, "Factory South (experimental 2v2)", ArenaMapCatalog.FactoryDayId,
        new ArenaAreaBounds(-25f, -1f, -46f, 40f, 10f, 26f), -0.5f, 2f, 2);

    // Native ZoneFactoryCenter spawns provide a compact first Customs candidate.
    // Coordinates are not proof of connectivity: each raid must still pass the
    // live enclosure/path checks before releasing either players or bots.
    public static readonly ArenaAreaDefinition CustomsFactoryCenter = new ArenaAreaDefinition(
        CustomsFactoryCenterId, "Customs Factory Center (experimental 2v2)", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(305f, 0f, -135f, 420f, 12f, -65f), 0.5f, 3f, 2);

    // User survey 20260907-192946, geometry SHA256
    // ff45e11f287393f189229d07e8034d7c5cb229cca81b96ce9ec782a9de990967.
    // Copy the measured XZ outline exactly; heights in the draft are not limits.
    // The elevated sniper marker is excluded by identity instead of hiding
    // all elevated or underground positions behind an arbitrary height band.
    public static readonly ArenaAreaDefinition CustomsFactoryCenterPolygon = new ArenaAreaDefinition(
        CustomsFactoryCenterPolygonId, "Customs Factory Center contour (experimental 2v2)", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(464.7143f, -145.724579f),
            new ArenaAreaVertex(467.552673f, -128.753784f),
            new ArenaAreaVertex(485.3326f, -117.640137f),
            new ArenaAreaVertex(493.686554f, -103.316223f),
            new ArenaAreaVertex(505.736938f, -27.9927254f),
            new ArenaAreaVertex(506.786652f, -24.71637f),
            new ArenaAreaVertex(379.45694f, -7.35720825f),
            new ArenaAreaVertex(372.48645f, -10.0381222f),
            new ArenaAreaVertex(355.6324f, -8.132172f),
            new ArenaAreaVertex(349.910278f, -3.30896f),
            new ArenaAreaVertex(327.595f, -1.9775393f),
            new ArenaAreaVertex(311.6721f, -104.798462f),
            new ArenaAreaVertex(315.104736f, -112.206207f),
            new ArenaAreaVertex(322.2989f, -117.058868f),
            new ArenaAreaVertex(330.003784f, -129.5967f),
        }, 0f, 12f, limitsHeight: false), 0.5f, 3f, 2,
        new[] { "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e" });

    // Unified Center: exact 20260928-172827 survey, geometry SHA256
    // c42302b34f4406624c9fec27b96bc4a68b5906f5d1d310f52fc9b75d8099f3e1.
    // The earlier fixed/team variants are retired at wire admission.
    public static readonly ArenaAreaDefinition CustomsFactoryCenterTeams = new ArenaAreaDefinition(
        CustomsFactoryCenterTeamsId, "Customs Factory Center", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(480.774139f, -20.654253f),
            new ArenaAreaVertex(414.180725f, -12.1899605f),
            new ArenaAreaVertex(379.439484f, -7.41137457f),
            new ArenaAreaVertex(376.1238f, -9.193539f),
            new ArenaAreaVertex(372.434937f, -10.7678471f),
            new ArenaAreaVertex(363.2147f, -9.386149f),
            new ArenaAreaVertex(355.809753f, -8.451146f),
            new ArenaAreaVertex(350.111267f, -3.189417f),
            new ArenaAreaVertex(333.3773f, -1.31976f),
            new ArenaAreaVertex(330.186371f, -1.39980423f),
            new ArenaAreaVertex(326.5494f, -2.8858285f),
            new ArenaAreaVertex(320.6984f, -41.18651f),
            new ArenaAreaVertex(317.594025f, -63.037674f),
            new ArenaAreaVertex(311.787872f, -104.613548f),
            new ArenaAreaVertex(313.250458f, -108.957359f),
            new ArenaAreaVertex(315.3602f, -111.791275f),
            new ArenaAreaVertex(322.4152f, -116.8172f),
            new ArenaAreaVertex(330.7475f, -116.722656f),
            new ArenaAreaVertex(329.7196f, -117.91555f),
            new ArenaAreaVertex(328.191742f, -122.978386f),
            new ArenaAreaVertex(331.880951f, -127.067513f),
            new ArenaAreaVertex(333.402466f, -128.276382f),
            new ArenaAreaVertex(359.154846f, -130.950912f),
            new ArenaAreaVertex(362.325531f, -132.227341f),
            new ArenaAreaVertex(366.1151f, -133.235336f),
            new ArenaAreaVertex(378.5905f, -134.8532f),
            new ArenaAreaVertex(410.830139f, -138.955154f),
            new ArenaAreaVertex(464.603851f, -145.668182f),
            new ArenaAreaVertex(466.24353f, -133.054871f),
            new ArenaAreaVertex(468.678375f, -128.774658f),
            new ArenaAreaVertex(475.5854f, -125.119453f),
            new ArenaAreaVertex(482.701752f, -119.993156f),
            new ArenaAreaVertex(485.6603f, -117.228752f),
            new ArenaAreaVertex(488.035675f, -114.122948f),
            new ArenaAreaVertex(490.391174f, -110.380127f),
            new ArenaAreaVertex(492.2905f, -106.930893f),
            new ArenaAreaVertex(493.916168f, -102.950264f),
            new ArenaAreaVertex(504.7133f, -34.5102234f),
            new ArenaAreaVertex(505.7819f, -27.6242523f),
            new ArenaAreaVertex(507.326019f, -24.4577465f),
        }, 0f, 12f, limitsHeight: false), 0.5f, 3f, 5,
        new[] { "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e" }, new ArenaTeamStarts(
            new[] { "2051eb7b-3ae8-49b8-adcb-c13f0ddc7972", "f8fa00b6-4941-4c21-b10b-77ff30b0c382" },
            new[] { "046309a0-3d13-48c4-a3e6-9bb509ac339d", "570f30b0-d2da-4f91-9270-45783c7c6a88", "75558193-c46a-490f-aa68-15650a803d6f", "785e4aeb-03eb-45f8-a521-2a870204a7d3", "cffefb6b-e763-4386-9625-8c002b16d4f3" },
            new[] { "2cb9950d-20ec-484e-919c-5489bb7abd5c", "4d2e6d0c-078c-4918-8227-8df098a47a39", "582b9ea6-4404-4af1-8443-da3db48fcaad", "6d8626ec-449b-4d63-a066-38e802756ec9", "b54ce786-74b8-41b1-889f-0fe62c36d8e0" },
            new[] { "13adbe1b-21b0-4422-90a9-eab3ee8313fb", "295048b1-140d-47c3-85f8-ab5bdaaafa67", "75c4714c-1d9f-411a-8a42-e9d5ee16a77a", "dd28bf37-44a2-4407-a30b-2b0e9ed732eb", "e880fea7-94e8-440f-874b-fd131ac2d998" }));

    // Exact user survey 20260928-172225, geometry SHA256
    // 7e2e907252db8f5f9bd5058c569b139577d7aedfc164f7c7b303839a69b7644c.
    // New wire identity rejects clients still using the earlier outline. Keep
    // the historical field name for source consumers, not as a protocol alias.
    // Positive world X projects left; Factory Side uses the native zone name.
    public static readonly ArenaAreaDefinition CustomsFactoryEastPolygon = new ArenaAreaDefinition(
        CustomsFactoryEastPolygonId, "Customs Factory Side", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(656.139343f, -45.62339f),
            new ArenaAreaVertex(652.390259f, -45.3716965f),
            new ArenaAreaVertex(638.6182f, -42.615036f),
            new ArenaAreaVertex(617.0731f, -39.4651f),
            new ArenaAreaVertex(581.060669f, -34.5355453f),
            new ArenaAreaVertex(536.45f, -28.5260582f),
            new ArenaAreaVertex(508.286f, -24.5994377f),
            new ArenaAreaVertex(506.456635f, -28.4905624f),
            new ArenaAreaVertex(499.766663f, -72.41972f),
            new ArenaAreaVertex(494.48114f, -104.261581f),
            new ArenaAreaVertex(492.764923f, -108.0333f),
            new ArenaAreaVertex(490.67627f, -111.62117f),
            new ArenaAreaVertex(488.2538f, -115.341141f),
            new ArenaAreaVertex(485.701233f, -118.5592f),
            new ArenaAreaVertex(482.2324f, -121.620949f),
            new ArenaAreaVertex(475.916656f, -126.164787f),
            new ArenaAreaVertex(468.456421f, -129.8445f),
            new ArenaAreaVertex(467.5111f, -133.1111f),
            new ArenaAreaVertex(465.583923f, -145.825562f),
            new ArenaAreaVertex(496.156738f, -149.6608f),
            new ArenaAreaVertex(562.981f, -158.225479f),
            new ArenaAreaVertex(634.7771f, -166.869736f),
            new ArenaAreaVertex(646.5289f, -167.732224f),
            new ArenaAreaVertex(650.6966f, -167.644424f),
            new ArenaAreaVertex(652.7254f, -167.675308f),
            new ArenaAreaVertex(655.0999f, -167.322479f),
            new ArenaAreaVertex(656.7311f, -167.479279f),
            new ArenaAreaVertex(656.3319f, -162.741516f),
            new ArenaAreaVertex(656.0348f, -158.648331f),
            new ArenaAreaVertex(663.7906f, -100.600174f),
            new ArenaAreaVertex(669.462769f, -58.2416344f),
            new ArenaAreaVertex(671.3696f, -48.25674f),
        }, 0f, 12f, limitsHeight: false), 0.5f, 3f, 5, teamStarts: new ArenaTeamStarts(
            // USEC: map-left sector, native PMC alternatives near X615.
            new[] { "4d676699-1243-452b-b4d7-3353616854fb", "5a551a12-ca0e-41af-9845-270ffce6808a", "8878252a-9516-4f33-a54d-56f9705098b6", "c11f719c-3491-4a60-901e-cf20832b432e", "d7bae537-f5a0-416d-9849-d939cc3575f4" },
            new[] { "4fa5e11e-daa0-4594-88ef-d43e87d19f94", "6524d251-5c63-43ae-a7db-ec131b06c33e", "ae0f2712-f51f-4576-943e-aec0c9c3d0b3", "b82d5efc-aa51-41fc-8ac2-9a243920ae7a", "caa78aec-ecbd-4420-9e7b-54f7435de639", "da7e6945-c403-4e9a-9f40-33e0d526264a", "ecf6b9e8-d95e-4697-85ca-3503aaabea04" },
            // BEAR: map-right sector, native PMC alternatives near X570.
            new[] { "105e161c-c1ad-4004-8c02-f3c95164a75e", "3b128730-ba41-4ac8-9c08-517655a787dd", "89fe6e6f-7ebc-458e-8297-621824d1d6ac", "8bae21f5-6d20-4368-a820-0a05730f81ad", "c70da15f-6bc8-4dda-b666-e4163754bcd5" },
            new[] { "01a445b0-4545-48da-9ce6-6d0190869360", "15fb12c7-625d-4665-9fb6-2774905743ca", "37a06e76-55b5-4a82-a26f-049a41e78c2e", "545760c5-2ab7-43af-bdb3-864b20706af3", "e508eac2-6cae-490b-89cc-4b2e6db5bcb0", "fa37fe1f-0927-4c2a-8d82-917f50cdd47f" }));

    // Exact user survey 20260928-173324; native ZoneCustoms courtyard.
    // Geometry SHA256 ccf8e459ddbbfea567a3d49dd31a69a2f7cb22cc485242d60b63c944682f8c3b.
    // Two compact candidate formations derived from native ground anchors.
    // Their scene clearance/connectivity must still pass the live checks.
    public static readonly ArenaAreaDefinition CustomsYard = new ArenaAreaDefinition(
        CustomsYardId, "Customs Yard", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(-157.857864f, -33.0622368f),
            new ArenaAreaVertex(-179.480118f, -21.1656284f),
            new ArenaAreaVertex(-183.429337f, -19.6527061f),
            new ArenaAreaVertex(-187.48082f, -20.07306f),
            new ArenaAreaVertex(-209.462921f, -30.4629726f),
            new ArenaAreaVertex(-212.065979f, -34.18947f),
            new ArenaAreaVertex(-216.393631f, -36.1204643f),
            new ArenaAreaVertex(-221.061768f, -35.3956642f),
            new ArenaAreaVertex(-224.238251f, -37.3490372f),
            new ArenaAreaVertex(-226.190018f, -40.64308f),
            new ArenaAreaVertex(-226.576431f, -56.29533f),
            new ArenaAreaVertex(-227.372391f, -64.89289f),
            new ArenaAreaVertex(-228.349518f, -90.11953f),
            new ArenaAreaVertex(-229.899155f, -145.315353f),
            new ArenaAreaVertex(-230.46756f, -160.495377f),
            new ArenaAreaVertex(-230.90567f, -184.935516f),
            new ArenaAreaVertex(-228.963318f, -188.153168f),
            new ArenaAreaVertex(-220.473282f, -188.710175f),
            new ArenaAreaVertex(-201.054565f, -189.978363f),
            new ArenaAreaVertex(-161.672836f, -191.890717f),
            new ArenaAreaVertex(-155.3688f, -192.211319f),
            new ArenaAreaVertex(-154.216156f, -156.130814f),
            new ArenaAreaVertex(-153.723663f, -121.276749f),
            new ArenaAreaVertex(-153.020264f, -75.8379059f),
            new ArenaAreaVertex(-152.27092f, -52.01178f),
            new ArenaAreaVertex(-153.111221f, -46.64779f),
            new ArenaAreaVertex(-153.016388f, -39.0447121f),
            new ArenaAreaVertex(-154.859177f, -35.7736664f),
        }, 0f, 12f, limitsHeight: false), 0.5f, 3f, 5, teamStarts: new ArenaTeamStarts(
            new[] { "9bde6412-a3d8-566d-919a-153ff1f328dd", "d1a539c6-50eb-5c54-afc4-4464b2b3fa8b" },
            new[] { "9ccb87c2-4d70-5da0-af13-d423f6dfba8d", "3ed2dd78-69d2-5b0c-827d-fd6f1d636214", "3bf4fa37-e21f-5e62-8a61-04873b9b43a4", "50521378-ff8f-56b5-8be0-240aba209514", "b9713176-1481-53eb-aa79-ed173f4e388d", "318c7c45-8aad-57cf-9213-7993544d15b0", "118fc856-33dc-511f-8c65-aea9ce4db4b1", "5d67bea5-0f29-5017-9191-7c25ff900a76" },
            new[] { "bbc9f3d1-8918-5da5-a7aa-c64bdbb3e64c", "45c53e98-06a7-5bad-b12b-302e823d1d57" },
            new[] { "06145194-2a3d-50bb-b06c-b2638d469d16", "ec42fdf5-3b8d-5c2e-a4d5-f36a6e647ad2", "6cdfea2a-2190-5614-8bfa-6f19a08a59d4", "3db5f8a5-ef12-50c6-86f1-f5578d4ca3a0", "88039b43-f3bd-56c2-b051-091a1937bade", "a1987c27-d8a8-5cc9-9bf8-63199d0702e1", "d0e218aa-fd77-5d4d-bc28-66bc9beb031a", "3eba0f2a-55e6-52ca-88af-70c2967b3d01" }));

    public static readonly IReadOnlyList<ArenaAreaSpawnAddition> CustomsYardSpawns = Array.AsReadOnly(new[]
    {
        new ArenaAreaSpawnAddition("9bde6412-a3d8-566d-919a-153ff1f328dd", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 0f, -2f, true),
        new ArenaAreaSpawnAddition("d1a539c6-50eb-5c54-afc4-4464b2b3fa8b", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 0f, 2f, true),
        new ArenaAreaSpawnAddition("9ccb87c2-4d70-5da0-af13-d423f6dfba8d", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), -4f, -4f, false),
        new ArenaAreaSpawnAddition("3ed2dd78-69d2-5b0c-827d-fd6f1d636214", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 0f, -4f, false),
        new ArenaAreaSpawnAddition("3bf4fa37-e21f-5e62-8a61-04873b9b43a4", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 4f, -4f, false),
        new ArenaAreaSpawnAddition("50521378-ff8f-56b5-8be0-240aba209514", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), -4f, 0f, false),
        new ArenaAreaSpawnAddition("b9713176-1481-53eb-aa79-ed173f4e388d", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 4f, 0f, false),
        new ArenaAreaSpawnAddition("318c7c45-8aad-57cf-9213-7993544d15b0", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), -4f, 4f, false),
        new ArenaAreaSpawnAddition("118fc856-33dc-511f-8c65-aea9ce4db4b1", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 0f, 6f, false),
        new ArenaAreaSpawnAddition("5d67bea5-0f29-5017-9191-7c25ff900a76", "f2d2c753-7e4c-4055-83da-b1c2693d4552",
            new ArenaAreaPoint(-204.84001f, 1.0786875f, -133.961f), 4f, 4f, false),
        new ArenaAreaSpawnAddition("bbc9f3d1-8918-5da5-a7aa-c64bdbb3e64c", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 0f, -2f, true),
        new ArenaAreaSpawnAddition("45c53e98-06a7-5bad-b12b-302e823d1d57", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 0f, 2f, true),
        new ArenaAreaSpawnAddition("06145194-2a3d-50bb-b06c-b2638d469d16", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), -4f, -4f, false),
        new ArenaAreaSpawnAddition("ec42fdf5-3b8d-5c2e-a4d5-f36a6e647ad2", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 0f, -4f, false),
        new ArenaAreaSpawnAddition("6cdfea2a-2190-5614-8bfa-6f19a08a59d4", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 4f, -4f, false),
        new ArenaAreaSpawnAddition("3db5f8a5-ef12-50c6-86f1-f5578d4ca3a0", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), -4f, 0f, false),
        new ArenaAreaSpawnAddition("88039b43-f3bd-56c2-b051-091a1937bade", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 4f, 0f, false),
        new ArenaAreaSpawnAddition("a1987c27-d8a8-5cc9-9bf8-63199d0702e1", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), -4f, 4f, false),
        new ArenaAreaSpawnAddition("d0e218aa-fd77-5d4d-bc28-66bc9beb031a", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 0f, 6f, false),
        new ArenaAreaSpawnAddition("3eba0f2a-55e6-52ca-88af-70c2967b3d01", "7f751d14-f03f-406c-94d9-1600aa068835",
            new ArenaAreaPoint(-170.24002f, 1.063748f, -46.462997f), 4f, 4f, false),
    });

    /// <summary>Zero denotes the unchanged full-map/legacy protocol, never an inferred new-area choice.</summary>
    public static bool TryResolveTeamSize(ArenaAreaDefinition? area, int? requested, out int selected, out string? reason)
    {
        selected = 0; reason = null;
        if (area?.TeamStarts == null)
        {
            if (requested.HasValue && requested.Value != 0)
            { reason = "This area does not accept a team-size override."; return false; }
            return true;
        }
        if (!requested.HasValue || requested.Value < 2 || requested.Value > area.MaxTeamSize)
        { reason = "Select an explicit supported area team size (2.." + area.MaxTeamSize + ")."; return false; }
        selected = requested.Value;
        return true;
    }

    /// <summary>Only independent TDM negotiates a faction size. Other modes own their roster.</summary>
    public static bool TryResolveTeamSize(ArenaAreaDefinition? area, string? mode, bool pitFireTeam,
        int? requested, out int selected, out string? reason)
    {
        if (!pitFireTeam && string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))
            return TryResolveTeamSize(area, requested, out selected, out reason);
        selected = 0;
        reason = null;
        if (requested.HasValue && requested.Value != 0)
        { reason = "Only independent Team Deathmatch accepts an area team-size override."; return false; }
        return true;
    }

    public static string NormalizeId(string? id)
        => string.IsNullOrWhiteSpace(id) || string.Equals(id, FullMapId, StringComparison.Ordinal)
            ? FullMapId : id;

    public static bool TryResolve(string? id, string? map, string? mode, bool pitFireTeam,
        out ArenaAreaDefinition? area, out string? reason)
    {
        area = null;
        reason = null;
        id = NormalizeId(id);
        // Full maps share the generic native zone/point planner. Bay5 and
        // Equator do not require a surveyed faction-start preset: Tactical
        // asks for one complete opposing squad, independent TDM reserves each
        // faction slot, and Zombies requests individual infected. Optional map
        // presence and the authenticated PFT roster are checked by their owners.
        // Expanding these maps does not revive legacy/unidentified wire modes
        // whose population semantics were never admitted here. Existing native
        // maps keep their separate legacy compatibility unchanged.
        if (ArenaMapCatalog.RequiresModeFilteredView(map!)
            && !string.Equals(mode, "deathmatch", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "gungame", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase))
        { reason = "Select a current Arena mode for this map."; return false; }
        // Equator's center has only four bot markers beyond the unchanged 25 m
        // floor. Tactical needs five simultaneously, including after a surviving
        // player's round transition; safe opening spawns alone cannot prove that.
        if (ArenaMapCatalog.IsEquator(map!)
            && string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase))
        { reason = "Equator has too few distant bot spawn points for Tactical's five-opponent squad."; return false; }
        // Solo survival remains supported; the compact map does not have enough
        // room for the protected five-player squad and its infected wave together.
        if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)
            && string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase))
        { reason = "Equator is too small for Zombies with a PitFireTeam squad; select Solo or another map."; return false; }
        if (id == FullMapId)
        {
            if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))
            { reason = "Select one of the surveyed Customs areas; whole Customs is unavailable in Arena."; return false; }
            return true;
        }
        var candidate = id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams
            : id == CustomsFactoryEastPolygonId ? CustomsFactoryEastPolygon
            : id == CustomsYardId ? CustomsYard : null;
        if (candidate == null)
        { reason = "Unknown or incompatible Arena area: " + id; return false; }
        if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase))
        { reason = "PitFireTeam requires Tactical or Zombies."; return false; }
        if (!string.Equals(map, candidate.MapId, StringComparison.OrdinalIgnoreCase))
        { reason = candidate.DisplayName + " requires map " + candidate.MapId + "."; return false; }
        area = candidate;
        return true;
    }
}
/// <summary>Exact surveyed faction starts. Orientation is defined per preset; never retains caller-owned sets.</summary>
public sealed class ArenaTeamStarts
{
    private readonly HashSet<string> _usecPlayers, _usecBots, _bearPlayers, _bearBots;
    public ArenaTeamStarts(IEnumerable<string> usecPlayers, IEnumerable<string> usecBots,
        IEnumerable<string> bearPlayers, IEnumerable<string> bearBots)
    {
        _usecPlayers = Copy(usecPlayers); _usecBots = Copy(usecBots);
        _bearPlayers = Copy(bearPlayers); _bearBots = Copy(bearBots);
        if (_usecPlayers.Overlaps(_bearPlayers) || _usecBots.Overlaps(_bearBots)
            || _usecPlayers.Overlaps(_bearBots) || _bearPlayers.Overlaps(_usecBots))
            throw new ArgumentException("Opposing start identities must be disjoint.");
    }
    public bool Allows(bool usec, bool player, string? id)
        => id != null && (usec ? (player ? _usecPlayers : _usecBots)
            : (player ? _bearPlayers : _bearBots)).Contains(id);
    private static HashSet<string> Copy(IEnumerable<string> ids)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
            if (string.IsNullOrWhiteSpace(id) || !result.Add(id))
                throw new ArgumentException("Start identities must be non-empty and unique.");
        if (result.Count == 0) throw new ArgumentException("A start side must not be empty.");
        return result;
    }
}

/// <summary>Immutable extra marker; source anchor is checked before adapting the per-raid clone.</summary>
public sealed class ArenaAreaSpawnAddition
{
    public string Id { get; }
    public string SourceId { get; }
    public ArenaAreaPoint SourcePosition { get; }
    public ArenaAreaPoint Position { get; }
    public bool Player { get; }
    public const string Zone = "ZoneCustoms";
    public ArenaAreaSpawnAddition(string id, string sourceId, ArenaAreaPoint source, float offsetX, float offsetZ, bool player)
    {
        Id = id; SourceId = sourceId; SourcePosition = source;
        Position = new ArenaAreaPoint(source.X + offsetX, source.Y, source.Z + offsetZ);
        Player = player;
    }
}
