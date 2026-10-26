// Boundary doubles for ArenaPickupView.cs, which is linked unchanged. They model only what the
// constructor ordering depends on: object identity and destruction, the active state of a parent,
// and when native TMP runs Awake. No rendering is simulated.
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
    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<Component> Components = new();
        public Transform transform;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "")
        {
            this.name = name; transform = new() { gameObject = this };
            Components.Add(transform); All.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this }; Components.Add(c);
            // Unity delays Awake on a component added below an inactive parent until the parent is activated.
            if (activeInHierarchy && c is TMPro.TextMeshPro text) text.NativeAwake();
            return c;
        }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => Components.OfType<T>().Concat(transform.Children.SelectMany(t => t.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public static GameObject CreatePrimitive(PrimitiveType type)
        { var go = new GameObject("Cube"); go.AddComponent<Renderer>(); go.AddComponent<Collider>(); return go; }
        public void SetActive(bool active)
        {
            activeSelf = active;
            foreach (var text in GetComponentsInChildren<TMPro.TextMeshPro>(true))
                if (text.gameObject.activeInHierarchy) text.NativeAwake();
        }
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
    public class Camera : Component { }
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
    public class Shader : Object
    {
        public static readonly HashSet<string> Missing = new();
        public static Shader Find(string name) => Missing.Contains(name) ? null : new() { name = name };
    }
    public class Material : Object
    {
        public static readonly List<Material> All = new();
        public Color color;
        public Shader Shader;
        public Material(Shader shader) { Shader = shader; All.Add(this); }
    }
    public enum PrimitiveType { Cube }
    public struct Color(float r, float g, float b, float a = 1)
    {
        public float r = r, g = g, b = b, a = a;
        public static Color white => new(1, 1, 1);
    }
    public static class ColorUtility { public static string ToHtmlStringRGB(Color c) => "TEST"; }
    public readonly record struct Vector2(float x, float y);
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 one => new(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new(a.x * s, a.y * s, a.z * s);
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
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TextMeshPro : UnityEngine.Component
    {
        /// <summary>Makes the next text assignment throw, to model a late native failure in the constructor.</summary>
        public static bool FailText;
        private UnityEngine.Renderer _renderer;
        private object _font;
        private string _text;
        public bool Awakened { get; private set; }
        public void NativeAwake()
        {
            if (Awakened) return;
            Awakened = true;
            _renderer = gameObject.GetComponent<UnityEngine.Renderer>() ?? gameObject.AddComponent<UnityEngine.Renderer>();
        }
        // The installed TMP_Text.font setter calls LoadFontAsset, and TextMeshPro.LoadFontAsset writes its
        // cached renderer, which only Awake populates. This boundary throws a NullReferenceException when
        // production configures a label that has not awakened yet.
        public object font
        {
            get => _font;
            set
            {
                if (ReferenceEquals(_font, value)) return;
                _font = value;
                _renderer.receiveShadows = false;
            }
        }
        public float fontSize;
        public TextAlignmentOptions alignment;
        public UnityEngine.Color color;
        public bool richText, enableWordWrapping;
        public string text
        {
            get => _text;
            set => _text = FailText ? throw new InvalidOperationException("injected late label failure") : value;
        }
        public Rect rectTransform = new();
        public class Rect { public UnityEngine.Vector2 sizeDelta; }
    }
}
namespace EFT.CameraControl
{
    public class CameraManager
    {
        public static CameraManager Instance;
        public static bool Exist => Instance != null;
        public UnityEngine.Camera Camera;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger = new(); }
    public sealed class Logger { public List<string> Warnings = new(); public void LogWarning(string text) => Warnings.Add(text); }
}
namespace ArenaMode.Hud { public static class HudStyle { public static object Font = new(); } }
namespace ArenaMode.Modes
{
    public struct DominationPointState
    {
        public bool? OwnerUsec;
        public bool Contested, UnderAttack;
        public float CaptureProgress;
    }
    public class ArenaMatchService
    {
        public static ArenaMatchService Current;
        public object Mode;
    }
    // Only the mode/geometry query is a boundary; the real pickup view owns all visibility.
    public class TeamDeathmatchMode
    {
        public bool NearControlPoint;
        public UnityEngine.Vector3 LastFloor;
        public bool IsNearControlPoint(UnityEngine.Vector3 floor)
        { LastFloor = floor; return NearControlPoint; }
    }
}
namespace ArenaMode.Effects
{
    public static class ArenaTeamPalette
    {
        public static UnityEngine.Color UsecUi => new(.5f, .65f, 1f);
        public static UnityEngine.Color BearUi => new(1f, .42f, .42f);
    }
}
namespace ArenaMode.Pickups
{
    public enum ArenaPickupKind { DoubleDamage, Shield, Weapon, JumpBoots, InfiniteAmmo, Health }
    public enum ArenaPickupPhase { Available, Pending, Cooldown, Retired }
    public class ArenaPickupPoint { public string Id; public float Yaw; public ArenaPickupKind Kind; }
    public class ArenaPickupState { public ArenaPickupPhase Phase; public float ReadyAt; }
    public static class ArenaHealthPickup { public const float Amount = 150f; }
    // The real BeamLength probes physics; a fixed length keeps the beacon children in the tree.
    public static class ArenaPickupGeometry { public static float BeamLength(UnityEngine.Vector3 point) => 4f; }
}
