// Explicit native boundaries; production manager/registry/adapters are linked.
// These doubles record ownership and command intent, never pretend to rasterize.
using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static void Destroy(Object obj) { if (obj != null) obj.Destroyed = true; }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a,b) || ((ReferenceEquals(a,null) || a.Destroyed) && (ReferenceEquals(b,null) || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T:Component => gameObject.Components.OfType<T>().FirstOrDefault();
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform Parent;
        public void SetParent(Transform parent, bool world) => Parent = parent;
        public bool IsChildOf(Transform parent) { for (var p = this; p != null; p = p.Parent) if (ReferenceEquals(p,parent)) return true; return false; }
    }
    public class GameObject : Object
    {
        public static readonly List<GameObject> Created = new();
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        public int layer;
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); Created.Add(this); }
        public T AddComponent<T>() where T:Component,new()
        { var c = new T { gameObject = this }; Components.Add(c); c.GetType().GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(c,null); return c; }
    }
    public enum CameraType { Game, Preview }
    public enum RenderingPath { Forward, DeferredShading }
    [Flags] public enum DepthTextureMode { None=0, Depth=1 }
    public class Camera : Component
    {
        public static event Action<Camera> onPreCull;
        public static int Subscribers => onPreCull?.GetInvocationList().Length ?? 0;
        public static void Render(Camera camera) => onPreCull?.Invoke(camera);
        public bool isActiveAndEnabled = true;
        public CameraType cameraType = CameraType.Game;
        public RenderingPath actualRenderingPath = RenderingPath.DeferredShading;
        public DepthTextureMode depthTextureMode = DepthTextureMode.None;
        public int cullingMask = -1;
        public readonly List<Rendering.CommandBuffer> Commands = new();
        public void AddCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer buffer) => Commands.Add(buffer);
        public void RemoveCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer buffer) => Commands.Remove(buffer);
    }
    public struct Plane { }
    public struct Bounds { }
    public static class GeometryUtility
    {
        public static bool InFrustum = true;
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes) { }
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds) => InFrustum;
    }
    public class Renderer : Component
    {
        public bool enabled = true, forceRenderingOff, isVisible = true;
        public Rendering.ShadowCastingMode shadowCastingMode = Rendering.ShadowCastingMode.On;
        public Bounds bounds;
    }
    public class SkinnedMeshRenderer : Renderer { public Mesh sharedMesh; }
    public class MeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Mesh : Object { public int subMeshCount = 1; }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color clear => default; }
    public enum HideFlags { HideAndDontSave }
    public enum FilterMode { Point }
    public enum RenderTextureFormat { ARGBFloat }
    public enum RenderTextureReadWrite { Linear }
    public static class SystemInfo { public static bool Supported = true; public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => Supported; }
    public static class Time { public static int frameCount=10; public static float unscaledTime; }
    public class Shader : Object
    {
        public bool isSupported = true; public int passCount = 2;
        public static int PropertyToID(string value) => value.GetHashCode();
    }
    public class Material : Object
    {
        public static readonly List<Material> Created = new();
        public Shader shader;
        public HideFlags hideFlags;
        public readonly Dictionary<string,Color> Colors = new();
        public Material(Shader shader) { this.shader=shader; Created.Add(this); }
        public void SetColor(string key,Color color) => Colors[key]=color;
    }
    public class AssetBundle : Object
    {
        public static readonly List<AssetBundle> Created = new();
        public static bool FailLoad;
        public bool Unloaded;
        public readonly Shader Shader = new();
        public static AssetBundle LoadFromMemory(byte[] bytes) { if(FailLoad) throw new IOException("synthetic bundle failure"); var b=new AssetBundle();Created.Add(b);return b; }
        public T LoadAsset<T>(string name) where T:Object => Shader as T;
        public void Unload(bool destroy) { Unloaded = true; if(destroy) Shader.Destroyed=true; }
    }
}
namespace UnityEngine.Rendering
{
    public enum CameraEvent { BeforeImageEffectsOpaque }
    public enum ShadowCastingMode { Off, On, ShadowsOnly }
    public enum BuiltinRenderTextureType { CameraTarget }
    public class CommandBuffer : IDisposable
    {
        public static readonly List<CommandBuffer> Created = new();
        public static bool FailDraw;
        public string name;
        public bool Disposed;
        public readonly List<UnityEngine.Renderer> Draws = new();
        public readonly Dictionary<int,float> Globals = new();
        public int Allocations, Releases, Composites, Width, Height;
        public CommandBuffer() => Created.Add(this);
        public void Clear() { Draws.Clear(); Globals.Clear(); Allocations=Releases=Composites=0; }
        public void Dispose() { Clear(); Disposed=true; }
        public void GetTemporaryRT(int id,int width,int height,int depth,UnityEngine.FilterMode filter,UnityEngine.RenderTextureFormat format,UnityEngine.RenderTextureReadWrite rw)
        { Allocations++; Width=width;Height=height; }
        public void SetRenderTarget(int id) { }
        public void ClearRenderTarget(bool depth,bool color,UnityEngine.Color value) { }
        public void SetGlobalFloat(int id,float value) => Globals[id]=value;
        public void DrawRenderer(UnityEngine.Renderer renderer,UnityEngine.Material mat,int submesh,int pass)
        { if(FailDraw) throw new InvalidOperationException("synthetic command failure after allocation"); Draws.Add(renderer); }
        public void Blit(int source,BuiltinRenderTextureType destination,UnityEngine.Material mat,int pass) => Composites++;
        public void ReleaseTemporaryRT(int id) => Releases++;
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    public enum GameStatus { Started, Stopped }
    public enum EBotState { Active, NonActive }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame { public GameStatus Status=GameStatus.Started; public GameWorld GameWorld; }
    public class GameWorld : UnityEngine.Component
    {
        public readonly Dictionary<string,Player> Alive = new();
        public Player GetAlivePlayerByProfileID(string id) => Alive.TryGetValue(id,out var value)?value:null;
    }
    public class Player : UnityEngine.Component
    {
        public string ProfileId="bot"; public bool IsAI=true;
        public HealthSystem.ActiveHealthController ActiveHealthController = new();
        public PlayerBody PlayerBody;
        public AIData AIData = new();
    }
    public class AIData { public BotOwner BotOwner = new(); }
    public class BotOwner { public EBotState BotState = EBotState.Active; }
    public class PlayerBody : UnityEngine.Component
    {
        public readonly List<UnityEngine.Renderer> Renderers = new();
        public void GetRenderersNonAlloc(List<UnityEngine.Renderer> list) => list.AddRange(Renderers);
    }
}
namespace EFT.HealthSystem { public class ActiveHealthController { public bool IsAlive=true; } }
namespace EFT.CameraControl { public class CameraManager { public static CameraManager Instance=new(); public UnityEngine.Camera Camera; } }
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode=new TeamDeathmatchMode(); }
    public class TeamDeathmatchMode { public bool AssistScoringOpen=true, IsComplete; }
    public class RoundsMode { public bool AssistScoringOpen=true, IsComplete; }
    public static class ArenaMatchService { public static ArenaMatch Current = new(); public static bool TeamStartAbortPending; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive=true, Exact=true;
        public static int ArmEpoch;
        public static bool IsExactActiveRaid(EFT.AbstractGame game,EFT.GameWorld world)=>Exact;
    }
}
namespace ArenaMode.Pickups
{
    public struct ArenaPowerupSnapshot { public float ShieldSeconds,ShieldPoints,DamageSeconds; }
    public static class ArenaPickupColors
    {
        public static UnityEngine.Color Shield => new(.25f,.8f,1f);
        public static UnityEngine.Color DoubleDamage => new(1f,.25f,.7f);
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger=new(); }
    public class Logger { public int Warnings; public void LogWarning(string text)=>Warnings++; }
}
