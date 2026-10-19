using System.Reflection;

namespace UnityEngine
{
    public class GameObject { }
    public class Component { public GameObject gameObject = new(); }
    public class MonoBehaviour : Component { }
}
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
        public static int FailAfter = -1;
        public static bool FailUnpatch;
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (FailAfter == 0) throw new InvalidOperationException("injected partial patch failure");
            if (FailAfter > 0) FailAfter--;
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
namespace EFT.InventoryLogic { public class InventoryController { } }
namespace EFT.Interactive { public class LootableContainer { } }
namespace EFT
{
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public class AbstractGame { }
    public class BotsController { }
    public interface IBotGame { BotsController BotsController { get; } }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public GameStatus Status = GameStatus.Running;
        public LocalRaidSettings _raidSettings;
        public BotsController BotsController = new();
        BotsController IBotGame.BotsController => BotsController;
    }
    public class HideoutGame : AbstractGame { }
    public class GameWorld
    {
        public string LocationId;
        public Dictionary<string, Player> Players = new();
        public Player GetEverExistedPlayerByID(string id) => Players.GetValueOrDefault(id);
    }
    public class LocalRaidSettings { public string location, serverId; public JsonType.ESideType playerSide; }
    public class Profile { public string AccountId; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : UnityEngine.Component
    {
        public string ProfileId;
        public bool IsAI = true;
        public AIData AIData = new();
        public InventoryLogic.InventoryController InventoryController = new();
    }
    public class BotOwner
    {
        public Profile Profile = new();
        public Player GetPlayer;
        public BotsController BotsController;
    }
}
namespace DrakiaXYZ.BigBrain.Brains
{
    public class CustomLayer
    {
        public EFT.BotOwner BotOwner { get; set; }
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
        public static string ArmedMap, ArmedMode, ActiveMode;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
    public static class ArenaServerClient { public static bool HasPendingArm; public static string PendingArmToken; }
}
namespace ArenaMode.Compat
{
    public static class PitFireTeamCompat
    {
        public static bool TeamArenaActive;
        private static IReadOnlyList<string> _accounts = Array.Empty<string>();
        public static int RosterReads;
        public static IReadOnlyList<string> FrozenCompanionAccountIds
        {
            get { RosterReads++; return _accounts; }
            set { _accounts = value; RosterReads = 0; }
        }
    }
}
