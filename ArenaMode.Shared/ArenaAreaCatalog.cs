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

    public ArenaAreaDefinition(string id, string displayName, string mapId,
        ArenaAreaBounds bounds, float spawnMinY, float spawnMaxY, int maxTeamSize)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(mapId)
            || !ArenaAreaBounds.Finite(spawnMinY) || !ArenaAreaBounds.Finite(spawnMaxY)
            || spawnMinY > spawnMaxY || spawnMinY < bounds.MinY || spawnMaxY > bounds.MaxY
            || bounds.MinX >= bounds.MaxX || bounds.MinY >= bounds.MaxY || bounds.MinZ >= bounds.MaxZ
            || maxTeamSize < 2)
            throw new ArgumentException("Invalid Arena area definition.");
        Id = id;
        DisplayName = displayName;
        MapId = mapId;
        Bounds = bounds;
        SpawnMinY = spawnMinY;
        SpawnMaxY = spawnMaxY;
        MaxTeamSize = maxTeamSize;
    }

    // Foot-point band is deliberately separate from the enclosing volume:
    // an ordinary jump must not hit a ceiling placed at the spawn's height.
    public bool AllowsSpawn(float x, float y, float z)
        => Bounds.Contains(x, y, z, ArenaAreaCatalog.SpawnInset)
           && y >= SpawnMinY && y <= SpawnMaxY;
}

/// <summary>
/// A finite convex volume. Every segment between contained endpoints remains
/// inside, including body-to-first-corner; concave polygons need a different rule.
/// </summary>
public readonly struct ArenaAreaBounds
{
    public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

    public ArenaAreaBounds(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        if (!Finite(minX) || !Finite(minY) || !Finite(minZ)
            || !Finite(maxX) || !Finite(maxY) || !Finite(maxZ)
            || minX >= maxX || minY >= maxY || minZ >= maxZ)
            throw new ArgumentException("Arena area bounds must be finite and ordered.");
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
    }

    public bool Contains(float x, float y, float z, float horizontalInset = 0f)
        => Finite(x) && Finite(y) && Finite(z) && Finite(horizontalInset)
           && horizontalInset >= 0f && MinX < MaxX && MinY < MaxY && MinZ < MaxZ
           && x >= MinX + horizontalInset && x <= MaxX - horizontalInset
           && y >= MinY && y <= MaxY
           && z >= MinZ + horizontalInset && z <= MaxZ - horizontalInset;

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
            if (!Contains(point.X, point.Y, point.Z, horizontalInset)) return false;
        }
        return true;
    }

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
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
    public const float SpawnInset = 0.75f;
    public const float NavigationInset = 0.4f;

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
            { reason = "Customs currently requires the experimental Factory Center Team Deathmatch area."; return false; }
            return true;
        }
        var candidate = id == FactorySouthId ? FactorySouth
            : id == CustomsFactoryCenterId ? CustomsFactoryCenter : null;
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
