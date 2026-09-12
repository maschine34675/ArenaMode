using ArenaMode.Areas;
using ArenaMode.Shared;
using UnityEngine;
using UnityEngine.AI;
using Plan = ArenaMode.Areas.ArenaAreaEnclosurePlan;
using Visibility = ArenaMode.Areas.ArenaAreaGridVisibility;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string text)
    { ++_checks; if (!value) throw new InvalidOperationException("ASSERT: " + text); }
    public static int Main()
    {
        try
        {
            Distances(); ActualMeshAndBlockers(); UnlimitedSceneVisibility(); PolygonRanges(); RuntimeAdmission();
            Console.WriteLine($"ARENA_AREA_VISIBILITY_CONTRACTS_PASSED: {_checks} assertions; no Unity rendering or navigation executed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Distances()
    {
        var side = new Plan.GridSide(new(-100, 0), new(100, 0), 0, 4);
        Check(Visibility.SideAlpha(side, 0, 3) == 255, "near middle of long wall remains visible despite distant endpoints");
        Check(Visibility.SideAlpha(side, 0, 20) == 0, "distant wall is transparent");
        Check(Visibility.SideAlpha(side, 0, 6) == 255, "six meter wall reaches original alpha");
        byte half = Visibility.SideAlpha(side, 0, 13);
        Check(half is 127 or 128, "fade is linear halfway between the two distances");
        byte previous = 255;
        for (int distance = 0; distance <= 30; distance++)
        {
            var alpha = Visibility.SideAlpha(side, 0, distance);
            Check(alpha <= previous, "alpha never rises while retreating"); previous = alpha;
        }
        Check(Visibility.SideAlpha(side, 121, 0) == 0, "distance clamps to finite wall endpoints");
        Check(Visibility.SideAlpha(side, 100, 5) == 255, "corner remains visible");
        var diagonal = new Plan.GridSide(new(0, 0), new(100, 100), 0, 4);
        Check(Visibility.SideAlpha(diagonal, 50, 50) == 255, "oblique contour is measured against actual segment");
        Check(Visibility.SideAlpha(diagonal, 100, 0) == 0, "far diagonal is not treated as its AABB");
        Check(Visibility.SideAlpha(side, float.NaN, 0) == 255, "invalid observer leaves boundary warning visible");
        Check(Visibility.CapAlpha(0, 21) == 0 && Visibility.CapAlpha(0, 3) == 255, "legacy caps fade by height proximity");
    }

    private static void ActualMeshAndBlockers()
    {
        var bounds = new ArenaAreaBounds(-100, -50, -100, 100, 50, 100);
        var plan = Plan.Create(bounds);
        var root = new GameObject("visibility fixture");
        using var enclosure = new ArenaAreaEnclosure(root, plan);
        enclosure.Build();
        var mesh = root.GetComponentsInChildren<MeshFilter>().Single().sharedMesh;
        var physical = root.GetComponentsInChildren<Collider>();
        var navigation = root.GetComponentsInChildren<NavMeshObstacle>();
        var vertices = mesh.vertices;
        var indices = (int[])mesh.Indices.Clone();
        var blockers = physical.Select(c => c.GetHashCode()).ToArray();
        enclosure.UpdateVisibility(new Vector3(0, 0, 0), 0);
        Check(mesh.colors32.All(c => c.a == 0), "interior update hides actual mesh colors");
        Check(enclosure.Intact(), "transparent grid retains all enclosure invariants");
        Check(root.GetComponentsInChildren<MeshRenderer>().Single().enabled, "fade never disables renderer or hierarchy");
        Check(physical.All(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy), "fade never disables physical blockers");
        Check(navigation.All(c => c.enabled && c.carving && c.gameObject.activeInHierarchy), "fade never disables navigation carving");
        enclosure.UpdateVisibility(new Vector3(0, 0, -97), .01f);
        Check(mesh.colors32.All(c => c.a == 0), "visibility cadence prevents duplicate mesh updates");
        enclosure.UpdateVisibility(new Vector3(0, 0, -97), .2f);
        var near = plan.GridSides.Single(s => s.Start.Z == -100 && s.End.Z == -100);
        Check(mesh.colors32.Skip(near.FirstVertex).Take(near.VertexCount).All(c => c.a == 255), "approach reveals complete nearest side only");
        Check(plan.GridSides.Where(s => s.FirstVertex != near.FirstVertex)
            .All(s => mesh.colors32.Skip(s.FirstVertex).Take(s.VertexCount).All(c => c.a == 0)), "opposite distant sides stay hidden on approach");
        enclosure.UpdateVisibility(new Vector3(0, 20, -97), .4f);
        Check(mesh.colors32.Skip(near.FirstVertex).Take(near.VertexCount).All(c => c.a == 255), "side proximity ignores player height");
        enclosure.UpdateVisibility(new Vector3(0, 0, 0), .6f);
        Check(mesh.colors32.All(c => c.a == 0), "retreat hides previously visible sides again");
        enclosure.UpdateVisibility(null, .8f);
        Check(mesh.colors32.All(c => c.a == 255), "missing observer restores all warning surfaces");
        enclosure.UpdateVisibility(new Vector3(float.NaN, 0, 0), 1f);
        Check(mesh.colors32.All(c => c.a == 255), "invalid body position does not erase warning surfaces");
        Check(vertices.SequenceEqual(mesh.vertices) && indices.SequenceEqual(mesh.Indices), "visibility never rebuilds grid geometry");
        Check(blockers.SequenceEqual(root.GetComponentsInChildren<Collider>().Select(c => c.GetHashCode())), "visibility never replaces blockers");
        Check(enclosure.Intact(), "repeated fade cycles retain enclosure integrity");
        enclosure.Dispose();
        enclosure.UpdateVisibility(new Vector3(0, 0, 0), 2f);
        Check(!enclosure.Intact(), "disposed enclosure never becomes active from a visibility tick");
    }

    private static void UnlimitedSceneVisibility()
    {
        var bounds = new ArenaAreaBounds(new ArenaAreaVertex[] {
            new(-100,-100), new(100,-100), new(100,100), new(-100,100) }, 0, 12, limitsHeight: false);
        var navigation = new ArenaAreaPoint[] { new(-200, -8, -200), new(200, 35, -200), new(0, 0, 200) };
        var plan = Plan.CreateForScene(bounds, navigation, new[] { 0, 1, 2 }, p => p, Array.Empty<ArenaAreaPoint>());
        var root = new GameObject("unlimited scene visibility");
        using var enclosure = new ArenaAreaEnclosure(root, plan);
        enclosure.Build();
        var mesh = root.GetComponentsInChildren<MeshFilter>().Single().sharedMesh;
        Check(plan.Floor == null && plan.Ceiling == null && plan.SideGridVertexCount == mesh.vertices.Length,
            "unlimited scene fade owns only side vertices with no caps");
        enclosure.UpdateVisibility(new Vector3(0, -8, 0), 0);
        Check(mesh.colors32.All(c => c.a == 0) && enclosure.Intact(), "unlimited scene interior hides warning but preserves barriers");
        enclosure.UpdateVisibility(new Vector3(0, -8, -97), .2f);
        var lowColors = mesh.colors32.Select(c => c.a).ToArray();
        Check(lowColors.Any(a => a == 255) && lowColors.Any(a => a == 0), "unlimited scene reveals only approached sides");
        enclosure.UpdateVisibility(new Vector3(0, 35, -97), .4f);
        Check(lowColors.SequenceEqual(mesh.colors32.Select(c => c.a)) && enclosure.Intact(),
            "unlimited scene height changes leave side fade and barriers unchanged");
    }

    private static void PolygonRanges()
    {
        foreach (var bounds in new[] {
            new ArenaAreaBounds(-100, -50, -100, 100, 50, 100),
            new ArenaAreaBounds(new ArenaAreaVertex[] { new(0,0), new(30,0), new(30,10), new(10,10), new(10,30), new(0,30) }, 0, 12) })
        {
            var plan = Plan.Create(bounds);
            int offset = 0;
            foreach (var side in plan.GridSides)
            {
                Check(side.FirstVertex == offset && side.VertexCount > 0, "polygon side ranges are contiguous and nonempty");
                offset += side.VertexCount;
            }
            Check(offset == plan.SideGridVertexCount && offset <= plan.Grid.Vertices.Count, "side ranges cover exactly the side grid before caps");
            Check(plan.GridSides.Count == bounds.Outline.Count, "each concave or rectangular edge has its own fade range");
        }
    }

    private static void RuntimeAdmission()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(repo, "ArenaMode.Client/Areas/ArenaAreaRuntime.cs"));
        var tick = source.Substring(source.IndexOf("internal static void Tick()", StringComparison.Ordinal));
        int guard = tick.IndexOf("if (!IsCurrentActive() || _phase == Phase.Failed) return;", StringComparison.Ordinal);
        int update = tick.IndexOf("_enclosure?.UpdateVisibility(", StringComparison.Ordinal);
        Check(guard >= 0 && update > guard, "visibility call remains behind exact active-raid gate");
        Check(tick.Contains("_world.MainPlayer.Position"), "observer comes from the currently owned world");
    }
}
