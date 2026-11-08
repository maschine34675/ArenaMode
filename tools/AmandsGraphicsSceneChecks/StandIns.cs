// Only the native scene lookup and its physical ownership inputs are simulated. Harmony's reflection and instruction surface is modelled for the offline runner.
namespace UnityEngine
{
    public class GameObject
    {
        public static GameObject Named;
        public object Component;
        public Scene scene = new();
        public static GameObject Find(string name) => Named;
        public T GetComponent<T>() => Component is T value ? value : default;
    }
    public class Scene { public bool isLoaded = true; }
}
public class PrismEffects { }
public class LevelSettings { public bool isActiveAndEnabled = true; public UnityEngine.GameObject gameObject = new(); }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace EFT
{
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public RaidSettings Settings = new(); }
    public class GameWorld { public string LocationId; }
    public class RaidSettings { public string location, serverId; }
}
namespace EFT.CameraControl
{
    public class CameraManager { public static CameraManager Instance = new(); public Camera Camera = new(); }
    public class Camera { public UnityEngine.GameObject gameObject = new(); }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid, Matches;
        public static string ArmedMap;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => Matches;
    }
    public static class ArenaServerClient { public static string PendingArmToken; }
}
namespace ArenaMode.Shared
{
    public static class ArenaMapCatalog { public const string LabyrinthId = "labyrinth"; }
    public static class Protocol
    {
        public static bool IsLifecycleRequestId(string value) => value == "current";
        public static bool IsNativeStartIdentity(string id, string token) => id == "start-" + token;
        public static bool HasNativeEndIdentity(string id, string token) => id == "end-" + token;
    }
}
namespace Redeploy.Core.Compat { public static class FikaCompatCheck { public static bool IsFikaLoaded; } }
namespace Redeploy.Core.GameAccess { public static class LocalGameAccessor { public static EFT.RaidSettings GetLocalRaidSettings(EFT.LocalGame game) => game.Settings; } }
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "test.arena"; public static Log ModLogger = new(); }
    public class Log { public void LogDebug(string text) { } }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, Info> PluginInfos = new(); }
    public class Info { public object Instance; }
}

namespace HarmonyLib
{
    using System.Reflection;
    using System.Reflection.Emit;
    internal class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public List<Label> labels = new();
        public CodeInstruction(OpCode opcode, object operand = null) { this.opcode = opcode; this.operand = operand; }
        public bool Calls(MethodInfo method) => (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && method.Equals(operand);
    }
    internal static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                : type.GetMethod(name, parameters);
        public static MethodInfo Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters);
    }
    internal class Harmony(string id)
    {
        public string Id = id;
        public void Patch(MethodInfo target, HarmonyMethod transpiler) => throw new NotSupportedException("Native patch installation needs EFT");
        public void Unpatch(MethodInfo target, HarmonyPatchType patchType, string owner) { }
    }
    internal class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    internal enum HarmonyPatchType { Transpiler }
}
