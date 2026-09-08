using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)
               || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class MonoBehaviour : Object { public bool isActiveAndEnabled = true; }
    public class Camera : MonoBehaviour
    {
        public int Width = 1920, Height = 945;
        public List<string> Draws = new();
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
    public enum GameStatus { Running, Started, Stopping, Stopped }
    public class AbstractGame { }
    public class Player { }
    public class LocalGame : AbstractGame { public GameStatus Status; public GameWorld GameWorld; }
    public class GameWorld { public Player MainPlayer; }
    public class PlayerCameraController { public Player Player; }
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
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Active;
        public static EFT.AbstractGame BoundGame;
        public static EFT.GameWorld BoundWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => Active && ReferenceEquals(game, BoundGame) && ReferenceEquals(world, BoundWorld);
    }
}
namespace GPUInstancer
{
    // An explicit native-order seam, not a Unity renderer. Mirrors the relevant
    // ordering in GPUInstancerManager.Update (size check, clear, late rebind).
    // The production repair itself is compiled verbatim by this project.
    public class GPUInstancerManager : UnityEngine.MonoBehaviour
    {
        public bool IsOptic;
        public UnityEngine.Camera Camera;
        public int Rebinds;
        public bool ThrowOnRebind;
        public static int TextureWidth, TextureHeight;
        public static bool Cleared;
        public static List<string> SharedDraws = new();
        public string Name = "grass";
        public void InitializeCameraData()
        {
            if (ThrowOnRebind) throw new InvalidOperationException("rebind failed");
            Camera = EFT.CameraControl.CameraManager.Instance.Camera;
            Rebinds++;
        }
        public virtual void Update()
        {
            if (Camera != null && !IsOptic)
            {
                TextureWidth = Camera.Width;
                TextureHeight = Camera.Height;
            }
            if (Camera != null && !Cleared) { SharedDraws.Clear(); Cleared = true; }
            if (Camera == null) InitializeCameraData();
            SharedDraws.Add(Name);
        }
        public bool RenderDimensionsMatch => Camera.Width == TextureWidth && Camera.Height == TextureHeight;
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int Last = 0; }
    public class HarmonyMethod
    {
        public MethodInfo Method;
        public int priority;
        public HarmonyMethod(Type type, string name)
        {
            Method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(name);
        }
    }
    public class Harmony
    {
        public static List<(MethodInfo target, HarmonyMethod prefix)> Installed = new();
        public Harmony(string id) { }
        public void Patch(MethodInfo original, HarmonyMethod prefix) => Installed.Add((original, prefix));
        public void UnpatchSelf() => Installed.Clear();
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static Log ModLogger = new();
    }
    public class Log
    {
        public List<string> Lines = new();
        public void LogInfo(string message) => Lines.Add(message);
        public void LogWarning(string message) => Lines.Add(message);
    }
}
