using System;

namespace ArenaMode.Modes;

/// <summary>
/// Opening lanes for a Tactical round (docs/reviews/TACTICAL-OBJECTIVE-20261108.md): one point per
/// squad bot across the middle between the two groups, side by side at right angles to the line
/// joining their centroids, so the squad fans out instead of filing down one path. Pure
/// geometry; the caller projects every lane onto the NavMesh and drops what does not land.
/// </summary>
/// <summary>A lane position before NavMesh projection; plain floats so the pure math links without Unity.</summary>
internal readonly struct TacticalLanePoint
{
    public readonly float X, Y, Z;
    public TacticalLanePoint(float x, float y, float z) { X = x; Y = y; Z = z; }
}

internal static class TacticalObjectiveLanes
{
    internal const float MinSpacing = 6f;
    internal const float MaxSpacing = 18f;
    internal const float SpacingFraction = .15f;

    /// <summary>
    /// Lanes between the squad centroid <paramref name="squad"/> and the player side centroid
    /// <paramref name="players"/>. Empty when either centroid is missing or no lane is wanted.
    /// </summary>
    internal static TacticalLanePoint[] Compute(TacticalLanePoint? squad, TacticalLanePoint? players, int lanes)
    {
        if (!squad.HasValue || !players.HasValue || lanes < 1) return Array.Empty<TacticalLanePoint>();
        var a = squad.Value;
        var b = players.Value;
        float midX = (a.X + b.X) * .5f, midY = (a.Y + b.Y) * .5f, midZ = (a.Z + b.Z) * .5f;
        float axisX = b.X - a.X, axisZ = b.Z - a.Z;
        float length = (float)Math.Sqrt(axisX * axisX + axisZ * axisZ);
        if (float.IsNaN(length) || float.IsInfinity(length)) return Array.Empty<TacticalLanePoint>();
        if (length < 1f) { axisX = 0f; axisZ = 1f; }
        else { axisX /= length; axisZ /= length; }
        // Perpendicular in the ground plane: up x axis.
        float lateralX = axisZ, lateralZ = -axisX;
        float spacing = Math.Min(MaxSpacing, Math.Max(MinSpacing, length * SpacingFraction));
        var result = new TacticalLanePoint[lanes];
        for (int i = 0; i < lanes; i++)
        {
            float offset = (i - (lanes - 1) * .5f) * spacing;
            result[i] = new TacticalLanePoint(midX + lateralX * offset, midY, midZ + lateralZ * offset);
        }
        return result;
    }
}
