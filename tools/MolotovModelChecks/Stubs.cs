using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public virtual string name { get; set; }
        public bool Destroyed;
        public static void Destroy(Object o)
        {
            if (o == null || o.Destroyed) return;
            o.Destroyed = true;
            if (o is GameObject g) foreach (var c in g.Components.ToArray()) Destroy(c);
            else o.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(o, null);
        }
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public GameObject(string name = "object") { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool all) where T : class => Components.OfType<T>().Concat(transform.Children.SelectMany(x => x.gameObject.GetComponentsInChildren<T>(all))).ToArray();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public override string name { get => gameObject?.name; set { if (gameObject != null) gameObject.name = value; } }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool all) where T : class => gameObject.GetComponentsInChildren<T>(all);
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform value) { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
    }
    public class MeshFilter : Component
    {
        private Mesh _mesh;
        public bool FailNextAssignment;
        public Mesh sharedMesh { get=>_mesh; set { if(FailNextAssignment) { FailNextAssignment=false;throw new Exception("injected native mesh setter failure"); } _mesh=value; } }
    }
    public class MeshRenderer : Component
    {
        public bool enabled = true;
        public Material[] sharedMaterials = Array.Empty<Material>();
    }
    public class Shader : Object
    {
        public static bool Missing;
        public static Shader Find(string name) => Missing ? null : new Shader { name = name };
    }
    public class Material : Object
    {
        public readonly Shader shader;
        public Color color;
        public readonly Dictionary<string, float> Values = new();
        public Material(Shader shader) { this.shader = shader; }
        public bool HasProperty(string property) => true;
        public void SetFloat(string property, float value) => Values[property] = value;
    }
    public class Mesh : Object
    {
        public int subMeshCount;
        public Bounds bounds;
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector2> Uv = new();
        public readonly Dictionary<int, List<int>> Triangles = new();
        public void SetVertices(List<Vector3> vertices) { Vertices.Clear(); Vertices.AddRange(vertices); }
        public void SetUVs(int channel, List<Vector2> uv) { Uv.Clear(); Uv.AddRange(uv); }
        public void SetTriangles(List<int> triangles, int submesh) => Triangles[submesh] = new List<int>(triangles);
        public void RecalculateNormals() { }
        public void RecalculateBounds()
        {
            var low = new Vector3(Vertices.Min(x => x.x), Vertices.Min(x => x.y), Vertices.Min(x => x.z));
            var high = new Vector3(Vertices.Max(x => x.x), Vertices.Max(x => x.y), Vertices.Max(x => x.z));
            bounds = new Bounds { center = (low + high) * .5f, extents = (high - low) * .5f };
        }
    }
    public struct Bounds { public Vector3 center, extents; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float k) => new(a.x*k,a.y*k,a.z*k);
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Quaternion { }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1) { this.r=r; this.g=g; this.b=b; this.a=a; } }
    public static class Mathf { public const float PI = MathF.PI; public static float Cos(float x)=>MathF.Cos(x); public static float Sin(float x)=>MathF.Sin(x); }
}
namespace EFT.InventoryLogic { public class Item { public string TemplateId; } }
namespace EFT.AssetsManager
{
    public class AssetPoolObject : UnityEngine.MonoBehaviour
    {
        public bool IsInPool, IsStub;
        public virtual void ReturnToPool() { IsInPool=true; }
        public virtual void OnGetFromPool() { IsInPool=false; }
    }
}
public class TransformLinks : UnityEngine.MonoBehaviour { }
public class GrenadePrefab : EFT.AssetsManager.AssetPoolObject { public TransformLinks Hierarchy; }
namespace EFT
{
    public class Grenade : UnityEngine.MonoBehaviour { public EFT.InventoryLogic.Item WeaponSource; }
    public class ObjectsFactory
    {
        public Task<UnityEngine.GameObject> CreateItemAsync(EFT.InventoryLogic.Item item, int camera, object player, bool animated, object yield, CancellationToken cancellation) => null;
    }
    public class Player
    {
        public class BaseGrenadeHandsController
        {
            public Grenade CreateAndRegisterGrenade(UnityEngine.Vector3 p,UnityEngine.Quaternion q,UnityEngine.Vector3 f,float warm=0) => null;
        }
    }
}
namespace ArenaMode.Session { internal static class ArenaSession { public static bool IsActive, ArmedForNextRaid; } }
namespace ArenaMode
{
    internal static class Plugin { public static readonly Log ModLogger = new(); }
    internal class Log { public void LogWarning(string message) { } }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static List<MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).ToList();
        public static MethodInfo Method(Type t,string name,Type[] p)=>t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,p,null);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { if(GetTargetMethod()==null) throw new Exception("patch missing"); } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute:Attribute { }
}

namespace ArenaMode.Combat.Fire { internal static class ArenaFireSupply { internal static bool HasVerifiedTemplate=true; } }
