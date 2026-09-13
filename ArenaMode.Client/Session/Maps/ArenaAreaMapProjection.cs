using System;
using ArenaMode.Shared;

namespace ArenaMode.Session.Maps;

/// <summary>
/// Map-image coordinates use a top-left origin, unlike Unity UI's upward Y.
/// Calibration is data from Customs_TarkovDev; no DynamicMaps code/runtime is used.
/// The same transform positions the image, selected fill and every boundary.
/// </summary>
internal readonly struct ArenaMapPoint
{
    internal readonly float X, Y;
    internal ArenaMapPoint(float x, float y) { X = x; Y = y; }
}

internal readonly struct ArenaAreaMapProjection
{
    internal const float ImageWidth = 1062.48f, ImageHeight = 535.17f;
    internal const float WorldMinX = -372f, WorldMaxX = 698f, WorldMinZ = -306f, WorldMaxZ = 235f;
    internal readonly float Left, Top, Width, Height;

    private ArenaAreaMapProjection(float left, float top, float width, float height)
    { Left = left; Top = top; Width = width; Height = height; }

    internal static ArenaMapPoint ToUv(float x, float z)
        => new((WorldMaxX - x) / (WorldMaxX - WorldMinX),
            (z - WorldMinZ) / (WorldMaxZ - WorldMinZ));

    internal ArenaMapPoint FromUv(float u, float v) => new(Left + u * Width, Top + v * Height);
    internal ArenaMapPoint Project(ArenaAreaVertex vertex)
    { var uv = ToUv(vertex.X, vertex.Z); return FromUv(uv.X, uv.Y); }

    internal static bool TryCreate(ArenaAreaBounds bounds, bool overview, float viewportWidth,
        float viewportHeight, out ArenaAreaMapProjection projection)
    {
        projection = default;
        if (!bounds.IsValid || !ArenaAreaBounds.Finite(viewportWidth) || !ArenaAreaBounds.Finite(viewportHeight)
            || viewportWidth <= 0f || viewportHeight <= 0f) return false;
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        if (!overview)
        {
            // Twenty-four world metres of context, including shared roads and
            // neighbouring buildings; preserve the source image's aspect ratio.
            const float margin = 24f;
            var first = ToUv(bounds.MaxX + margin, bounds.MinZ - margin);
            var last = ToUv(bounds.MinX - margin, bounds.MaxZ + margin);
            u0 = Math.Max(0f, first.X); v0 = Math.Max(0f, first.Y);
            u1 = Math.Min(1f, last.X); v1 = Math.Min(1f, last.Y);
        }
        if (u1 <= u0 || v1 <= v0) return false;
        float scale = Math.Min(viewportWidth / ((u1 - u0) * ImageWidth),
            viewportHeight / ((v1 - v0) * ImageHeight));
        float width = ImageWidth * scale, height = ImageHeight * scale;
        float left = (viewportWidth - (u1 - u0) * width) * .5f - u0 * width;
        float top = (viewportHeight - (v1 - v0) * height) * .5f - v0 * height;
        if (!ArenaAreaBounds.Finite(width) || !ArenaAreaBounds.Finite(height)
            || !ArenaAreaBounds.Finite(left) || !ArenaAreaBounds.Finite(top) || width <= 0f || height <= 0f) return false;
        projection = new ArenaAreaMapProjection(left, top, width, height);
        return true;
    }
}
