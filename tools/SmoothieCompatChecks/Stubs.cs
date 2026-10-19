// Boundary doubles. Policies, reflected discovery, lifecycle and marker mutations
// below are linked production source. No foreign binary is loaded or executed.
using System.Reflection;

namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    {
        public string GUID => guid;
        public string Name => name;
        public Version Version => new(version);
    }
    public sealed class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(); }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailPatch, FailUnpatch, DropReceipt;
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (!DropReceipt) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (FailPatch) throw new InvalidOperationException("injected partial patch failure");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values) info.Prefixes.RemoveAll(p => p.owner == id);
        }
        public static PatchInfo GetPatchInfo(MethodBase target) => Bound.GetValueOrDefault(target);
    }
    public static class AccessTools
    {
        public static bool MissingSettings;
        public static FieldInfo Field(Type type, string name)
            => MissingSettings ? null : type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace JsonType { public enum ESideType { Pmc, Savage } }
namespace EFT
{
    public enum GameStatus { Stopped, Starting, Started, Stopping }
    public interface IBotGame { }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public GameStatus Status = GameStatus.Started;
        public LocalRaidSettings _raidSettings;
    }
    public class HideoutGame : AbstractGame, IBotGame { }
    public class GameWorld { public string LocationId; }
    public class LocalRaidSettings { public string location; public JsonType.ESideType playerSide; }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool FailRead;
        public static EFT.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)
            => FailRead ? throw new InvalidOperationException("injected settings read failure") : game._raidSettings;
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Logger ModLogger = new();
    }
    public sealed class Logger
    {
        public List<string> Info = new(), Debug = new(), Warnings = new();
        public void LogInfo(string text) => Info.Add(text);
        public void LogDebug(string text) => Debug.Add(text);
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
}
namespace EFT.CameraControl
{
    public class OpticCameraManager { public UnityEngine.Camera Camera; }
    public class CameraManager
    {
        public static CameraManager Instance;
        public static bool Exist => Instance != null;
        public UnityEngine.Camera Camera;
        public OpticCameraManager OpticCameraManager;
    }
}
namespace ArenaMode.Hud { public static class HudStyle { public static object Font; } }
namespace ArenaMode.Pickups
{
    public enum ArenaPickupKind { Weapon, DoubleDamage, Shield, JumpBoots, InfiniteAmmo, Health }
    public enum ArenaPickupPhase { Available, Cooldown, Pending, Retired }
    public class ArenaPickupPoint { public string Id; public float Yaw; public ArenaPickupKind Kind; }
    public class ArenaPickupState { public ArenaPickupPhase Phase; public float ReadyAt; }
    public static class ArenaPickupColors { public static UnityEngine.Color DoubleDamage, Shield; }
    public static class ArenaHealthPickup { public const float Amount = 160; }
    public static class ArenaPickupGeometry { public static float BeamLength(UnityEngine.Vector3 point) => 4f; }
}
