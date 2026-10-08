// Boundary doubles only: the linked production AkimboView owns every action
// under test. This models immediate destruction; native end-of-frame destruction,
// rendering, layers, shader output and IK still require a Unity raid.
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed { get; private set; }
        public static readonly List<Object> Destructions = new();
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true; Destructions.Add(value);
            if (value is GameObject go)
            {
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components.ToArray()) Destroy(component);
            }
        }
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.Destroyed;
            bool b = ReferenceEquals(right, null) || right.Destroyed;
            return a || b ? a && b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public static implicit operator bool(Object value) => value != null;
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : Component
        {
            for (var cursor = transform; cursor != null; cursor = cursor.parent)
                if (cursor.GetComponent<T>() is { } found) return found;
            return null;
        }
    }
    public sealed class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        internal readonly List<Component> Components = new();
        public Transform transform { get; }
        public int layer;
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "")
        {
            this.name = name; transform = new Transform { gameObject = this, name = name };
            Components.Add(transform); All.Add(this);
        }
        public void SetActive(bool value) => activeSelf = value;
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this, name = name }; Components.Add(component); return component; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(x => x != null);
        public static GameObject CreatePrimitive(PrimitiveType type)
        { var go = new GameObject("Sphere"); go.AddComponent<MeshFilter>().sharedMesh = new Mesh(); go.AddComponent<MeshRenderer>(); go.AddComponent<Collider>(); return go; }
    }
    public sealed class Transform : Component
    {
        private Transform _parent;
        internal readonly List<Transform> Children = new();
        public Transform parent { get => _parent; set => SetParent(value, true); }
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public bool IsChildOf(Transform other)
        { for (var cursor = this; cursor != null; cursor = cursor.parent) if (ReferenceEquals(cursor, other)) return true; return false; }
        public Vector3 position { get => _parent == null ? localPosition : _parent.TransformPoint(localPosition); set => localPosition = _parent == null ? value : _parent.InverseTransformPoint(value); }
        public Quaternion rotation { get => _parent == null ? localRotation : _parent.rotation * localRotation; set => localRotation = _parent == null ? value : Quaternion.Inverse(_parent.rotation) * value; }
        public Vector3 lossyScale => _parent == null ? localScale : Vector3.Scale(_parent.lossyScale, localScale);
        public Vector3 TransformPoint(Vector3 point) => position + rotation * Vector3.Scale(lossyScale, point);
        public Vector3 InverseTransformPoint(Vector3 point)
        { var raw = Quaternion.Inverse(rotation) * (point - position); var s = lossyScale; return new(raw.x / s.x, raw.y / s.y, raw.z / s.z); }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
        public void SetParent(Transform next, bool worldPositionStays = true)
        {
            var p = position; var r = rotation; var s = lossyScale;
            _parent?.Children.Remove(this); _parent = next; next?.Children.Add(this);
            if (!worldPositionStays) return;
            position = p; rotation = r;
            var ps = next == null ? Vector3.one : next.lossyScale;
            localScale = new(s.x / ps.x, s.y / ps.y, s.z / ps.z);
        }
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        internal NVector3 Native => new(x, y, z);
        internal static Vector3 From(NVector3 value) => new(value.X, value.Y, value.Z);
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => From(a.Native + b.Native);
        public static Vector3 operator -(Vector3 a, Vector3 b) => From(a.Native - b.Native);
        public static Vector3 operator *(Vector3 a, float b) => From(a.Native * b);
        public static Vector3 Scale(Vector3 a, Vector3 b) => From(a.Native * b.Native);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => From(NVector3.Lerp(a.Native, b.Native, Math.Clamp(t, 0, 1)));
    }
    public struct Quaternion
    {
        internal NQuaternion Native;
        private static Quaternion From(NQuaternion value) => new() { Native = value };
        public static Quaternion identity => From(NQuaternion.Identity);
        public static Quaternion Inverse(Quaternion value) => From(NQuaternion.Inverse(value.Native));
        public static Quaternion Euler(float x, float y, float z) => From(NQuaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static Quaternion operator *(Quaternion a, Quaternion b) => From(a.Native * b.Native);
        public static Vector3 operator *(Quaternion a, Vector3 b) => Vector3.From(NVector3.Transform(b.Native, a.Native));
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => From(NQuaternion.Slerp(a.Native, b.Native, Math.Clamp(t, 0, 1)));
    }
    public class Renderer : Component
    {
        private bool _off;
        public int VisibilityWrites { get; private set; }
        public bool forceRenderingOff { get => _off; set { VisibilityWrites++; _off = value; } }
        public bool enabled = true;
        public bool isVisible => !forceRenderingOff && enabled && gameObject.activeInHierarchy;
        public Material[] sharedMaterials = Array.Empty<Material>();
        public Material sharedMaterial { get => sharedMaterials.FirstOrDefault(); set => sharedMaterials = new[] { value }; }
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public int lightProbeUsage, reflectionProbeUsage;
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class SkinnedMeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class Mesh : Object { }
    public sealed class Collider : Component { public bool enabled = true; }
    public sealed class Shader : Object
    {
        public static bool Available = true;
        public static Shader Find(string name) => Available ? new Shader { name = name } : null;
    }
    public sealed class Material(Shader shader) : Object { public Shader Shader = shader; public Color color; }
    public struct Color(float r, float g, float b, float a) { public float r = r, g = g, b = b, a = a; }
    public enum PrimitiveType { Sphere }
    public static class Time { public static int frameCount; public static float time; public static float deltaTime = .016f; }
    public static class Mathf { public static float Exp(float x) => MathF.Exp(x); }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off, On } }
namespace EFT.AssetsManager
{
    public class AssetPoolObject : UnityEngine.Component { public bool IsInPool; }
    public sealed class AmmoPoolObject : AssetPoolObject { }
}
namespace EFT
{
    public enum ECharacterWeaponBones { Weapon_root }
    public sealed class TestHierarchy
    {
        public UnityEngine.Transform Root;
        public UnityEngine.Transform GetTransform(ECharacterWeaponBones bone) => Root;
    }
    public sealed class TestFireport { public UnityEngine.Transform Original; }
    public sealed class Player
    {
        public sealed class FirearmController
        {
            public TestHierarchy HandsHierarchy = new();
            public TestFireport CurrentFireport = new();
        }
    }
}
