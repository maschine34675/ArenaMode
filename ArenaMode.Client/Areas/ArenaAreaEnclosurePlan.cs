using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Areas;

/// <summary>Pure, bounded layout consumed by the actual Unity builder and offline checks.</summary>
internal sealed class ArenaAreaEnclosurePlan
{
    internal const float Thickness = .6f;
    internal const float CornerRadius = .3f;
    internal const float ProbeSpacing = 4f;
    internal const float ProbeOffset = 1f;
    internal const float GridSpacing = 3f;
    internal const float GridWidth = .055f;
    internal const int MaxVertices = 60000; // Stay below Unity's default 16-bit mesh index limit.
    internal const int MaxProbes = 4096;
    private const int MaxCapClipOperations = 20000;
    private const float EndOverlap = .05f;

    internal readonly struct Point
    {
        internal readonly float X, Y, Z;
        internal Point(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    internal readonly struct Box
    {
        internal readonly Point Center, Size;
        internal readonly float Yaw;
        internal Box(Point center, Point size, float yaw = 0f) { Center = center; Size = size; Yaw = yaw; }
    }

    internal readonly struct Corner
    {
        internal readonly Point Center;
        internal readonly float Height;
        internal Corner(Point center, float height) { Center = center; Height = height; }
    }

    internal readonly struct Probe
    {
        internal readonly ArenaAreaVertex Inside, Outside;
        internal readonly int Edge;
        internal readonly float Along;
        internal Probe(ArenaAreaVertex inside, ArenaAreaVertex outside, int edge, float along)
        { Inside = inside; Outside = outside; Edge = edge; Along = along; }
    }

    internal sealed class MeshData
    {
        internal readonly IReadOnlyList<Point> Vertices;
        internal readonly IReadOnlyList<int> Triangles;
        internal MeshData(List<Point> vertices, List<int> triangles)
        { Vertices = vertices.AsReadOnly(); Triangles = triangles.AsReadOnly(); }
    }

    internal readonly IReadOnlyList<Box> Walls, CapCarvers;
    internal readonly IReadOnlyList<Corner> Corners;
    internal readonly IReadOnlyList<Probe> Probes;
    internal readonly MeshData Floor, Ceiling, Grid;
    internal readonly bool LegacyRectangle;
    internal int ColliderCount => Walls.Count + Corners.Count + 2;
    internal int ObstacleCount => Walls.Count + Corners.Count + CapCarvers.Count;

    private ArenaAreaEnclosurePlan(List<Box> walls, List<Box> caps, List<Corner> corners,
        List<Probe> probes, MeshData floor, MeshData ceiling, MeshData grid, bool rectangle)
    {
        Walls = walls.AsReadOnly(); CapCarvers = caps.AsReadOnly(); Corners = corners.AsReadOnly();
        Probes = probes.AsReadOnly(); Floor = floor; Ceiling = ceiling; Grid = grid; LegacyRectangle = rectangle;
    }

    internal static ArenaAreaEnclosurePlan Create(ArenaAreaBounds bounds)
    {
        if (!bounds.IsValid || bounds.Outline == null || bounds.Outline.Count < 3 || bounds.Outline.Count > 64)
            throw new InvalidOperationException("Arena enclosure requires a valid bounded polygon.");
        float sx = bounds.MaxX - bounds.MinX, sy = bounds.MaxY - bounds.MinY, sz = bounds.MaxZ - bounds.MinZ;
        if (sx > 2048f || sz > 2048f || sy > 256f
            || Math.Abs(bounds.MinX) > 100000f || Math.Abs(bounds.MaxX) > 100000f
            || Math.Abs(bounds.MinY) > 100000f || Math.Abs(bounds.MaxY) > 100000f
            || Math.Abs(bounds.MinZ) > 100000f || Math.Abs(bounds.MaxZ) > 100000f)
            throw new InvalidOperationException("Arena enclosure exceeds the supported coordinate/extent budget.");
        if (Math.Sqrt(CornerRadius * CornerRadius + EndOverlap * EndOverlap) >= ArenaAreaCatalog.NavigationInset)
            throw new InvalidOperationException("Arena wall overlap must stay inside the excluded navigation margin.");

        bool rectangle = IsAxisRectangle(bounds.Outline);
        var walls = new List<Box>();
        var corners = new List<Corner>();
        var probes = new List<Probe>();
        var gridVertices = new List<Point>();
        var gridTriangles = new List<int>();
        float middleY = (bounds.MinY + bounds.MaxY) / 2f;
        double perimeter = 0;
        for (int edge = 0; edge < bounds.Outline.Count; edge++)
        {
            var a = bounds.Outline[edge];
            var b = bounds.Outline[(edge + 1) % bounds.Outline.Count];
            double dx = b.X - a.X, dz = b.Z - a.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            perimeter += length;
            if (length <= 0 || perimeter > 8192)
                throw new InvalidOperationException("Arena enclosure exceeds the supported perimeter budget.");
            float ex = (float)(dx / length), ez = (float)(dz / length);
            float nx = -ez, nz = ex; // CCW interior is left of the edge in XZ.
            float shift = rectangle ? -Thickness / 2 : 0;
            walls.Add(new Box(new Point((a.X + b.X) / 2 + nx * shift, middleY, (a.Z + b.Z) / 2 + nz * shift),
                new Point((float)length + 2 * (rectangle ? Thickness : EndOverlap), sy + 2 * Thickness, Thickness),
                (float)(Math.Atan2(-ez, ex) * 180 / Math.PI)));
            if (!rectangle) corners.Add(new Corner(new Point(a.X, middleY, a.Z), sy + 2 * Thickness));

            int steps = Math.Max(1, (int)Math.Ceiling(length / ProbeSpacing));
            for (int step = 0; step <= steps; step++)
            {
                float along = (float)step / steps;
                AddProbe(probes, a.X + (b.X - a.X) * along, a.Z + (b.Z - a.Z) * along, nx, nz, edge, along);
            }
            // Extra corner/bisector probe, including reflex corners. Runtime
            // checks the resulting sampled points really lie on opposite sides.
            var previous = bounds.Outline[(edge + bounds.Outline.Count - 1) % bounds.Outline.Count];
            double px = a.X - previous.X, pz = a.Z - previous.Z;
            double plen = Math.Sqrt(px * px + pz * pz);
            double bx = nx - pz / plen, bz = nz + px / plen;
            double blen = Math.Sqrt(bx * bx + bz * bz);
            if (blen > 0.000001) AddProbe(probes, a.X, a.Z, (float)(bx / blen), (float)(bz / blen), edge, -1f);
            AddSideGrid(gridVertices, gridTriangles, a, b, bounds.MinY, bounds.MaxY);
        }

        // These two carvers preserve the previous vertical rejection of
        // basement/roof shortcuts. Their bounding footprints can include a
        // concave void, but ONLY below/above the playable height interval.
        // They have no collider or visual; both physical caps are clipped.
        var capCarvers = new List<Box>
        {
            new Box(new Point((bounds.MinX + bounds.MaxX)/2, bounds.MinY - Thickness/2, (bounds.MinZ + bounds.MaxZ)/2), new Point(sx + 2*Thickness, Thickness, sz + 2*Thickness)),
            new Box(new Point((bounds.MinX + bounds.MaxX)/2, bounds.MaxY + Thickness/2, (bounds.MinZ + bounds.MaxZ)/2), new Point(sx + 2*Thickness, Thickness, sz + 2*Thickness))
        };
        AddCapGrid(gridVertices, gridTriangles, bounds);
        return new ArenaAreaEnclosurePlan(walls, capCarvers, corners, probes,
            CreateCap(bounds, bounds.MinY - Thickness, bounds.MinY),
            CreateCap(bounds, bounds.MaxY, bounds.MaxY + Thickness),
            new MeshData(gridVertices, gridTriangles), rectangle);
    }

    private static bool IsAxisRectangle(IReadOnlyList<ArenaAreaVertex> outline)
    {
        if (outline.Count != 4) return false;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Count];
            if ((a.X == b.X) == (a.Z == b.Z)) return false;
        }
        return true; // The shared constructor already proved a simple valid polygon.
    }

