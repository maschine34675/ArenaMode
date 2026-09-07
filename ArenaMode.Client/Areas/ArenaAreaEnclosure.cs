using System;
using System.Collections.Generic;
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
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal TransformState(Transform transform)
        { Transform = transform; Parent = transform.parent; Position = transform.position; Rotation = transform.rotation; Scale = transform.localScale; }
        internal bool Intact => Transform != null && Transform.gameObject.activeInHierarchy
            && ReferenceEquals(Transform.parent, Parent)
            && (Transform.position - Position).sqrMagnitude <= .00000001f
            && (Transform.localScale - Scale).sqrMagnitude <= .00000001f
            && Quaternion.Angle(Transform.rotation, Rotation) <= .001f;
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
        internal float Height;
        internal bool Capsule;
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
        AddCap("floor", _plan.Floor);
        AddCap("ceiling", _plan.Ceiling);
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

    internal bool Intact()
    {
        if (!_built || !_rootState.Intact || _colliders.Count != _plan.ColliderCount
            || _obstacles.Count != _plan.ObstacleCount || _material == null || _grid == null
            || _grid.vertexCount != _plan.Grid.Vertices.Count || !_visualState.Intact
            || _renderer == null || !_renderer.enabled || _renderer.sharedMaterial != _material
            || _renderer.gameObject.layer != 0
            || _filter == null || _filter.sharedMesh != _grid) return false;
        foreach (var state in _colliders)
        {
            var collider = state.Collider;
            if (collider == null || !collider.enabled || collider.isTrigger || !state.Transform.Intact
                || collider.gameObject.layer != _layer || collider.attachedRigidbody != null) return false;
            if (collider is BoxCollider box)
            {
                if (!Same(box.center, Vector3.zero) || !Same(box.size, state.Size)) return false;
            }
            else if (collider is CapsuleCollider capsule)
            {
                if (!Same(capsule.center, Vector3.zero) || capsule.direction != 1
                    || capsule.radius != ArenaAreaEnclosurePlan.CornerRadius || capsule.height != state.Height) return false;
            }
            else if (collider is MeshCollider cap)
            {
                if (cap.convex || state.Mesh == null || cap.sharedMesh != state.Mesh
                    || state.Mesh.vertexCount != state.Vertices || !Same(state.Mesh.bounds.center, state.MeshBounds.center)
                    || !Same(state.Mesh.bounds.size, state.MeshBounds.size)) return false;
            }
            else return false;
        }
        foreach (var state in _obstacles)
        {
            var obstacle = state.Obstacle;
            if (obstacle == null || !obstacle.enabled || !obstacle.carving || !obstacle.carveOnlyStationary
                || obstacle.carvingTimeToStationary != 0f || !state.Transform.Intact
                || !Same(obstacle.center, Vector3.zero)) return false;
            if (state.Capsule)
            {
                if (obstacle.shape != NavMeshObstacleShape.Capsule || obstacle.radius != ArenaAreaEnclosurePlan.CornerRadius
                    || obstacle.height != state.Height) return false;
            }
            else if (obstacle.shape != NavMeshObstacleShape.Box || !Same(obstacle.size, state.Size)) return false;
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
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = size;
        Configure(obstacle);
        _obstacles.Add(new ObstacleState { Obstacle = obstacle, Transform = new TransformState(go.transform), Size = size });
    }

    private void AddCorner(string name, ArenaAreaEnclosurePlan.Corner corner)
    {
        var go = Child(name, Vector(corner.Center), Quaternion.identity);
        var collider = go.AddComponent<CapsuleCollider>();
        collider.direction = 1;
        collider.radius = ArenaAreaEnclosurePlan.CornerRadius;
        collider.height = corner.Height;
        _colliders.Add(new ColliderState { Collider = collider, Transform = new TransformState(go.transform), Height = corner.Height });
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = ArenaAreaEnclosurePlan.CornerRadius;
        obstacle.height = corner.Height;
        Configure(obstacle);
        _obstacles.Add(new ObstacleState { Obstacle = obstacle, Transform = new TransformState(go.transform), Capsule = true, Height = corner.Height });
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
