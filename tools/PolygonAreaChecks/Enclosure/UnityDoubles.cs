// Purpose-limited state doubles for the ACTUAL builder. No PhysX, mesh cooking,
// navmesh carving, renderer, scene or engine scheduling is simulated here.
namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> Created = new();
        internal bool Destroyed;
        public string name;
        public Object() => Created.Add(this);
        internal bool Gone => Destroyed || this is Component c && c.gameObject.Destroyed;
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            if (value is GameObject go)
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Gone, bn = ReferenceEquals(b, null) || b.Gone;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public readonly Transform transform;
        public int layer;
        private bool _active = true;
        public bool activeInHierarchy => !Destroyed && _active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; _components.Add(transform); }
        public void SetActive(bool active) => _active = active;
        public T AddComponent<T>() where T : Component, new()
        { var value = new T { gameObject = this }; _components.Add(value); return value; }
        public T[] GetComponentsInChildren<T>() where T : Component
            => _components.OfType<T>().Concat(transform.Children.SelectMany(c => c.gameObject.GetComponentsInChildren<T>())).ToArray();
    }
    public class Component : Object
    { public GameObject gameObject; public Transform transform => gameObject.transform; }
    public class Behaviour : Component { public bool enabled = true; }
    public sealed class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform parent;
        public Vector3 position, localScale = new(1, 1, 1);
        public Quaternion rotation = Quaternion.identity;
        public void SetParent(Transform value, bool worldPositionStays)
        { parent?.Children.Remove(this); parent = value; value?.Children.Add(this); }
        public bool IsChildOf(Transform value) => ReferenceEquals(this, value) || parent != null && parent.IsChildOf(value);
    }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public readonly struct Quaternion
    {
        private readonly System.Numerics.Quaternion _value;
        private Quaternion(System.Numerics.Quaternion value) => _value = value;
        public static Quaternion identity => new(System.Numerics.Quaternion.Identity);
        public static Quaternion Euler(float x, float y, float z)
            => new(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static float Angle(Quaternion a, Quaternion b)
        {
            if (a._value == b._value) return 0;
            float dot = Math.Min(1f, Math.Abs(System.Numerics.Quaternion.Dot(a._value, b._value)));
            return MathF.Acos(dot) * 360 / MathF.PI;
        }
    }
    public readonly struct Bounds
    {
        public readonly Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }
    public class Rigidbody : Component { }
    public class Collider : Behaviour { public bool isTrigger; public Rigidbody attachedRigidbody; }
    public sealed class BoxCollider : Collider { public Vector3 center; public Vector3 size = new(1, 1, 1); }
    public sealed class CapsuleCollider : Collider { public Vector3 center; public float radius = .5f, height = 2; public int direction = 1; }
    public sealed class MeshCollider : Collider { public bool convex; public Mesh sharedMesh; }
    public static class LayerMask { public static int NameToLayer(string name) => name == "LowPolyCollider" ? 12 : name == "Player" ? 8 : -1; }
    public static class Physics
    { public static bool IgnoreBodies; public static bool GetIgnoreLayerCollision(int first, int second) => IgnoreBodies; }
    public sealed class Shader : Object
    { public static bool Missing; public static Shader Find(string name) => Missing ? null : new Shader { name = name }; }
    public readonly struct Color { public Color(float r, float g, float b, float a) { } }
    public readonly struct Color32 { public Color32(byte r, byte g, byte b, byte a) { } }
    public sealed class Material : Object { public Color color; public Material(Shader shader) { } }
    public sealed class Mesh : Object
    {
        internal static int ThrowOnWrite;
        private Vector3[] _vertices = Array.Empty<Vector3>();
        public Vector3[] vertices
        {
            get => (Vector3[])_vertices.Clone();
            set
            {
                _vertices = (Vector3[])value.Clone();
                if (ThrowOnWrite > 0 && --ThrowOnWrite == 0) throw new InvalidOperationException("injected mesh setter failure");
            }
        }
        public int vertexCount => _vertices.Length;
        public int[] Indices = Array.Empty<int>();
        public Color32[] colors32;
        public Bounds bounds;
        public void SetTriangles(int[] values, int submesh)
        {
            if (values.Any(i => i < 0 || i >= vertexCount)) throw new ArgumentException("invalid mesh index");
            Indices = (int[])values.Clone();
        }
        public void RecalculateBounds()
        {
            float minX = _vertices.Min(v => v.x), minY = _vertices.Min(v => v.y), minZ = _vertices.Min(v => v.z);
            float maxX = _vertices.Max(v => v.x), maxY = _vertices.Max(v => v.y), maxZ = _vertices.Max(v => v.z);
            bounds = new Bounds(new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2), new Vector3(maxX - minX, maxY - minY, maxZ - minZ));
        }
    }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class MeshRenderer : Behaviour
    { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows; }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off, On } }
namespace UnityEngine.AI
{
    public enum NavMeshObstacleShape { Capsule, Box }
    public sealed class NavMeshObstacle : UnityEngine.Behaviour
    {
        public NavMeshObstacleShape shape;
        public UnityEngine.Vector3 center, size;
        public float radius, carvingTimeToStationary;
        private float _height;
        // Reproduce the observed SPT getter/setter mismatch only. This does
        // not claim to model its internal storage or actual carving geometry.
        public float height { get => _height * .5f; set => _height = value; }
        public bool carving, carveOnlyStationary;
    }
}
