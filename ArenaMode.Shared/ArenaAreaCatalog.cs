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
    public const string CustomsFactoryCenterTeamsId = "customs-factory-center-teams-v1";
    public const string CustomsFactoryEastPolygonId = "customs-factory-east-polygon-v1";
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

    // Separate protocol identity: the old surveyed contour remains fixed 2v2.
    public static readonly ArenaAreaDefinition CustomsFactoryCenterTeams = new ArenaAreaDefinition(
        CustomsFactoryCenterTeamsId, "Customs contour — team starts", ArenaMapCatalog.CustomsId,
        CustomsFactoryCenterPolygon.Bounds, 0.5f, 3f, 5,
        new[] { "6e771bc5-ce82-4ab8-88e2-d50dedf4e34e" }, new ArenaTeamStarts(
            new[] { "2051eb7b-3ae8-49b8-adcb-c13f0ddc7972", "f8fa00b6-4941-4c21-b10b-77ff30b0c382" },
            new[] { "046309a0-3d13-48c4-a3e6-9bb509ac339d", "570f30b0-d2da-4f91-9270-45783c7c6a88", "75558193-c46a-490f-aa68-15650a803d6f", "785e4aeb-03eb-45f8-a521-2a870204a7d3", "cffefb6b-e763-4386-9625-8c002b16d4f3" },
            new[] { "2cb9950d-20ec-484e-919c-5489bb7abd5c", "4d2e6d0c-078c-4918-8227-8df098a47a39", "582b9ea6-4404-4af1-8443-da3db48fcaad", "6d8626ec-449b-4d63-a066-38e802756ec9", "b54ce786-74b8-41b1-889f-0fe62c36d8e0" },
            new[] { "13adbe1b-21b0-4422-90a9-eab3ee8313fb", "295048b1-140d-47c3-85f8-ab5bdaaafa67", "75c4714c-1d9f-411a-8a42-e9d5ee16a77a", "dd28bf37-44a2-4407-a30b-2b0e9ed732eb", "e880fea7-94e8-440f-874b-fd131ac2d998" }));

    // User survey 20260913-090513, geometry SHA256
    // bddf25bdaec03bce8a140cbd7fe0dd73ddc58ef58672ca5ce30078f1f1a7ad1e.
    // Adjacent factory grounds (native ZoneFactorySide). Positive world X projects
    // left in the calibrated map; use the native zone name, not a compass guess.
    // Preserve every measured XZ vertex; draft heights are hints, not limits.
    // The native sniper marker and checkpoint spawns are outside this contour.
    // Exact starts were surveyed from the installed native marker database. The
    // original ID remains compatible because old fixed-team null/0 requests are
    // rejected by TryResolveTeamSize, and old servers reject new 2..5 overrides.
    // Live carving and complete in-area navigation still decide raid readiness.
    public static readonly ArenaAreaDefinition CustomsFactoryEastPolygon = new ArenaAreaDefinition(
        CustomsFactoryEastPolygonId, "Customs Factory Side — team starts", ArenaMapCatalog.CustomsId,
        new ArenaAreaBounds(new[]
        {
            new ArenaAreaVertex(494.5332f, -104.145111f),
            new ArenaAreaVertex(491.2321f, -110.743347f),
            new ArenaAreaVertex(485.52002f, -119.189484f),
            new ArenaAreaVertex(479.8096f, -123.66256f),
            new ArenaAreaVertex(475.6268f, -126.166672f),
            new ArenaAreaVertex(468.341431f, -130.243744f),
            new ArenaAreaVertex(466.07547f, -145.328766f),
            new ArenaAreaVertex(648.5004f, -167.768631f),
            new ArenaAreaVertex(655.292847f, -166.976868f),
            new ArenaAreaVertex(670.6625f, -48.0239f),
            new ArenaAreaVertex(507.023682f, -25.097086f),
        }, 0f, 12f, limitsHeight: false), 0.5f, 3f, 5, teamStarts: new ArenaTeamStarts(
            // USEC: map-left sector, native PMC alternatives near X615.
            new[] { "4d676699-1243-452b-b4d7-3353616854fb", "5a551a12-ca0e-41af-9845-270ffce6808a", "8878252a-9516-4f33-a54d-56f9705098b6", "c11f719c-3491-4a60-901e-cf20832b432e", "d7bae537-f5a0-416d-9849-d939cc3575f4" },
            new[] { "4fa5e11e-daa0-4594-88ef-d43e87d19f94", "6524d251-5c63-43ae-a7db-ec131b06c33e", "ae0f2712-f51f-4576-943e-aec0c9c3d0b3", "b82d5efc-aa51-41fc-8ac2-9a243920ae7a", "caa78aec-ecbd-4420-9e7b-54f7435de639", "da7e6945-c403-4e9a-9f40-33e0d526264a", "e06b071c-db66-4823-93ea-ea24471eb829", "ecf6b9e8-d95e-4697-85ca-3503aaabea04" },
            // BEAR: map-right sector, native PMC alternatives near X570.
            new[] { "105e161c-c1ad-4004-8c02-f3c95164a75e", "3b128730-ba41-4ac8-9c08-517655a787dd", "89fe6e6f-7ebc-458e-8297-621824d1d6ac", "8bae21f5-6d20-4368-a820-0a05730f81ad", "c70da15f-6bc8-4dda-b666-e4163754bcd5" },
            new[] { "01a445b0-4545-48da-9ce6-6d0190869360", "15fb12c7-625d-4665-9fb6-2774905743ca", "37a06e76-55b5-4a82-a26f-049a41e78c2e", "545760c5-2ab7-43af-bdb3-864b20706af3", "e508eac2-6cae-490b-89cc-4b2e6db5bcb0", "fa37fe1f-0927-4c2a-8d82-917f50cdd47f" }));

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

    public static string NormalizeId(string? id)
        => string.IsNullOrWhiteSpace(id) || string.Equals(id, FullMapId, StringComparison.Ordinal)
            ? FullMapId : id;

    public static bool TryResolve(string? id, string? map, string? mode, bool pitFireTeam,
        out ArenaAreaDefinition? area, out string? reason)
    {
        area = null;
        reason = null;
        id = NormalizeId(id);
        if (id == FullMapId)
        {
            if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))
            { reason = "Customs currently requires an experimental Team Deathmatch area."; return false; }
            return true;
        }
        var candidate = id == CustomsFactoryCenterPolygonId ? CustomsFactoryCenterPolygon
            : id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams
            : id == CustomsFactoryEastPolygonId ? CustomsFactoryEastPolygon : null;
        if (candidate == null)
        { reason = "Unknown or incompatible Arena area: " + id; return false; }
        if (pitFireTeam || !string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))
        { reason = "Bounded areas currently require independent Team Deathmatch without PitFireTeam."; return false; }
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
