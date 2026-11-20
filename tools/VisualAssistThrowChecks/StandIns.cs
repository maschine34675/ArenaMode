using System.Reflection;
namespace BepInEx.Bootstrap
{
    public class Info { public object Instance; }
    public static class Chainloader { public static Dictionary<string, Info> PluginInfos = new(); }
}
namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value; } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance { get; set; } } }
namespace UnityEngine
{
    public class MonoBehaviour { public bool isActiveAndEnabled = true; }
    public class Transform { }
    public struct Vector3 { public float x, y, z; public float sqrMagnitude => x*x + y*y + z*z; }
}
namespace EFT
{
    public class Health { public bool IsAlive = true; }
    public class Player
    {
        public bool IsYourPlayer = true, IsAI;
        public Health HealthController = new();
        public BaseGrenadeHandsController HandsController;
        public class BaseGrenadeHandsController { }
        public class QuickGrenadeThrowHandsController : BaseGrenadeHandsController { }
        public class GrenadeHandsController : BaseGrenadeHandsController
        {
            public object CurrentOperation = new HighThrowOperation();
            public class HighThrowOperation { }
            public class LowThrowOperation : HighThrowOperation { }
        }
    }
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public Player LocalPlayer; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, Exact = true;
        public static bool IsExactActiveRaid(object game, object world) => Exact;
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static Log ModLogger = new(); }
    public class Log { public void LogDebug(string text) { } }
}
namespace HarmonyLib
{
    // The behavioral harness invokes the actual production prefix explicitly. Native tests establish Harmony wiring.
    public enum HarmonyPatchType { Prefix }
    public static class Priority { public const int First = 800; }
    public class HarmonyMethod(Type type, string name) { public int priority; public MethodInfo Method = AccessTools.DeclaredMethod(type, name); }
    public static class AccessTools { public static MethodInfo DeclaredMethod(Type type, string name) => type.GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic); }
    public class Patch { public string owner; public MethodInfo PatchMethod; }
    public class PatchInfo { public List<Patch> Prefixes = new(); }
    public class Harmony(string id)
    {
        public string Id => id;
        public static int Removes;
        public static Dictionary<MethodInfo, PatchInfo> Entries = new();
        public void Patch(MethodInfo target, HarmonyMethod prefix) { Entries[target] = new() { Prefixes = new() { new() { owner = id, PatchMethod = prefix.Method } } }; }
        public void Unpatch(MethodInfo target, HarmonyPatchType type, string owner) { Removes++; Entries.Remove(target); }
        public static PatchInfo GetPatchInfo(MethodInfo method) => Entries.TryGetValue(method, out var info) ? info : null;
    }
}
namespace VisualAssist
{
    public class Plugin { public static BepInEx.Configuration.ConfigEntry<bool> GrenadeArcEnabled = new() { Value = true }; }
    public struct GrenadeThrow { public UnityEngine.Vector3 ThrowPosition, ThrowForce; }
    public class GrenadeArc : UnityEngine.MonoBehaviour
    {
        public EFT.Player localPlayer;
        public GrenadeThrow GrenadeThrow;
        public static UnityEngine.Vector3 NextPosition = new() { x = 17, y = 5, z = 31 };
        public static UnityEngine.Vector3 NextForce = new() { x = 2, y = 3, z = 4 };
        public static int Calculations;
        public static bool Fail, LastLow;
        private GrenadeThrow CalculateGrenadeThrow(bool low)
        {
            Calculations++; LastLow = low;
            if (Fail) throw new InvalidOperationException("fixture: disappearing hands");
            return new() { ThrowPosition = NextPosition, ThrowForce = NextForce };
        }
    }
    public static class GrenadeThrowPrefixPatch
    {
        public static bool Prefix(EFT.Player.BaseGrenadeHandsController hands, EFT.Player player, UnityEngine.Transform transform, bool lowThrow, float timeSinceSafetyLevelRemoved) => false;
    }
}
