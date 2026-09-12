// Only engine/game collaborators are modeled. Both the actual UI lifecycle and
// the new Tactical current-body method are linked from production source.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object x, Object y) => (ReferenceEquals(x, null) || x.Destroyed) ? ReferenceEquals(y, null) || y.Destroyed : ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !(x == y);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object obj) { if (obj != null) obj.Destroyed = true; }
    }
    public class GameObject : Object
    {
        public static int Created;
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly Transform transform;
        private readonly List<Component> _components = new();
        public GameObject(string name = "", params Type[] types)
        {
            Created++; this.name = name;
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this; _components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; _components.Add(c); return c; }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public void SetActive(bool active)
        {
            activeSelf = active;
            // Native Graphic.OnDisable clears CanvasRenderer geometry; OnEnable
            // merely marks it dirty. A later UI rebuild must restore geometry.
            if (!active)
                foreach (var image in UI.Image.All.Where(i => !i.gameObject.activeInHierarchy)) image.HasGeometry = false;
        }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform => gameObject.transform; public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>(); }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public Vector3 position;
        public void SetParent(Transform next, bool worldPositionStays) { parent?.Children.Remove(this); parent = next; next?.Children.Add(this); }
    }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition; public Quaternion localRotation; }
    public readonly record struct Quaternion(float X, float Y, float Z) { public static Quaternion Euler(float x, float y, float z) => new(x, y, z); }
    public readonly record struct Vector2(float x, float y) { public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y); }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));
        public static implicit operator Vector2(Vector3 p) => new(p.x, p.y);
    }
    public readonly record struct Color(float r, float g, float b, float a = 1f);
    public readonly record struct Rect(float x, float y, float width, float height);
    public static class Screen { public static int width = 1920, height = 1080; }
    public class Camera : Component
    {
        public static event Action<Camera> onPreCull;
        public static int Subscribers => onPreCull?.GetInvocationList().Length ?? 0;
        public bool isActiveAndEnabled = true;
        public Rect rect = new(0, 0, 1, 1);
        public int RenderTargetWidth, RenderTargetHeight;
        public Func<Vector3, Vector3> Project = p => p;
        public Vector3 WorldToViewportPoint(Vector3 p) => Project(p);
        // Native camera projection collaborator: input/target pixels, not the
        // postprocessed display. This deliberately reproduces the old DLSS bug.
        public Vector3 WorldToScreenPoint(Vector3 p)
        {
            var v = Project(p);
            return RenderTargetWidth > 0 && RenderTargetHeight > 0
                ? new Vector3(v.x * RenderTargetWidth, v.y * RenderTargetHeight, v.z)
                : new Vector3((rect.x + v.x * rect.width) * Screen.width,
                    (rect.y + v.y * rect.height) * Screen.height, v.z);
        }
        public static void Cull(Camera camera) => onPreCull?.Invoke(camera);
        public static void Reset() => onPreCull = null;
    }
    public static class Time { public static float unscaledTime; }
    public static class Canvas
    {
        public static event Action preWillRenderCanvases;
        public static int Subscribers => preWillRenderCanvases?.GetInvocationList().Length ?? 0;
        public static void Rebuild()
        {
            preWillRenderCanvases?.Invoke();
            // CanvasUpdateRegistry.PerformUpdate is subscribed to the later
            // willRenderCanvases event in the installed UnityEngine.UI assembly.
            foreach (var image in UI.Image.All) image.HasGeometry = image.gameObject.activeInHierarchy;
        }
        public static void Reset() { preWillRenderCanvases = null; UI.Image.All.Clear(); }
    }
    public static class RectTransformUtility
    {
        public static bool ConversionWorks = true;
        public static float Scale = 1f;
        public static bool ScreenPointToLocalPointInRectangle(RectTransform root, Vector2 screen, Camera camera, out Vector2 local)
        { local = new((screen.x - Screen.width / 2f) / Scale, (screen.y - Screen.height / 2f) / Scale); return ConversionWorks; }
    }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static int Rays;
        public static Vector3 LastOrigin, LastEnd;
        public static int LastMask;
        public static QueryTriggerInteraction LastTrigger;
        public static Func<bool> IsBlocked = () => false;
        public static bool Linecast(Vector3 origin, Vector3 end, int mask, QueryTriggerInteraction query)
        { Rays++; LastOrigin = origin; LastEnd = end; LastMask = mask; LastTrigger = query; return IsBlocked(); }
    }
}
// Only native input/output state is modeled; the production field reader and
// projection policy run unchanged. RequestedRatio does not imply applied state.
public class SSAAImpl : UnityEngine.Component
{
    private enum SSState { NOSCALE, UPSCALE, DOWNSCALE }
    private SSState CurrentState;
    public bool isActiveAndEnabled = true;
    public float RequestedRatio = 1f;
    public void ApplyState(int state) => CurrentState = (SSState)state;
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component
    {
        public static readonly List<Image> All = new();
        public Image() => All.Add(this);
        public UnityEngine.Color color;
        public bool raycastTarget = true, HasGeometry;
    }
}
public static class LayersMaskController { public static int HighPolyWithTerrainMask = 3, LowPolyColliderLayerMask = 4, PlayerStaticDoorMask = 8, TransparentLayerMask = 16; }
public enum EBotState { PreActive, Active, NonActive, Disposed }
public class PlayerBones { public EFT.BifacialTransform Head = new(); }
namespace EFT
{
    public class BifacialTransform
    {
        public UnityEngine.Transform Original = new UnityEngine.GameObject().transform;
        public bool UseImitation, AccumulatePositionAndRotation;
        public UnityEngine.Vector3 AccumulatedPosition;
        public Func<UnityEngine.Vector3> Imitator;
        public UnityEngine.Vector3 position => AccumulatePositionAndRotation ? AccumulatedPosition : UseImitation ? Imitator() : Original.position;
    }
    public class Health { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : UnityEngine.Component
    {
        public string ProfileId;
        public bool IsAI = true;
        public Health HealthController = new();
        public PlayerBones PlayerBones = new();
        public AIData AIData = new();
    }
    public class BotOwner : UnityEngine.Object
    {
        public Player GetPlayer;
        public string ProfileId;
        public EBotState BotState = EBotState.Active;
        public bool IsDead;
        public Health HealthController = new();
    }
    public class GameWorld
    {
        public Player MainPlayer;
        public List<Player> AllAlivePlayersList = new();
        public Dictionary<string, Player> Alive = new(), Ever = new();
        public Player GetAlivePlayerByProfileID(string id) => id != null && Alive.TryGetValue(id, out var p) ? p : null;
        public Player GetEverExistedPlayerByID(string id) => id != null && Ever.TryGetValue(id, out var p) ? p : null;
    }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { }
}
namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT.CameraControl { public class CameraManager { public static CameraManager Instance; public static bool Exist => Instance != null; public UnityEngine.Camera Camera; } }
namespace ArenaMode.Config
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public static class Settings
    {
        public static Setting<bool> HudEnabled = new(true), ShowFriendlyMarkers = new(true);
        public static Setting<ArenaMode.Effects.ArenaTracerColor> FriendlyTracerColor = new(ArenaMode.Effects.ArenaTracerColor.Cyan);
    }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive = true, Exact = true; public static string ActiveMode = "teamdeathmatch"; public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => Exact; } }
