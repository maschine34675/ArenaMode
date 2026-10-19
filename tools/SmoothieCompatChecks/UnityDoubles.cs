// Models owned object identity, layers and camera masks, not native rendering or occlusion.
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            if (value is GameObject go)
            {
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components) component.Destroyed = true;
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<Component> Components = new();
        public Transform transform;
        public int layer;
        public bool activeSelf = true;
        public GameObject(string name = "")
        {
            this.name = name; transform = new() { gameObject = this };
            Components.Add(transform); All.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => Components.OfType<T>().Concat(transform.Children.SelectMany(t => t.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public static GameObject CreatePrimitive(PrimitiveType type)
        { var go = new GameObject("Cube"); go.AddComponent<Renderer>(); go.AddComponent<Collider>(); return go; }
        public void SetActive(bool active) => activeSelf = active;
    }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform parent;
        public Vector3 position, localPosition, localScale;
        public Quaternion rotation, localRotation;
        public void SetParent(Transform target, bool world)
        { parent?.Children.Remove(this); parent = target; parent?.Children.Add(this); }
    }
    public class Camera : Component { public int cullingMask = -1; }
    public class Collider : Component { public bool enabled = true; }
    public class Renderer : Component
    { public bool enabled = true, receiveShadows; public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class LineRenderer : Renderer
    {
        public bool useWorldSpace, loop;
        public int positionCount;
        public float startWidth, endWidth;
        public Color startColor, endColor;
        public void SetPosition(int index, Vector3 position) { }
    }
    public class Shader : Object { public static Shader Find(string name) => new() { name = name }; }
    public class Material(Shader shader) : Object { public Color color; public Shader Shader = shader; }
    public enum PrimitiveType { Cube }
    public struct Color(float r, float g, float b, float a = 1)
    {
        public float r = r, g = g, b = b, a = a;
        public static Color white => new(1, 1, 1);
    }
    public readonly record struct Vector2(float x, float y);
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 one => new(1, 1, 1);
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator *(Vector3 a, float s) => new(a.x*s, a.y*s, a.z*s);
    }
    public readonly record struct Quaternion(float x, float y, float z)
    { public static Quaternion Euler(float x, float y, float z) => new(x, y, z); }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Cos(float f) => MathF.Cos(f);
        public static float Sin(float f) => MathF.Sin(f);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int CeilToInt(float f) => (int)MathF.Ceiling(f);
    }
    public static class Time { public static float time, unscaledTime; }
    public static class LayerMask
    {
        public static Dictionary<string, int> Layers = new() { ["Loot"] = 11, ["Interactive"] = 13 };
        public static int NameToLayer(string name) => Layers.GetValueOrDefault(name, -1);
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TextMeshPro : UnityEngine.Component
    {
        public object font;
        public float fontSize;
        public TextAlignmentOptions alignment;
        public UnityEngine.Color color;
        public bool richText, enableWordWrapping;
        public string text;
        public Rect rectTransform = new();
        public class Rect { public UnityEngine.Vector2 sizeDelta; }
    }
}
