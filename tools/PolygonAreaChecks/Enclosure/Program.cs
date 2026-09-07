using ArenaMode.Areas;
using ArenaMode.Shared;
using UnityEngine;
using UnityEngine.AI;
using Plan = ArenaMode.Areas.ArenaAreaEnclosurePlan;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("ASSERT: " + label); }
    private static bool Near(double a, double b, double tolerance = .0003) => Math.Abs(a - b) <= tolerance;
    private static ArenaAreaBounds Polygon(params (float x, float z)[] vertices)
        => new(vertices.Select(p => new ArenaAreaVertex(p.x, p.z)).ToArray(), 0, 12);

    public static int Main()
    {
        try
        {
            var rectangle = new ArenaAreaBounds(-24, -2, -16, 51, 12, 33);
            var concave = Polygon((0, 0), (30, 0), (30, 10), (10, 10), (10, 30), (0, 30));
            var oblique = Polygon((0, 0), (24, 7), (17, 29), (-8, 18));
            var acute = Polygon((0, 0), (50, 0), (2, 3), (0, 18));
            var collinear = Polygon((0, 0), (10, 0), (20, 0), (20, 20), (0, 20));
            Legacy(rectangle);
            foreach (var bounds in new[] { rectangle, concave, oblique, acute, collinear,
                ArenaAreaCatalog.FactorySouth.Bounds, ArenaAreaCatalog.CustomsFactoryCenter.Bounds })
            {
                var plan = Plan.Create(bounds);
                Walls(bounds, plan); Probes(bounds, plan); Caps(bounds, plan); Grid(bounds, plan);
                Console.WriteLine($"Enclosure coverage: edges={bounds.Outline.Count}, rectangle={plan.LegacyRectangle}, " +
                    $"colliders={plan.ColliderCount}, carvers={plan.ObstacleCount}, probes={plan.Probes.Count}, gridVertices={plan.Grid.Vertices.Count}");
            }
            var concavePlan = Plan.Create(concave);
            Check(!CoversXZ(concavePlan.Floor, 20, 20) && !CoversXZ(concavePlan.Ceiling, 20, 20), "concave cap void remains empty");
            Check(CoversXZ(concavePlan.Floor, 5, 20) && CoversXZ(concavePlan.Floor, 20, 5), "both concave arms have physical floor");
            Budgets(); Builder(concave); HeightUnlimited(concave); IntegrationSource();
            Console.WriteLine($"ARENA_POLYGON_ENCLOSURE_CONTRACTS_PASSED assertions={_checks}");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void Legacy(ArenaAreaBounds b)
    {
        var plan = Plan.Create(b);
        Check(plan.LegacyRectangle && plan.Walls.Count == 4 && plan.Corners.Count == 0
            && plan.ColliderCount == 6 && plan.ObstacleCount == 6, "legacy rectangle component counts");
        // Independent old six-box AABBs. Wall yaw is allowed to change the local
        // axes but not the original world volume or outside-only placement.
        float t = .6f;
        var expected = new[]
        {
            (b.MinX-t, b.MinZ-t, b.MinX, b.MaxZ+t),
            (b.MaxX, b.MinZ-t, b.MaxX+t, b.MaxZ+t),
            (b.MinX-t, b.MinZ-t, b.MaxX+t, b.MinZ),
            (b.MinX-t, b.MaxZ, b.MaxX+t, b.MaxZ+t)
        };
        foreach (var box in plan.Walls)
        {
            var corners = BoxCorners(box);
            Check(expected.Any(e => Near(corners.Min(p => p.x), e.Item1) && Near(corners.Min(p => p.z), e.Item2)
                && Near(corners.Max(p => p.x), e.Item3) && Near(corners.Max(p => p.z), e.Item4)), "legacy outside wall volume");
            Check(Near(box.Center.Y - box.Size.Y / 2, b.MinY - t)
                && Near(box.Center.Y + box.Size.Y / 2, b.MaxY + t), "legacy wall vertical overlap");
        }
        foreach (var box in plan.CapCarvers)
            Check(Near(box.Center.X - box.Size.X / 2, b.MinX - t)
                && Near(box.Center.X + box.Size.X / 2, b.MaxX + t)
                && Near(box.Center.Z - box.Size.Z / 2, b.MinZ - t)
                && Near(box.Center.Z + box.Size.Z / 2, b.MaxZ + t), "legacy cap carve footprint");
    }

    private static (double x, double z)[] BoxCorners(Plan.Box box)
    {
        double yaw = box.Yaw * Math.PI / 180, c = Math.Cos(yaw), s = Math.Sin(yaw);
        return new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) }
            .Select(v => (box.Center.X + c * v.Item1 * box.Size.X / 2 + s * v.Item2 * box.Size.Z / 2,
                box.Center.Z - s * v.Item1 * box.Size.X / 2 + c * v.Item2 * box.Size.Z / 2)).ToArray();
    }

    private static void Walls(ArenaAreaBounds b, Plan plan)
    {
        Check(plan.Walls.Count == b.Outline.Count, "one wall per polygon edge");
        Check(plan.Corners.Count == (plan.LegacyRectangle ? 0 : b.Outline.Count), "polygon corner closure count");
        for (int i = 0; i < b.Outline.Count; i++)
        {
            var a = b.Outline[i]; var end = b.Outline[(i + 1) % b.Outline.Count]; var box = plan.Walls[i];
            double dx = end.X - a.X, dz = end.Z - a.Z, length = Math.Sqrt(dx * dx + dz * dz);
            double yaw = box.Yaw * Math.PI / 180;
            Check(Near(Math.Cos(yaw), dx / length) && Near(-Math.Sin(yaw), dz / length), "edge wall yaw follows native local X");
            Check(box.Size.X > length && Near(box.Size.Z, .6), "wall endpoint overlap");
            for (int step = 0; step <= 24; step++)
            {
                double x = a.X + dx * step / 24, z = a.Z + dz * step / 24;
                Check(InBox(box, x, z, .0003), "every edge segment covered by physical wall");
            }
            if (!plan.LegacyRectangle)
            {
                var corner = plan.Corners[i];
                Check(Near(corner.Center.X, a.X) && Near(corner.Center.Z, a.Z), "corner centered on actual vertex");
                Check(InBox(box, a.X, a.Z) && InBox(plan.Walls[(i + b.Outline.Count - 1) % b.Outline.Count], a.X, a.Z), "corner joins both incident boxes");
                Check(Plan.CornerRadius < ArenaAreaCatalog.NavigationInset, "corner radius below reserved navigation inset");
                // The furthest box overlap past an endpoint is still inside
                // the 0.4m reserved contour margin; this is independent of angle.
                double extension = (box.Size.X - length) / 2;
                Check(Math.Sqrt(extension * extension + box.Size.Z * box.Size.Z / 4) < ArenaAreaCatalog.NavigationInset,
                    "oblique wall overlap below reserved navigation inset");
            }
        }
        // Dense independent body-space audit around the acute/reflex corners.
        // Agent-radius expansion by native carving is intentionally NOT modeled.
        for (double x = b.MinX; x <= b.MaxX; x += Math.Max(.25, (b.MaxX - b.MinX) / 70d))
            for (double z = b.MinZ; z <= b.MaxZ; z += Math.Max(.25, (b.MaxZ - b.MinZ) / 70d))
                if (InsideXZ(b.Outline, x, z) && BoundaryDistance(b.Outline, x, z) >= .4001)
                    Check(!plan.Walls.Any(w => InBox(w, x, z))
                        && !plan.Corners.Any(c => Math.Pow(x - c.Center.X, 2) + Math.Pow(z - c.Center.Z, 2) < .09),
                        "physical walls do not intrude into valid navigation clearance");
    }

    private static bool InBox(Plan.Box box, double x, double z, double tolerance = .00001)
    {
        double yaw = box.Yaw * Math.PI / 180, dx = x - box.Center.X, dz = z - box.Center.Z;
        return Math.Abs(Math.Cos(yaw) * dx - Math.Sin(yaw) * dz) <= box.Size.X / 2 + tolerance
            && Math.Abs(Math.Sin(yaw) * dx + Math.Cos(yaw) * dz) <= box.Size.Z / 2 + tolerance;
    }

    private static void Probes(ArenaAreaBounds b, Plan plan)
    {
        Check(plan.Probes.Count <= Plan.MaxProbes, "bounded probe count");
        for (int edge = 0; edge < b.Outline.Count; edge++)
        {
            var stations = plan.Probes.Where(p => p.Edge == edge && p.Along >= 0).OrderBy(p => p.Along).ToArray();
            var a = b.Outline[edge]; var next = b.Outline[(edge + 1) % b.Outline.Count];
            double dx = next.X - a.X, dz = next.Z - a.Z, length = Math.Sqrt(dx * dx + dz * dz);
            Check(stations.Length >= 2 && stations[0].Along == 0 && stations[^1].Along == 1, "edge probes include both endpoints");
            Check(plan.Probes.Any(p => p.Edge == edge && p.Along == -1), "corner bisector probe included");
            for (int i = 0; i < stations.Length; i++)
            {
                var p = stations[i];
                double mx = (p.Inside.X + p.Outside.X) / 2d, mz = (p.Inside.Z + p.Outside.Z) / 2d;
                Check(Near(mx, a.X + dx * p.Along) && Near(mz, a.Z + dz * p.Along), "probe station on actual contour");
                Check(Near(p.Inside.X - mx, -dz / length) && Near(p.Inside.Z - mz, dx / length), "probe inward normal follows CCW contour");
                if (i > 0) Check((p.Along - stations[i - 1].Along) * length <= 4.0001, "probe spacing bounded along every edge");
            }
        }
    }

    private static void Caps(ArenaAreaBounds b, Plan plan)
    {
        Check(plan.CapCarvers.Count == 2, "exactly two vertical cap carvers");
        Check(plan.CapCarvers[0].Center.Y + plan.CapCarvers[0].Size.Y / 2 <= b.MinY + .000001f
            && plan.CapCarvers[1].Center.Y - plan.CapCarvers[1].Size.Y / 2 >= b.MaxY - .000001f,
            "cap carvers do not enter playable height interval");
        double area = PolygonArea(b.Outline);
        foreach (var (mesh, low, high) in new[] { (plan.Floor, b.MinY - .6f, b.MinY), (plan.Ceiling, b.MaxY, b.MaxY + .6f) })
        {
            Check(mesh.Vertices.Count == 2 * b.Outline.Count, "cap prism uses actual outline");
            double bottomArea = 0, upperArea = 0, volume = 0;
            foreach (var (a, c, d) in Triangles(mesh))
            {
                var n = Cross(a, c, d);
                Check(n.x * n.x + n.y * n.y + n.z * n.z > .0000000001, "cap triangle has physical area");
                foreach (var p in new[] { a, c, d, Mix(a, c, d, 1d / 3, 1d / 3), Mix(a, c, d, .2, .6), Mix(a, c, d, .6, .2) })
                    Check(InsideXZ(b.Outline, p.X, p.Z) && p.Y >= low - .0001 && p.Y <= high + .0001, "physical cap clipped to polygon prism");
                if (Near(a.Y, low) && Near(c.Y, low) && Near(d.Y, low))
                { Check(n.y < 0, "lower cap winding points outward"); bottomArea += -n.y / 2; }
                else if (Near(a.Y, high) && Near(c.Y, high) && Near(d.Y, high))
                { Check(n.y > 0, "upper cap winding points outward"); upperArea += n.y / 2; }
                else
                {
                    double mx = (a.X + c.X + d.X) / 3d, mz = (a.Z + c.Z + d.Z) / 3d;
                    Check(BoundaryDistance(b.Outline, mx, mz) < .0003, "prism sides lie on contour");
                    var edge = Enumerable.Range(0, b.Outline.Count).First(i => SegmentDistance(b.Outline[i], b.Outline[(i + 1) % b.Outline.Count], mx, mz) < .0003);
                    var first = b.Outline[edge]; var last = b.Outline[(edge + 1) % b.Outline.Count];
                    Check(n.x * (last.Z - first.Z) - n.z * (last.X - first.X) > 0, "prism side winding points outward");
                }
                volume += (a.X * ((double)c.Y * d.Z - (double)c.Z * d.Y)
                    + a.Y * ((double)c.Z * d.X - (double)c.X * d.Z) + a.Z * ((double)c.X * d.Y - (double)c.Y * d.X)) / 6;
            }
            Check(Near(bottomArea, area, .02) && Near(upperArea, area, .02), "physical cap area covers exactly the polygon");
            Check(Near(volume, area * (high - low), .05), "closed cap prism has expected oriented volume");
        }
    }

    private static void Grid(ArenaAreaBounds b, Plan plan)
    {
        Check(plan.Grid.Vertices.Count > 0 && plan.Grid.Vertices.Count <= 60000, "grid stays below native 16-bit vertex limit");
        Check(plan.Grid.Triangles.All(i => i >= 0 && i < plan.Grid.Vertices.Count), "grid indices address bounded mesh");
        var faces = new HashSet<(int, int, int)>();
        for (int i = 0; i < plan.Grid.Triangles.Count; i += 3)
            faces.Add((plan.Grid.Triangles[i], plan.Grid.Triangles[i + 1], plan.Grid.Triangles[i + 2]));
        foreach (var face in faces) Check(faces.Contains((face.Item3, face.Item2, face.Item1)), "grid has explicit reverse faces");
        foreach (var (a, c, d) in Triangles(plan.Grid))
        {
            bool cap = Near(a.Y, c.Y, .000001) && Near(c.Y, d.Y, .000001);
            foreach (var p in new[] { a, c, d, Mix(a, c, d, 1d / 3, 1d / 3) })
                Check(InsideXZ(b.Outline, p.X, p.Z), "visible grid never fills concave void");
            if (cap)
                Check(InSingleStrip(a.X, c.X, d.X, b.MinX, b.MaxX) || InSingleStrip(a.Z, c.Z, d.Z, b.MinZ, b.MaxZ),
                    "cap grid triangle remains inside one thin grid strip");
            else
                Check(BoundaryDistance(b.Outline, (a.X + c.X + d.X) / 3d, (a.Z + c.Z + d.Z) / 3d) < .0003,
                    "side grid follows actual polygon edges");
        }
    }

    private static bool InSingleStrip(float a, float b, float c, float low, float high)
    {
        int count = Math.Max(1, (int)Math.Ceiling((high - low) / 3d));
        for (int i = 0; i <= count; i++)
        {
            double center = low + (double)(high - low) * i / count;
            if (Math.Abs(a - center) <= .0278 && Math.Abs(b - center) <= .0278 && Math.Abs(c - center) <= .0278) return true;
        }
        return false;
    }

    private static void Budgets()
    {
        Reject(default, "valid bounded polygon");
        Reject(new ArenaAreaBounds(0, 0, 0, 2049, 10, 20), "coordinate/extent budget");
        Reject(new ArenaAreaBounds(0, 0, 0, 20, 257, 20), "coordinate/extent budget");
        Reject(new ArenaAreaBounds(100001, 0, 0, 100021, 10, 20), "coordinate/extent budget");
        // Simple 64-gon: extents and perimeter are legal, but triangle-strip
        // clipping must refuse its otherwise quadratic work before mesh growth.
        var circle = Enumerable.Range(0, 64).Select(i => new ArenaAreaVertex((float)(900 * Math.Cos(i * Math.PI / 32)),
            (float)(900 * Math.Sin(i * Math.PI / 32)))).ToArray();
        Reject(new ArenaAreaBounds(circle, 0, 12), "clipped-grid work budget");
    }
    private static void Reject(ArenaAreaBounds b, string reason)
    {
        try { Plan.Create(b); Check(false, "excessive geometry rejected: " + reason); }
        catch (InvalidOperationException e) when (!e.Message.StartsWith("ASSERT:"))
        { Check(e.Message.Contains(reason, StringComparison.Ordinal), "clear geometry budget refusal: " + reason); }
    }

    private static void Builder(ArenaAreaBounds bounds)
    {
        var plan = Plan.Create(bounds);
        (GameObject, ArenaAreaEnclosure) Fresh()
        {
            var root = new GameObject("owned fixture root"); var enclosure = new ArenaAreaEnclosure(root, plan);
            enclosure.Build();
            Check(root.GetComponentsInChildren<MeshRenderer>().Single().gameObject.layer == 0, "visible boundary retains Default camera layer");
            Check(enclosure.Intact(), "new actual builder intact"); return (root, enclosure);
        }
        void Cleanup(GameObject root, ArenaAreaEnclosure enclosure)
        {
            var ownMeshes = root.GetComponentsInChildren<MeshCollider>().Select(c => c.sharedMesh)
                .Concat(root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh)).ToArray();
            var ownMaterials = root.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial).ToArray();
            root.SetActive(false); Check(!enclosure.Intact(), "disabled root immediately invalidates enclosure");
            UObject.Destroy(root); enclosure.Dispose(); enclosure.Dispose();
            Check(ownMeshes.All(m => m == null) && ownMaterials.All(m => m == null), "owned mesh and material cleanup");
        }
        var (go, owned) = Fresh();
        Check(go.GetComponentsInChildren<Collider>().Length == plan.ColliderCount
            && go.GetComponentsInChildren<NavMeshObstacle>().Length == plan.ObstacleCount, "actual builder uses plan component counts");
        Check(go.GetComponentsInChildren<MeshCollider>().Length == 2
            && go.GetComponentsInChildren<MeshCollider>().All(c => !c.convex), "actual cap collider never convexifies concave shape");
        Check(go.GetComponentsInChildren<Rigidbody>().Length == 0, "static nonconvex caps have no rigidbody");
        Check(go.GetComponentsInChildren<NavMeshObstacle>().Count(o => o.shape == NavMeshObstacleShape.Capsule) == bounds.Outline.Count,
            "actual builder creates corner capsule carvers");
        var caps = go.GetComponentsInChildren<NavMeshObstacle>().Where(o => o.gameObject.name.Contains("vertical cap carver")).ToArray();
        Check(caps.Length == 2 && caps.All(o => o.gameObject.GetComponentsInChildren<Collider>().Length == 0), "vertical carvers own no bounding-box physical caps");
        Cleanup(go, owned);
        var mutations = new (string label, Action<GameObject> action)[]
        {
            ("root moved", r => r.transform.position = new Vector3(1, 0, 0)),
            ("wall moved", r => r.GetComponentsInChildren<BoxCollider>()[0].transform.position = new Vector3(1, 0, 0)),
            ("wall rotated", r => r.GetComponentsInChildren<BoxCollider>()[0].transform.rotation = Quaternion.Euler(0, 12, 0)),
            ("wall scaled", r => r.GetComponentsInChildren<BoxCollider>()[0].transform.localScale = new Vector3(1, 2, 1)),
            ("wall reparented", r => r.GetComponentsInChildren<BoxCollider>()[0].transform.SetParent(new GameObject("foreign").transform, true)),
            ("collider disabled", r => r.GetComponentsInChildren<Collider>()[0].enabled = false),
            ("collider destroyed", r => UObject.Destroy(r.GetComponentsInChildren<Collider>()[0])),
            ("collider trigger", r => r.GetComponentsInChildren<Collider>()[0].isTrigger = true),
            ("collider resized", r => r.GetComponentsInChildren<BoxCollider>()[0].size = new Vector3(1, 1, 1)),
            ("collider layer", r => r.GetComponentsInChildren<Collider>()[0].gameObject.layer = 0),
            ("dynamic body", r => r.GetComponentsInChildren<Collider>()[0].attachedRigidbody = r.AddComponent<Rigidbody>()),
            ("corner direction", r => r.GetComponentsInChildren<CapsuleCollider>()[0].direction = 0),
            ("corner radius", r => r.GetComponentsInChildren<CapsuleCollider>()[0].radius = .1f),
            ("carver disabled", r => r.GetComponentsInChildren<NavMeshObstacle>()[0].enabled = false),
            ("carver not carving", r => r.GetComponentsInChildren<NavMeshObstacle>()[0].carving = false),
            ("carver moved", r => r.GetComponentsInChildren<NavMeshObstacle>()[^1].transform.position = new Vector3(1, 0, 0)),
            ("carver shape", r => r.GetComponentsInChildren<NavMeshObstacle>()[0].shape = NavMeshObstacleShape.Capsule),
            ("cap convex", r => r.GetComponentsInChildren<MeshCollider>()[0].convex = true),
            ("cap mesh removed", r => r.GetComponentsInChildren<MeshCollider>()[0].sharedMesh = null),
            ("visual disabled", r => r.GetComponentsInChildren<MeshRenderer>()[0].enabled = false),
            ("visual layer", r => r.GetComponentsInChildren<MeshRenderer>()[0].gameObject.layer = 12),
            ("visual mesh removed", r => r.GetComponentsInChildren<MeshFilter>()[0].sharedMesh = null)
        };
        foreach (var (label, action) in mutations)
        {
            (go, owned) = Fresh();
            var originalChildren = go.GetComponentsInChildren<Transform>().Where(t => !ReferenceEquals(t, go.transform)).Select(t => t.gameObject).ToArray();
            action(go); Check(!owned.Intact(), "integrity detects " + label); Cleanup(go, owned);
            Check(originalChildren.All(c => c == null), "detached owned children are still destroyed");
        }
        foreach (int failWrite in new[] { 1, 2, 3 })
        {
            int before = UObject.Created.Count; go = new GameObject("partial owned root"); owned = new ArenaAreaEnclosure(go, plan);
            Mesh.ThrowOnWrite = failWrite;
            try { owned.Build(); Check(false, "injected native setter escapes builder"); }
            catch (InvalidOperationException e) when (e.Message == "injected mesh setter failure") { }
            Check(!owned.Intact(), "partial builder cannot report ready");
            go.SetActive(false); UObject.Destroy(go); owned.Dispose();
            Check(UObject.Created.Skip(before).Where(o => o is Mesh or Material).All(o => o == null), "partial build retains all asset cleanup ownership");
        }
        Shader.Missing = true;
        go = new GameObject("missing shader"); owned = new ArenaAreaEnclosure(go, plan);
        try { owned.Build(); Check(false, "missing shader fails closed"); }
        catch (InvalidOperationException e) when (e.Message.Contains("shader")) { }
        Shader.Missing = false; go.SetActive(false); UObject.Destroy(go); owned.Dispose();
        Physics.IgnoreBodies = true;
        go = new GameObject("collision mismatch"); owned = new ArenaAreaEnclosure(go, plan);
        try { owned.Build(); Check(false, "incompatible collision matrix fails closed"); }
        catch (InvalidOperationException e) when (e.Message.Contains("collision matrix")) { }
        Check(go.GetComponentsInChildren<Collider>().Length == 0, "collision mismatch refuses before constructing barriers");
        Physics.IgnoreBodies = false; go.SetActive(false); UObject.Destroy(go); owned.Dispose();
    }

    private static void IntegrationSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ArenaMode.Client", "Areas", "ArenaAreaRuntime.cs"))) directory = directory.Parent;
        Check(directory != null, "runtime source available for integration contract");
        string source = File.ReadAllText(Path.Combine(directory.FullName, "ArenaMode.Client", "Areas", "ArenaAreaRuntime.cs"));
        Check(source.Contains("_enclosurePlan = ArenaAreaEnclosurePlan.Create(_area.Bounds)")
            && source.Contains("_enclosure = new ArenaAreaEnclosure(_root, _enclosurePlan)") && source.Contains("_enclosure.Build()"), "runtime consumes exact polygon plan and builder");
        Check(source.Contains("if (_area.Bounds.LimitsHeight)") && source.Contains("NavMesh.CalculateTriangulation()")
            && source.Contains("ArenaAreaEnclosurePlan.CreateForScene(_area.Bounds,")
            && source.Contains("navigation.vertices, navigation.indices, ToPoint, anchors"), "runtime consumes loaded scene heights for uncapped walls");
        Check(source.Contains("!_area.IsExcludedSpawn(pair.Key.Id)") && source.Contains("!_area.IsExcludedSpawn(point.Id)"),
            "client collection and readiness exclude area-specific sniper identity");
        Check(source.Contains("foreach (var probe in _enclosurePlan.Probes)")
            && source.Contains("Contains(a.position) && !Contains(b.position)"), "runtime consumes edge probes with correct sampled sides");
        Check(source.Contains("collider.transform.IsChildOf(_root.transform)"), "capture ignores only owned enclosure colliders");
        int reset = source.IndexOf("internal static void Reset()", StringComparison.Ordinal);
        Check(source.IndexOf("_root.SetActive(false)", reset, StringComparison.Ordinal) < source.IndexOf("_enclosure?.Dispose()", reset, StringComparison.Ordinal),
            "runtime disables root before deferred mesh destruction");
    }

    private static void HeightUnlimited(ArenaAreaBounds original)
    {
        var bounds = new ArenaAreaBounds(original.Outline, 0, 12, limitsHeight: false);
        Reject(bounds, "loaded navigation surface");
        // Two large triangles span the area without a single vertex inside it.
        // A separate high triangle elsewhere must not inflate local side walls.
        var vertices = new[] {
            new ArenaAreaPoint(-100, -8, -100), new ArenaAreaPoint(100, -8, -100), new ArenaAreaPoint(0, -8, 100),
            new ArenaAreaPoint(-100, 35, -100), new ArenaAreaPoint(100, 35, -100), new ArenaAreaPoint(0, 35, 100),
            new ArenaAreaPoint(1000, 900, 1000), new ArenaAreaPoint(1010, 900, 1000), new ArenaAreaPoint(1000, 900, 1010)
        };
        var indices = Enumerable.Range(0, vertices.Length).ToArray();
        Plan Scene(ArenaAreaPoint[] anchors) => Plan.CreateForScene(bounds, vertices, indices, p => p, anchors);
        var plan = Scene(Array.Empty<ArenaAreaPoint>());
        Check(Near(plan.SideMinY, -24) && Near(plan.SideMaxY, 51), "side walls span overlapping low and high surfaces without interior vertices");
        Check(plan.Floor == null && plan.Ceiling == null && plan.CapCarvers.Count == 0,
            "height unlimited plan has no physical or navigation caps");
        Check(plan.ColliderCount == bounds.Outline.Count * 2 && plan.ObstacleCount == plan.ColliderCount,
            "height unlimited component counts contain only side walls and corners");
        Check(plan.Walls.All(w => w.Center.Y-w.Size.Y/2 <= -24 && w.Center.Y+w.Size.Y/2 >= 51)
            && plan.Corners.All(c => c.Center.Y-c.Height/2 <= -24 && c.Center.Y+c.Height/2 >= 51),
            "all physical and navigation sides cover the scene height range");
        Check(plan.Grid.Vertices.All(p => p.Y >= -24 && p.Y <= 51)
            && plan.Grid.Vertices.Any(p => Near(p.Y, -24)) && plan.Grid.Vertices.Any(p => Near(p.Y, 51)),
            "side grid spans the same low and high surfaces");
        foreach (var (a, b, c) in Triangles(plan.Grid))
            Check(BoundaryDistance(bounds.Outline, (a.X+b.X+c.X)/3d, (a.Z+b.Z+c.Z)/3d) < .0003,
                "height unlimited grid contains only side faces");
        var withActors = Scene(new[] { new ArenaAreaPoint(5, -12, 5), new ArenaAreaPoint(5, 70, 5) });
        Check(Near(withActors.SideMinY, -28) && Near(withActors.SideMaxY, 86), "scene wall coverage also includes actual actor spawn heights");
        Check(bounds.Contains(5, -1000, 5) && bounds.Contains(5, 1000, 5)
            && !bounds.Contains(-1, 1000, 5), "finite wall coverage does not become a logical height limit");

        var root = new GameObject("height unlimited fixture"); var enclosure = new ArenaAreaEnclosure(root, plan);
        enclosure.Build();
        Check(enclosure.Intact() && root.GetComponentsInChildren<Collider>().Length == plan.ColliderCount,
            "height unlimited actual builder intact with expected side colliders");
        Check(root.GetComponentsInChildren<MeshCollider>().Length == 0
            && !root.GetComponentsInChildren<Transform>().Any(t => t.gameObject.name.Contains("vertical cap")),
            "height unlimited actual builder creates no floor ceiling or cap carver");
        var children = root.GetComponentsInChildren<Transform>().Select(t => t.gameObject).ToArray();
        var assets = root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
        root.GetComponentsInChildren<BoxCollider>()[0].enabled = false;
        Check(!enclosure.Intact(), "height unlimited integrity detects disabled side wall");
        root.SetActive(false); UObject.Destroy(root); enclosure.Dispose();
        Check(children.All(c => c == null) && assets.All(a => a == null), "height unlimited owned objects and grid cleaned up");

        void Refuse(Action action, string label)
        {
            try { action(); Check(false, label); }
            catch (InvalidOperationException e) when (!e.Message.StartsWith("ASSERT:")) { Check(true, label); }
        }
        Refuse(() => Plan.CreateForScene(bounds, vertices, new[] { -1, 1, 2 }, p => p, Array.Empty<ArenaAreaPoint>()), "invalid navigation indices rejected");
        Refuse(() => Plan.CreateForScene(bounds, vertices, new[] { 0, 1 }, p => p, Array.Empty<ArenaAreaPoint>()), "incomplete navigation triangles rejected");
        Refuse(() => Plan.CreateForScene(bounds, vertices, new[] { 6, 7, 8 }, p => p, Array.Empty<ArenaAreaPoint>()), "no overlapping navigation surface rejected");
        Refuse(() => Scene(new[] { new ArenaAreaPoint(5, float.NaN, 5) }), "nonfinite actor height rejected");
        Refuse(() => Scene(new[] { new ArenaAreaPoint(50, 1, 50) }), "outside actor anchor rejected");
        Refuse(() => Scene(new[] { new ArenaAreaPoint(5, 1000, 5) }), "excessive side mesh budget rejected instead of clipping height");
        var nonfinite = vertices.ToArray(); nonfinite[0] = new ArenaAreaPoint(0, float.PositiveInfinity, 0);
        Refuse(() => Plan.CreateForScene(bounds, nonfinite, indices, p => p, Array.Empty<ArenaAreaPoint>()), "nonfinite navigation vertex rejected");
        var current = ArenaAreaCatalog.CustomsFactoryCenterPolygon.Bounds;
        var actualNav = new[] { new ArenaAreaPoint(290, -6, -160), new ArenaAreaPoint(540, 32, -160), new ArenaAreaPoint(400, 2, 20) };
        var actual = Plan.CreateForScene(current, actualNav, new[] { 0, 1, 2 }, p => p, Array.Empty<ArenaAreaPoint>());
        Check(actual.Walls.Count == 15 && actual.Grid.Vertices.Count < Plan.MaxVertices && actual.Probes.Count < Plan.MaxProbes,
            "user contour side-only enclosure fits work budgets");
    }

    private static IEnumerable<(Plan.Point a, Plan.Point b, Plan.Point c)> Triangles(Plan.MeshData mesh)
    {
        for (int i = 0; i < mesh.Triangles.Count; i += 3)
            yield return (mesh.Vertices[mesh.Triangles[i]], mesh.Vertices[mesh.Triangles[i + 1]], mesh.Vertices[mesh.Triangles[i + 2]]);
    }
    private static (double x, double y, double z) Cross(Plan.Point a, Plan.Point b, Plan.Point c)
        => (((double)b.Y - a.Y) * (c.Z - a.Z) - ((double)b.Z - a.Z) * (c.Y - a.Y),
            ((double)b.Z - a.Z) * (c.X - a.X) - ((double)b.X - a.X) * (c.Z - a.Z),
            ((double)b.X - a.X) * (c.Y - a.Y) - ((double)b.Y - a.Y) * (c.X - a.X));
    private static Plan.Point Mix(Plan.Point a, Plan.Point b, Plan.Point c, double u, double v)
        => new((float)(a.X * (1 - u - v) + b.X * u + c.X * v), (float)(a.Y * (1 - u - v) + b.Y * u + c.Y * v), (float)(a.Z * (1 - u - v) + b.Z * u + c.Z * v));
    private static double PolygonArea(IReadOnlyList<ArenaAreaVertex> vertices)
        => Math.Abs(Enumerable.Range(0, vertices.Count).Sum(i => (double)vertices[i].X * vertices[(i + 1) % vertices.Count].Z
            - (double)vertices[i].Z * vertices[(i + 1) % vertices.Count].X)) / 2;
    private static double SegmentDistance(ArenaAreaVertex a, ArenaAreaVertex b, double x, double z)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double t = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / (dx * dx + dz * dz), 0, 1);
        return Math.Sqrt(Math.Pow(x - a.X - t * dx, 2) + Math.Pow(z - a.Z - t * dz, 2));
    }
    private static double BoundaryDistance(IReadOnlyList<ArenaAreaVertex> vertices, double x, double z)
        => Enumerable.Range(0, vertices.Count).Min(i => SegmentDistance(vertices[i], vertices[(i + 1) % vertices.Count], x, z));
    private static bool InsideXZ(IReadOnlyList<ArenaAreaVertex> vertices, double x, double z)
    {
        if (BoundaryDistance(vertices, x, z) < .0003) return true;
        bool inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            var a = vertices[i]; var b = vertices[j];
            if ((a.Z > z) != (b.Z > z) && x < (double)(b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
        }
        return inside;
    }
    private static bool CoversXZ(Plan.MeshData mesh, double x, double z)
    {
        foreach (var (a, b, c) in Triangles(mesh))
        {
            if (!Near(a.Y, b.Y) || !Near(b.Y, c.Y)) continue;
            double c1 = (b.X - a.X) * (z - a.Z) - (b.Z - a.Z) * (x - a.X);
            double c2 = (c.X - b.X) * (z - b.Z) - (c.Z - b.Z) * (x - b.X);
            double c3 = (a.X - c.X) * (z - c.Z) - (a.Z - c.Z) * (x - c.X);
            if (c1 >= 0 && c2 >= 0 && c3 >= 0 || c1 <= 0 && c2 <= 0 && c3 <= 0) return true;
        }
        return false;
    }
}