namespace ArenaMode.Compat { public static class PitFireTeamCompat { public static bool TeamArenaActive = true; } }
namespace ArenaMode { public static class Plugin { public static Logger ModLogger = new(); public class Logger { public readonly List<string> Info = new(); public void LogInfo(string text) => Info.Add(text); public void LogWarning(string text) { } } } }
namespace ArenaMode.Modes
{
    public interface IMode { bool IsComplete { get; } }
    public class ArenaMatch { public IMode Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    public class TeamDeathmatchMode : IMode
    {
        public bool IsComplete { get; set; }
        public readonly Dictionary<EFT.Player, bool> Bodies = new();
        // Existing TDM API is separately tested by TeamGiveUpChecks; this models
        // the returned exact-body affiliation, never a historical ID lookup.
        public bool TryClassifyCurrentBody(EFT.Player p, out bool friendly) => Bodies.TryGetValue(p, out friendly);
    }
    public sealed partial class TacticalMode : IMode
    {
        public enum Phase { Staging, Buy, Live, Aftermath, Rebinding, Ended }
        private const int TeamCompanionCount = 4;
        private bool _teamArena = true;
        private Phase _phase = Phase.Live;
        private ArenaMatch _match;
        private int _teamRoundEpoch = 1;
        private EFT.LocalGame _teamGame;
        private EFT.Player _boundLocalPlayer;
        private string _boundLocalProfileId;
        private EFT.BotOwner[] _boundTeamFollowers;
        private string[] _boundTeamFollowerProfileIds;
        public bool IsComplete { get; set; }
        public bool IsTeamArena => _teamArena;
        public TacticalMode(ArenaMatch match, EFT.LocalGame game, EFT.Player local, EFT.BotOwner[] followers)
        { _match = match; _teamGame = game; _boundLocalPlayer = local; _boundLocalProfileId = local.ProfileId; _boundTeamFollowers = followers; _boundTeamFollowerProfileIds = followers.Select(b => b.ProfileId).ToArray(); }
        public void SetPhase(Phase phase) => _phase = phase;
        public void SetTeam(bool team) => _teamArena = team;
        public void SetEpoch(int epoch) => _teamRoundEpoch = epoch;
        public void SetBoundProfile(int index, string profile) => _boundTeamFollowerProfileIds[index] = profile;
        public void SetOwner(int index, EFT.BotOwner owner) => _boundTeamFollowers[index] = owner;
        public void SetMatch(ArenaMatch match) => _match = match;
        public void SetLeader(EFT.Player player) => _boundLocalPlayer = player;
    }
}
