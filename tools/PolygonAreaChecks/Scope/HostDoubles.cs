// Limited synchronous host contracts for the real capture controller. This does
// not load Unity, EFT or Harmony and is not proof of real input/physics/rendering.
using ArenaMode.Shared;

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) => Value = value; }
    public readonly struct KeyboardShortcut
    {
        public UnityEngine.KeyCode MainKey { get; }
        public UnityEngine.KeyCode[] Modifiers { get; }
        public KeyboardShortcut(UnityEngine.KeyCode key, params UnityEngine.KeyCode[] modifiers)
        { MainKey = key; Modifiers = modifiers; }
        public override string ToString() => MainKey.ToString();
    }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}

namespace EFT
{
    public enum GameStatus { Starting, Started, Stopping, Stopped }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; public GameWorld GameWorld; }
    public sealed class LocalGame : AbstractGame { public Player LocalPlayer; }
    public sealed class GameWorld { public string LocationId; public Player MainPlayer; }
    public sealed class HealthController { public bool IsAlive = true; }
    public sealed class Player
    {
        public bool IsYourPlayer = true;
        public HealthController HealthController = new();
        public UnityEngine.Vector3 Position;
    }
}

namespace EFT.CameraControl
{
    public sealed class CameraManager
    {
        public static CameraManager Instance;
        public static bool Exist => Instance != null;
        public UnityEngine.Camera Camera;
    }
}

namespace EFT.UI.Screens
{
    public enum EEftScreenType { BattleUI, Menu }
    public sealed class EftScreenManager
    {
        public static EftScreenManager Instance;
        public EEftScreenType Screen = EEftScreenType.BattleUI;
        public bool CheckCurrentScreen(EEftScreenType requested) => requested == Screen;
    }
}

namespace ArenaMode.Config
{
    using BepInEx.Configuration;
    using UnityEngine;
    internal static class Settings
    {
        public static ConfigEntry<bool> AreaCaptureEnabled;
        public static ConfigEntry<float> AreaCaptureMinY, AreaCaptureMaxY, AreaCaptureSpawnMinY, AreaCaptureSpawnMaxY;
        public static ConfigEntry<KeyboardShortcut> AreaCaptureUndo, AreaCaptureExport, AreaCaptureFeet, AreaCaptureAim;
        public static void Reset()
        {
            AreaCaptureEnabled = new(true);
            AreaCaptureMinY = new(0); AreaCaptureMaxY = new(8);
            AreaCaptureSpawnMinY = new(0.5f); AreaCaptureSpawnMaxY = new(2);
            AreaCaptureUndo = new(new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl));
            AreaCaptureExport = new(new KeyboardShortcut(KeyCode.F9, KeyCode.LeftControl));
            AreaCaptureFeet = new(new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl));
            AreaCaptureAim = new(new KeyboardShortcut(KeyCode.F6, KeyCode.LeftControl));
        }
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame ExactGame;
        public static EFT.GameWorld ExactWorld;
        // Models the identity boundary, not the full profile/session lifecycle.
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && game != null && world != null
               && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
}
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Spectator { internal static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode.Modes
{
    internal static class ArenaToasts
    {
        public static readonly List<string> Messages = new();
        public static void Show(string message) => Messages.Add(message);
    }
}
namespace ArenaMode.Areas
{
    internal static class ArenaAreaRuntime
    {
        public static ArenaAreaDefinition CurrentArea;
        public static readonly HashSet<UnityEngine.Collider> Owned = new();
        public static bool OwnsCollider(UnityEngine.Collider collider) => Owned.Contains(collider);
    }
}
namespace ArenaMode
{
    internal sealed class Plugin
    {
        public static Plugin Instance;
        public static readonly Logger ModLogger = new();
        public PluginInfo Info = new();
        internal sealed class PluginInfo { public string Location; }
        internal sealed class Logger
        {
            public readonly List<string> Warnings = new(), Infos = new();
            public void LogWarning(object message) => Warnings.Add(message.ToString());
            public void LogInfo(object message) => Infos.Add(message.ToString());
        }
    }
}

namespace UnityEngine
{
    public class MonoBehaviour { }
    public enum KeyCode { F6, F7, F8, F9, LeftControl }
    public enum CursorLockMode { None, Locked }
    public static class Cursor { public static CursorLockMode lockState; }
    public static class Input
    {
        private static readonly HashSet<KeyCode> Down = new(), Held = new();
        public static int Reads;
        public static void Frame(KeyCode[] down, KeyCode[] held)
        { Down.Clear(); Held.Clear(); foreach (var key in down) Down.Add(key); foreach (var key in held) Held.Add(key); }
        public static bool GetKeyDown(KeyCode key) { Reads++; return Down.Contains(key); }
        public static bool GetKey(KeyCode key) { Reads++; return Held.Contains(key); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public struct Ray { }
    public sealed class Camera
    {
        public bool isActiveAndEnabled = true;
        public Ray ViewportPointToRay(Vector3 point) => new();
        public Vector3 WorldToScreenPoint(Vector3 point) => new(100 + point.x, 100 + point.y, 1);
    }
    public sealed class Collider
    {
        public string Name;
        public EFT.Player ParentPlayer;
        public T GetComponentInParent<T>() where T : class => ParentPlayer as T;
    }
    public struct RaycastHit { public float distance; public Vector3 point; public Collider collider; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public const int DefaultRaycastLayers = -5;
        public static RaycastHit[] Hits = Array.Empty<RaycastHit>();
        public static int Calls, LastLayers;
        public static float LastDistance;
        public static QueryTriggerInteraction LastTrigger;
        public static Exception Throw;
        public static RaycastHit[] RaycastAll(Ray ray, float distance, int layers, QueryTriggerInteraction trigger)
        {
            Calls++; LastDistance = distance; LastLayers = layers; LastTrigger = trigger;
            if (Throw != null) throw Throw;
            return Hits.ToArray();
        }
    }
    public struct Color
    {
        public float r, g, b;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
        public static Color white => new(1, 1, 1);
    }
    public struct Matrix4x4 { public int Stamp; }
    public enum EventType { Layout, Repaint }
    public sealed class Event { public static Event current = new(); public EventType type = EventType.Repaint; }
    public enum FontStyle { Normal, Bold }
    public sealed class GUIStyle
    {
        public int fontSize; public bool wordWrap; public FontStyle fontStyle;
        public GUIStyle() { }
        public GUIStyle(GUIStyle style) { }
    }
    public sealed class GUISkin { public GUIStyle label = new(); }
    public sealed class GUIContent { public static readonly GUIContent none = new(); }
    public sealed class Texture2D { public static readonly Texture2D whiteTexture = new(); }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public static class GUI
    {
        public static Color color;
        public static Matrix4x4 matrix;
        public static readonly GUISkin skin = new();
        public static int Labels, Boxes, Textures;
        public static bool ThrowOnDraw;
        public static void Label(Rect rect, string text, GUIStyle style) { Labels++; }
        public static void Box(Rect rect, GUIContent content) { Boxes++; }
        public static void DrawTexture(Rect rect, Texture2D texture)
        { Textures++; if (ThrowOnDraw) throw new InvalidOperationException("fake GUI failure"); }
    }
    public static class GUIUtility
    {
        public static void RotateAroundPivot(float degrees, Vector2 point) => GUI.matrix = new Matrix4x4 { Stamp = GUI.matrix.Stamp + 1 };
    }
    public static class Screen { public static int height = 1080; }
    public static class Mathf
    {
        public const float Rad2Deg = 180 / MathF.PI;
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    }
}
