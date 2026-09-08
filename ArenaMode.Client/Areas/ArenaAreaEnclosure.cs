using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Areas;

/// <summary>Owns the temporary components/assets of one polygon enclosure.</summary>
internal sealed class ArenaAreaEnclosure : IDisposable
{
    private sealed class TransformState
    {
        internal readonly Transform Transform, Parent;
        internal readonly string Name;
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal TransformState(Transform transform)
        { Transform = transform; Name = transform.gameObject.name; Parent = transform.parent; Position = transform.position; Rotation = transform.rotation; Scale = transform.localScale; }
        internal bool Intact(out string problem)
        {
            problem = null;
            if (Transform == null) return Fail(Name + ": transform missing", out problem);
            if (!Transform.gameObject.activeInHierarchy) return Fail(Name + ": inactive hierarchy", out problem);
            if (!ReferenceEquals(Transform.parent, Parent)) return Fail(Name + ": parent changed", out problem);
            if (!Same(Transform.position, Position))
                return Fail($"{Name}: position actual={Format(Transform.position)}, expected={Format(Position)}", out problem);
            if (!Same(Transform.localScale, Scale))
                return Fail($"{Name}: scale actual={Format(Transform.localScale)}, expected={Format(Scale)}", out problem);
            float angle = Quaternion.Angle(Transform.rotation, Rotation);
            if (!(angle <= .001f)) return Fail($"{Name}: rotation delta={Format(angle)} degrees, limit=0.001", out problem);
            return true;
        }
    }

    private sealed class ColliderState
    {
        internal Collider Collider;
        internal TransformState Transform;
        internal Vector3 Size;
        internal float Height;
        internal Mesh Mesh;
        internal int Vertices;
        internal Bounds MeshBounds;
    }

    private sealed class ObstacleState
    {
        internal NavMeshObstacle Obstacle;
        internal TransformState Transform;
        internal Vector3 Size;
    }

    private readonly GameObject _root;
    private readonly ArenaAreaEnclosurePlan _plan;
    private readonly List<ColliderState> _colliders = new();
    private readonly List<ObstacleState> _obstacles = new();
    private readonly List<Mesh> _meshes = new();
    private readonly List<GameObject> _objects = new();
    private TransformState _rootState, _visualState;
    private Material _material;
    private MeshRenderer _renderer;
    private MeshFilter _filter;
    private Mesh _grid;
    private int _layer;
    private bool _built;

    internal ArenaAreaEnclosure(GameObject root, ArenaAreaEnclosurePlan plan)
    { _root = root; _plan = plan; }

