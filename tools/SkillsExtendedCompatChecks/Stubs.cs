using System.Reflection;

namespace UnityEngine { public class MonoBehaviour { } }
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
    public static class Priority { public const int First = 800, Last = 0; }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(), Postfixes = new(); }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailPatch, FailUnpatch;
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (prefix != null) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (postfix != null) info.Postfixes.Add(new() { owner = id, PatchMethod = postfix.Method });
            if (FailPatch) throw new InvalidOperationException("injected patch failure after partial registration");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values)
            { info.Prefixes.RemoveAll(p => p.owner == id); info.Postfixes.RemoveAll(p => p.owner == id); }
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
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace JsonType { public enum ESideType { Pmc, Savage } }
namespace EFT
{
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public interface IBotGame { }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public GameStatus Status = GameStatus.Started;
        public LocalRaidSettings _raidSettings;
        public Profile Profile;
        public Player LocalPlayer;
    }
    public class HideoutGame : AbstractGame, IBotGame { }
    public class GameWorld
    {
        public string LocationId;
        public Player MainPlayer;
        public void OnGameStarted() { }
    }
    public class Profile { public string Id; }
    public class Player
    {
        public GameWorld GameWorld;
        public bool IsYourPlayer = true;
        public Profile Profile;
        public HealthSystem.ActiveHealthController ActiveHealthController { get; } = new();
        public SkillManager Skills { get; } = new();
    }
    public sealed class Mastering { }
    public sealed class SkillManager
    {
        public event Action<Mastering> OnMasteringExperienceChanged;
        public int Subscribers => OnMasteringExperienceChanged?.GetInvocationList().Length ?? 0;
        public void Fire()
        {
            foreach (var callback in OnMasteringExperienceChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                Program.Call(callback.Method, callback.Target, new Mastering());
        }
    }
    public class LocalRaidSettings { public string location, serverId; public JsonType.ESideType playerSide; }
}
namespace EFT.HealthSystem
{
    public interface IHealthEffect { }
    public sealed class Effect : IHealthEffect { }
    public class ActiveHealthController
    {
        public event Action<IHealthEffect> EffectStartedEvent;
        public int Subscribers => EffectStartedEvent?.GetInvocationList().Length ?? 0;
        public void Fire()
        {
            foreach (var callback in EffectStartedEvent?.GetInvocationList() ?? Array.Empty<Delegate>())
                Program.Call(callback.Method, callback.Target, new Effect());
        }
    }
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
        public static bool ArmedForNextRaid, IsActive;
        public static string ArmedMap;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
    public static class ArenaServerClient { public static bool HasPendingArm; public static string PendingArmToken; }
}
