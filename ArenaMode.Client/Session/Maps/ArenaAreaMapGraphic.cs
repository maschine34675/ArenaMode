using System.Collections.Generic;
using ArenaMode.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session.Maps;

/// <summary>One texture, a dim full-map quad and bright selected floor triangles.</summary>
internal sealed class ArenaAreaMapGraphic : MaskableGraphic
{
    private Texture _texture;
    private ArenaAreaBounds _bounds;
    private bool _overview;
    public override Texture mainTexture => _texture != null ? _texture : base.mainTexture;

    internal void Set(Texture texture, ArenaAreaBounds bounds, bool overview)
    { _texture = texture; _bounds = bounds; _overview = overview; SetAllDirty(); }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        if (_texture == null || !ArenaAreaMapProjection.TryCreate(_bounds, _overview, rect.width, rect.height, out var projection)) return;
        var dim = new Color(.38f, .38f, .38f, 1f);
        Add(mesh, rect, projection, new ArenaMapPoint(0f, 0f), dim);
        Add(mesh, rect, projection, new ArenaMapPoint(1f, 0f), dim);
        Add(mesh, rect, projection, new ArenaMapPoint(1f, 1f), dim);
        Add(mesh, rect, projection, new ArenaMapPoint(0f, 1f), dim);
        mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
        foreach (var vertex in _bounds.Outline)
            Add(mesh, rect, projection, ArenaAreaMapProjection.ToUv(vertex.X, vertex.Z), Color.white);
        for (int i = 0; i < _bounds.FloorTriangles.Count; i += 3)
            mesh.AddTriangle(4 + _bounds.FloorTriangles[i], 4 + _bounds.FloorTriangles[i + 1], 4 + _bounds.FloorTriangles[i + 2]);
    }

    private static void Add(VertexHelper mesh, Rect rect, ArenaAreaMapProjection projection, ArenaMapPoint uv, Color color)
    {
        var point = projection.FromUv(uv.X, uv.Y);
        mesh.AddVert(new Vector3(rect.xMin + point.X, rect.yMax - point.Y, 0f), color, new Vector2(uv.X, 1f - uv.Y));
    }
}

/// <summary>Untextured fill and constant-width edges; the parent RectMask2D clips zoomed geometry.</summary>
internal sealed class ArenaAreaBoundaryGraphic : MaskableGraphic
{
    private ArenaAreaBounds _selected;
    private IReadOnlyList<ArenaAreaBounds> _others;
    private bool _overview;
    private static readonly Color Boundary = new(.3f, .72f, 1f, 1f);

    internal void Set(ArenaAreaBounds selected, IReadOnlyList<ArenaAreaBounds> others, bool overview)
    { _selected = selected; _others = others; _overview = overview; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        if (!ArenaAreaMapProjection.TryCreate(_selected, _overview, rect.width, rect.height, out var projection)) return;
        if (_overview && _others != null)
            foreach (var bounds in _others)
                if (!ReferenceEquals(bounds.Outline, _selected.Outline)) Outline(mesh, rect, projection, bounds, 1.25f, new Color(.7f, .7f, .65f, .6f));
        int start = mesh.currentVertCount;
        foreach (var vertex in _selected.Outline)
        {
            var point = projection.Project(vertex);
            mesh.AddVert(new Vector3(rect.xMin + point.X, rect.yMax - point.Y, 0f), new Color(.3f, .72f, 1f, .12f), Vector2.zero);
        }
        for (int i = 0; i < _selected.FloorTriangles.Count; i += 3)
            mesh.AddTriangle(start + _selected.FloorTriangles[i], start + _selected.FloorTriangles[i + 1], start + _selected.FloorTriangles[i + 2]);
        Outline(mesh, rect, projection, _selected, 5f, new Color(.02f, .025f, .03f, .9f));
        Outline(mesh, rect, projection, _selected, 2.5f, Boundary);
    }

    private static void Outline(VertexHelper mesh, Rect rect, ArenaAreaMapProjection projection,
        ArenaAreaBounds bounds, float width, Color color)
    {
        for (int i = 0; i < bounds.Outline.Count; i++)
        {
            var a = projection.Project(bounds.Outline[i]);
            var b = projection.Project(bounds.Outline[(i + 1) % bounds.Outline.Count]);
            float dx = b.X - a.X, dy = b.Y - a.Y, length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= .001f) continue;
            float nx = -dy / length * width * .5f, ny = dx / length * width * .5f;
            int index = mesh.currentVertCount;
            Add(mesh, rect, a.X + nx, a.Y + ny, color); Add(mesh, rect, b.X + nx, b.Y + ny, color);
            Add(mesh, rect, b.X - nx, b.Y - ny, color); Add(mesh, rect, a.X - nx, a.Y - ny, color);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }

    private static void Add(VertexHelper mesh, Rect rect, float x, float y, Color color)
        => mesh.AddVert(new Vector3(rect.xMin + x, rect.yMax - y, 0f), color, Vector2.zero);
}