    internal void Build()
    {
        if (_built || _root == null) throw new InvalidOperationException("Arena enclosure builder has no fresh owned root.");
        _layer = LayerMask.NameToLayer("LowPolyCollider");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (_layer < 0 || playerLayer < 0 || Physics.GetIgnoreLayerCollision(_layer, playerLayer))
            throw new InvalidOperationException("the existing Player/LowPolyCollider collision matrix does not block bodies");
        _rootState = new TransformState(_root.transform);
        int index = 0;
        foreach (var wall in _plan.Walls) AddBox("edge " + index++, wall, true);
        index = 0;
        foreach (var corner in _plan.Corners) AddCorner("corner " + index++, corner);
        if (_plan.Floor != null) AddCap("floor", _plan.Floor);
        if (_plan.Ceiling != null) AddCap("ceiling", _plan.Ceiling);
        index = 0;
        foreach (var cap in _plan.CapCarvers) AddBox("vertical cap carver " + index++, cap, false);

        var shader = Shader.Find("Sprites/Default");
        if (shader == null) throw new InvalidOperationException("Sprites/Default boundary shader is unavailable");
        _material = new Material(shader) { name = "ArenaMode.AreaGrid", color = new Color(.15f, .85f, 1f, .42f) };
        _grid = CreateMesh("ArenaMode.AreaGrid", _plan.Grid);
        _grid.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), _plan.Grid.Vertices.Count).ToArray();
        var visual = Child("Visible boundary", Vector3.zero, Quaternion.identity);
        visual.layer = 0; // Preserve the original visible grid's Default camera layer.
        _filter = visual.AddComponent<MeshFilter>();
        _filter.sharedMesh = _grid;
        _renderer = visual.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _visualState = new TransformState(visual.transform);
        _built = true;
    }

    internal bool Intact() => Intact(out _);

    // Keep the first failed component/property so readiness and mid-match
    // failures cannot mislabel native dimension roundoff as a missing object.
    // Formatting only happens on failure, not on each successful actor tick.
    internal bool Intact(out string problem)
    {
        problem = null;
        if (!_built) return Fail("enclosure build incomplete or disposed", out problem);
        if (!_rootState.Intact(out problem)) return false;
        if (_colliders.Count != _plan.ColliderCount || _obstacles.Count != _plan.ObstacleCount)
            return Fail($"component counts: colliders={_colliders.Count}/{_plan.ColliderCount}, obstacles={_obstacles.Count}/{_plan.ObstacleCount}", out problem);
        if (_material == null || _grid == null) return Fail("visible boundary: material or mesh missing", out problem);
        if (_grid.vertexCount != _plan.Grid.Vertices.Count)
            return Fail($"visible boundary: vertices actual={_grid.vertexCount}, expected={_plan.Grid.Vertices.Count}", out problem);
        if (!_visualState.Intact(out problem)) return false;
        if (_renderer == null) return Fail("visible boundary: renderer missing", out problem);
        if (_renderer.sharedMaterial != _material || !_renderer.enabled || _renderer.gameObject.layer != 0)
            return Fail($"visible boundary: enabled={_renderer.enabled}, materialMatches={_renderer.sharedMaterial == _material}, layer={_renderer.gameObject.layer} (expected 0)", out problem);
        if (_filter == null || _filter.sharedMesh != _grid) return Fail("visible boundary: filter missing or mesh changed", out problem);
        foreach (var state in _colliders)
        {
            var collider = state.Collider;
            if (collider == null) return Fail(state.Transform.Name + ": collider missing", out problem);
            if (!state.Transform.Intact(out problem)) return false;
            if (!collider.enabled || collider.isTrigger || collider.gameObject.layer != _layer || collider.attachedRigidbody != null)
                return Fail($"{state.Transform.Name} collider: enabled={collider.enabled}, trigger={collider.isTrigger}, layer={collider.gameObject.layer} (expected {_layer}), rigidbody={collider.attachedRigidbody != null}", out problem);
            if (collider is BoxCollider box)
            {
                if (!Same(box.center, Vector3.zero) || !Same(box.size, state.Size))
                    return Fail($"{state.Transform.Name} box collider: center={Format(box.center)} (expected zero), size actual={Format(box.size)}, expected={Format(state.Size)}", out problem);
            }
            else if (collider is CapsuleCollider capsule)
            {
                if (!Same(capsule.center, Vector3.zero) || capsule.direction != 1
                    || !Same(capsule.radius, ArenaAreaEnclosurePlan.CornerRadius) || !Same(capsule.height, state.Height))
                    return Fail($"{state.Transform.Name} capsule collider: center={Format(capsule.center)} (expected zero), direction={capsule.direction} (expected 1), radius actual={Format(capsule.radius)}, expected={Format(ArenaAreaEnclosurePlan.CornerRadius)}, height actual={Format(capsule.height)}, expected={Format(state.Height)}", out problem);
            }
            else if (collider is MeshCollider cap)
            {
                if (cap.convex || state.Mesh == null || cap.sharedMesh != state.Mesh
                    || state.Mesh.vertexCount != state.Vertices || !Same(state.Mesh.bounds.center, state.MeshBounds.center)
                    || !Same(state.Mesh.bounds.size, state.MeshBounds.size))
                    return Fail($"{state.Transform.Name} cap collider: convex={cap.convex}, meshMissing={state.Mesh == null}, meshMatches={cap.sharedMesh == state.Mesh}; vertices actual={(state.Mesh == null ? -1 : state.Mesh.vertexCount)}, expected={state.Vertices}, bounds center actual={(state.Mesh == null ? "missing" : Format(state.Mesh.bounds.center))}, expected={Format(state.MeshBounds.center)}, size actual={(state.Mesh == null ? "missing" : Format(state.Mesh.bounds.size))}, expected={Format(state.MeshBounds.size)}", out problem);
            }
            else return Fail(state.Transform.Name + ": unsupported collider type", out problem);
        }
        foreach (var state in _obstacles)
        {
            var obstacle = state.Obstacle;
            if (obstacle == null) return Fail(state.Transform.Name + ": navigation obstacle missing", out problem);
            if (!state.Transform.Intact(out problem)) return false;
            if (!obstacle.enabled || !obstacle.carving || !obstacle.carveOnlyStationary
                || obstacle.carvingTimeToStationary != 0f || !Same(obstacle.center, Vector3.zero))
                return Fail($"{state.Transform.Name} navigation obstacle: enabled={obstacle.enabled}, carving={obstacle.carving}, stationaryOnly={obstacle.carveOnlyStationary} (expected all true), stationaryTime={Format(obstacle.carvingTimeToStationary)} (expected 0), center={Format(obstacle.center)} (expected zero)", out problem);
            if (obstacle.shape != NavMeshObstacleShape.Box || !Same(obstacle.size, state.Size))
                return Fail($"{state.Transform.Name} navigation box: shape={obstacle.shape} (expected Box), size actual={Format(obstacle.size)}, expected={Format(state.Size)}", out problem);
        }
        return true;
    }

    private GameObject Child(string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject("Arena boundary " + name) { layer = _layer };
        _objects.Add(go); // Retain ownership even if another component reparents it.
        go.transform.SetParent(_root.transform, false);
        go.transform.position = position;
        go.transform.rotation = rotation;
        return go;
    }

    private void AddBox(string name, ArenaAreaEnclosurePlan.Box box, bool physical)
    {
        var go = Child(name, Vector(box.Center), Quaternion.Euler(0f, box.Yaw, 0f));
        var size = Vector(box.Size);
        if (physical)
        {
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            _colliders.Add(new ColliderState { Collider = collider, Transform = new TransformState(go.transform), Size = size });
        }
        AddBoxObstacle(go, size);
    }

    private void AddCorner(string name, ArenaAreaEnclosurePlan.Corner corner)
    {
        var go = Child(name, Vector(corner.Center), Quaternion.identity);
        var collider = go.AddComponent<CapsuleCollider>();
        collider.direction = 1;
        collider.radius = ArenaAreaEnclosurePlan.CornerRadius;
        collider.height = corner.Height;
        _colliders.Add(new ColliderState { Collider = collider, Transform = new TransformState(go.transform), Height = corner.Height });
        // The installed game's capsule obstacle reports exactly half the height
        // assigned through its height API (07:04:53, 2026-09-08). Do not guess a
        // correction factor or adopt that readback as intended geometry. Use
        // the same box/size path as the verified side walls. This box contains
        // the physical capsule; at XZ diagonals it carves up to 0.125 m farther
        // from the vertex. Connected-spawn and whole-path checks stay strict.
        float diameter = 2f * ArenaAreaEnclosurePlan.CornerRadius;
        AddBoxObstacle(go, new Vector3(diameter, corner.Height, diameter));
    }

    private void AddBoxObstacle(GameObject go, Vector3 size)
    {
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = size;
        Configure(obstacle);
        _obstacles.Add(new ObstacleState { Obstacle = obstacle, Transform = new TransformState(go.transform), Size = size });
    }

    private void AddCap(string name, ArenaAreaEnclosurePlan.MeshData shape)
    {
        var go = Child(name, Vector3.zero, Quaternion.identity);
        var mesh = CreateMesh("ArenaMode.Area." + name, shape);
        var collider = go.AddComponent<MeshCollider>();
        collider.convex = false; // Static closed prism; convex cooking would fill concave voids.
        collider.sharedMesh = mesh;
        _colliders.Add(new ColliderState { Collider = collider, Transform = new TransformState(go.transform),
            Mesh = mesh, Vertices = shape.Vertices.Count, MeshBounds = mesh.bounds });
    }

    private Mesh CreateMesh(string name, ArenaAreaEnclosurePlan.MeshData shape)
    {
        var mesh = new Mesh { name = name };
        _meshes.Add(mesh); // Also own partially constructed meshes if a later setter throws.
        mesh.vertices = shape.Vertices.Select(Vector).ToArray();
        mesh.SetTriangles(shape.Triangles.ToArray(), 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Configure(NavMeshObstacle obstacle)
    {
        obstacle.carveOnlyStationary = true;
        obstacle.carvingTimeToStationary = 0f;
        obstacle.carving = true;
    }

    private static Vector3 Vector(ArenaAreaEnclosurePlan.Point point) => new Vector3(point.X, point.Y, point.Z);
    private static bool Same(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= .00000001f;
    // Allow bounded native dimension readback rounding, using the same absolute
    // 0.1-mm allowance as box dimensions. Compare with the plan, never adopt clamped geometry.
    // NaN/infinity differences fail the ordered comparison as well.
    private static bool Same(float a, float b) => Math.Abs(a - b) <= .0001f;
    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Format(Vector3 value) => $"({Format(value.x)},{Format(value.y)},{Format(value.z)})";
    private static bool Fail(string detail, out string problem) { problem = detail; return false; }

    public void Dispose()
    {
        _built = false;
        // Runtime deactivates the root synchronously before deferred Destroy.
        // A changed parent is an integrity failure, but cleanup must still
        // remove that owned object rather than leave a detached wall/carver.
        foreach (var go in _objects)
            if (go != null) { go.SetActive(false); UnityEngine.Object.Destroy(go); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        foreach (var mesh in _meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
        _material = null; _grid = null; _renderer = null; _filter = null;
        _meshes.Clear(); _colliders.Clear(); _obstacles.Clear(); _objects.Clear();
    }
}