    private static void AddProbe(List<Probe> probes, float x, float z, float nx, float nz, int edge, float along)
    {
        if (probes.Count >= MaxProbes) throw new InvalidOperationException("Arena enclosure exceeds the navigation probe budget.");
        probes.Add(new Probe(new ArenaAreaVertex(x + nx * ProbeOffset, z + nz * ProbeOffset),
            new ArenaAreaVertex(x - nx * ProbeOffset, z - nz * ProbeOffset), edge, along));
    }

    private static MeshData CreateCap(ArenaAreaBounds bounds, float low, float high)
    {
        int n = bounds.Outline.Count;
        var vertices = new List<Point>(2 * n);
        foreach (var p in bounds.Outline) vertices.Add(new Point(p.X, low, p.Z));
        foreach (var p in bounds.Outline) vertices.Add(new Point(p.X, high, p.Z));
        var triangles = new List<int>();
        // XZ-CCW triangles face -Y in Unity. Reverse only the upper cap.
        for (int i = 0; i < bounds.FloorTriangles.Count; i += 3)
        {
            int a = bounds.FloorTriangles[i], b = bounds.FloorTriangles[i + 1], c = bounds.FloorTriangles[i + 2];
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(c+n); triangles.Add(b+n); triangles.Add(a+n);
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            triangles.Add(i); triangles.Add(i+n); triangles.Add(next+n);
            triangles.Add(i); triangles.Add(next+n); triangles.Add(next);
        }
        return new MeshData(vertices, triangles);
    }

