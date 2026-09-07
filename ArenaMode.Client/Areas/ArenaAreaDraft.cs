using ArenaMode.Shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ArenaMode.Areas;

/// <summary>Reviewable point capture only: never installed in the shared area catalog.</summary>
internal sealed class ArenaAreaDraft
{
    public const int MaxPoints = 64;
    private readonly List<ArenaAreaPoint> _points = new();
    public IReadOnlyList<ArenaAreaPoint> Points { get; }
    public string MapId { get; }
    public string SourceAreaId { get; }

    public ArenaAreaDraft(string mapId, string sourceAreaId)
    {
        if (!ArenaMapCatalog.IsSupportedMap(mapId)) throw new ArgumentException("Unsupported capture map.");
        MapId = mapId.ToLowerInvariant();
        SourceAreaId = sourceAreaId ?? ArenaAreaCatalog.FullMapId;
        Points = _points.AsReadOnly();
    }

    public bool Add(ArenaAreaPoint point, out string reason)
    {
        reason = null;
        if (_points.Count >= MaxPoints) { reason = "Maximum 64 points. Undo a point before adding more."; return false; }
        if (!ArenaAreaBounds.Finite(point.X) || !ArenaAreaBounds.Finite(point.Y) || !ArenaAreaBounds.Finite(point.Z)
            || Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000 || Math.Abs(point.Z) > 100000)
        { reason = "Point coordinates are invalid or outside the capture range."; return false; }
        if (_points.Any(p => (double)(p.X - point.X) * (p.X - point.X) + (double)(p.Z - point.Z) * (p.Z - point.Z) < .0025))
        { reason = "This X/Z point already exists. The last edge closes automatically."; return false; }
        _points.Add(point);
        return true;
    }

    public bool Undo()
    {
        if (_points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        return true;
    }

    public void Clear() => _points.Clear();

    public bool Validate(float minY, float maxY, float spawnMinY, float spawnMaxY,
        out ArenaAreaBounds bounds, out string reason)
    {
        bounds = default;
        reason = null;
        if (_points.Count < 3) { reason = "At least three points are required."; return false; }
        try
        {
            bounds = new ArenaAreaBounds(_points.Select(p => new ArenaAreaVertex(p.X, p.Z)).ToArray(), minY, maxY);
            if (bounds.MaxX - bounds.MinX > 2000 || bounds.MaxZ - bounds.MinZ > 2000 || maxY - minY > 200)
                throw new ArgumentException("Draft is too large (maximum 2000m X/Z and 200m height).");
            // Reuse the real preset validation, including a separate spawn foot
            // band inside the enclosing floor/ceiling. Team size stays at 2v2.
            _ = new ArenaAreaDefinition("draft", "Draft", MapId, bounds, spawnMinY, spawnMaxY, 2);
            return true;
        }
        catch (ArgumentException ex) { reason = ex.Message; return false; }
    }

    public bool TryExport(float minY, float maxY, float spawnMinY, float spawnMaxY,
        out string json, out string reason)
    {
        json = null;
        if (!Validate(minY, maxY, spawnMinY, spawnMaxY, out var bounds, out reason)) return false;
        var geometry = new
        {
            mapId = MapId,
            vertices = bounds.Outline.Select(p => new { x = p.X, z = p.Z }).ToArray(),
            minY, maxY, spawnMinY, spawnMaxY, maxTeamSize = 2
        };
        string fingerprint;
        using (var sha = SHA256.Create())
            fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(geometry, Formatting.None)))).Replace("-", "").ToLowerInvariant();
        json = JsonConvert.SerializeObject(new
        {
            schemaVersion = 1,
            kind = "ArenaMode.AreaDraft",
            draftOnly = true,
            sourceAreaId = SourceAreaId,
            proposedAreaId = MapId + "-polygon-" + fingerprint.Substring(0, 12),
            geometrySha256 = fingerprint,
            geometry,
            capturedPoints = _points.Select(p => new { x = p.X, y = p.Y, z = p.Z }).ToArray(),
            note = "Not loaded by the mod. Review the contour, spawns and paths, then publish the same new preset ID and geometry on client and server."
        }, Formatting.Indented);
        return true;
    }
}