    private static void AddSideGrid(List<Point> vertices, List<int> triangles, ArenaAreaVertex a,
        ArenaAreaVertex b, float low, float high)
    {
        double dx = b.X-a.X, dz = b.Z-a.Z, length = Math.Sqrt(dx*dx+dz*dz);
        int horizontal = Math.Max(1, (int)Math.Ceiling(length/GridSpacing));
        int vertical = Math.Max(1, (int)Math.Ceiling((high-low)/GridSpacing));
        Point P(double t, float y) => new Point((float)(a.X+dx*t), y, (float)(a.Z+dz*t));
        for (int i=0; i<=horizontal; i++)
        {
            double center=(double)i/horizontal, half=GridWidth/(2*length);
            double first=Math.Max(0,center-half), last=Math.Min(1,center+half);
            Quad(vertices,triangles,P(first,low),P(last,low),P(last,high),P(first,high));
        }
        for (int i=0; i<=vertical; i++)
        {
            float center=low+(high-low)*i/vertical, first=Math.Max(low,center-GridWidth/2), last=Math.Min(high,center+GridWidth/2);
            Quad(vertices,triangles,P(0,first),P(1,first),P(1,last),P(0,last));
        }
    }

    private static void AddCapGrid(List<Point> vertices, List<int> triangles, ArenaAreaBounds bounds)
    {
        int nx=Math.Max(1,(int)Math.Ceiling((bounds.MaxX-bounds.MinX)/GridSpacing));
        int nz=Math.Max(1,(int)Math.Ceiling((bounds.MaxZ-bounds.MinZ)/GridSpacing));
        if ((long)(nx+nz+2)*(bounds.FloorTriangles.Count/3)>MaxCapClipOperations)
            throw new InvalidOperationException("Arena enclosure exceeds the clipped-grid work budget.");
        for (int axis=0; axis<2; axis++)
        {
            int count=axis==0?nx:nz;
            float min=axis==0?bounds.MinX:bounds.MinZ, max=axis==0?bounds.MaxX:bounds.MaxZ;
            for(int line=0;line<=count;line++)
            {
                float center=min+(max-min)*line/count;
                for(int i=0;i<bounds.FloorTriangles.Count;i+=3)
                {
                    var polygon=new List<ArenaAreaVertex> { bounds.Outline[bounds.FloorTriangles[i]], bounds.Outline[bounds.FloorTriangles[i+1]], bounds.Outline[bounds.FloorTriangles[i+2]] };
                    polygon=Clip(polygon,axis,center-GridWidth/2,true);
                    polygon=Clip(polygon,axis,center+GridWidth/2,false);
                    if(polygon.Count<3) continue;
                    for(int p=1;p<polygon.Count-1;p++)
                    {
                        var a=polygon[0];var b=polygon[p];var c=polygon[p+1];
                        if(Math.Abs((double)(b.X-a.X)*(c.Z-a.Z)-(double)(b.Z-a.Z)*(c.X-a.X))<0.0000001) continue;
                        CapTriangle(vertices,triangles,a,b,c,bounds.MinY+.01f);
                        CapTriangle(vertices,triangles,a,b,c,bounds.MaxY-.01f);
                    }
                }
            }
        }
    }

    private static List<ArenaAreaVertex> Clip(List<ArenaAreaVertex> source,int axis,float bound,bool greater)
    {
        var output=new List<ArenaAreaVertex>();
        if(source.Count==0)return output;
        double Value(ArenaAreaVertex p)=>axis==0?p.X:p.Z;
        var previous=source[source.Count-1];
        bool previousIn=greater?Value(previous)>=bound:Value(previous)<=bound;
        foreach(var current in source)
        {
            bool currentIn=greater?Value(current)>=bound:Value(current)<=bound;
            if(currentIn!=previousIn)
            {
                double t=(bound-Value(previous))/(Value(current)-Value(previous));
                output.Add(new ArenaAreaVertex((float)(previous.X+(current.X-previous.X)*t),(float)(previous.Z+(current.Z-previous.Z)*t)));
            }
            if(currentIn)output.Add(current);
            previous=current;previousIn=currentIn;
        }
        return output;
    }

    private static void CapTriangle(List<Point> vertices,List<int> triangles,ArenaAreaVertex a,ArenaAreaVertex b,ArenaAreaVertex c,float y)
    {
        Reserve(vertices,3);int n=vertices.Count;
        vertices.Add(new Point(a.X,y,a.Z));vertices.Add(new Point(b.X,y,b.Z));vertices.Add(new Point(c.X,y,c.Z));
        triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
        triangles.Add(n+2);triangles.Add(n+1);triangles.Add(n);
    }

    private static void Quad(List<Point> vertices,List<int> triangles,Point a,Point b,Point c,Point d)
    {
        Reserve(vertices,4);int n=vertices.Count;
        vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
        triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n);triangles.Add(n+2);triangles.Add(n+3);
        triangles.Add(n+2);triangles.Add(n+1);triangles.Add(n);triangles.Add(n+3);triangles.Add(n+2);triangles.Add(n);
    }

    private static void Reserve(List<Point> vertices,int count)
    {
        if(vertices.Count+count>MaxVertices)throw new InvalidOperationException("Arena enclosure exceeds the 16-bit grid mesh budget.");
    }
}
