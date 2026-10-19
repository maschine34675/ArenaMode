using System.Reflection;

namespace BepInEx
{
    public class BaseUnityPlugin { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    { public string GUID => guid; public Version Version => new(version); }
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
    { public int priority; public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public); }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(), Postfixes = new(), Transpilers = new(), Finalizers = new(); }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name) : type.GetMethod(name, parameters);
    }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool DropReceipt, FailUnpatch;
        public static int FailPatchAt, PatchCalls;
        public void Patch(MethodBase method, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (!Bound.TryGetValue(method, out var info)) Bound[method] = info = new();
            if (!DropReceipt)
            {
                if (prefix != null) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
                if (postfix != null) info.Postfixes.Add(new() { owner = id, PatchMethod = postfix.Method });
            }
            if (++PatchCalls == FailPatchAt) throw new InvalidOperationException("injected patch failure");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values)
            { info.Prefixes.RemoveAll(p => p.owner == id); info.Postfixes.RemoveAll(p => p.owner == id); }
        }
        public static PatchInfo GetPatchInfo(MethodBase method) => Bound.GetValueOrDefault(method);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Running, Loading, Started, Stopped }
    public enum ESideType { Pmc, Savage }
    public enum EBodyPart { Head, Chest }
    public enum EDamageType { Bullet, Fall }
    public interface IBotGame { }
    public interface IPlayer { }
    public class PlayerBridge { public IPlayer iPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame
    { public GameWorld GameWorld; public GameStatus Status = GameStatus.Started; public JsonType.LocalRaidSettings Settings = new(); public Player LocalPlayer; public Profile Profile = new(); }
    public class Profile { }
    public class GameWorld
    {
        public string LocationId = "factory4_day";
        public List<IPlayer> RegisteredPlayers = new();
        public void OnGameStarted() { }
        public void RegisterPlayer(IPlayer player) { RegisteredPlayers.Add(player); }
        public void UnregisterPlayer(IPlayer player) { RegisteredPlayers.Remove(player); }
    }
    public class Player : IPlayer
    {
        public GameWorld GameWorld;
        public Profile Profile;
        public string ProfileId = "same-profile";
        public bool IsYourPlayer;
        public void OnBeenKilledByAggressor(IPlayer aggressor, Ballistics.DamageInfo damage, EBodyPart part, EDamageType type) { }
        public void OnHealthApplyDamage(EBodyPart part, float damage, Ballistics.DamageInfo info) { }
        public class PlayerInventoryController { public Player Player; public string ID = "same-profile"; }
        public class SinglePlayerInventoryController : PlayerInventoryController
        { public SinglePlayerInventoryController(Player player, Profile profile, bool isBot = false, bool examined = false) { Player = player; } }
    }
    public class LocalPlayer : Player { }
}
namespace JsonType
{
    public sealed class LocalRaidSettings
    { public EFT.ESideType playerSide = EFT.ESideType.Pmc; public string location = "factory4_day", serverId; }
}
namespace EFT.InventoryLogic
{
    public class Weapon
    {
        public object Owner;
        public string Id = "instance", TemplateId = "template";
        public void OnShot() { }
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo { public EFT.PlayerBridge Player; public object Weapon; }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool ThrowRead;
        public static JsonType.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)
            => ThrowRead ? throw new InvalidOperationException("injected native settings read failure") : game.Settings;
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, ActiveGame) && ReferenceEquals(world, ActiveWorld);
    }
    public static class ArenaServerClient { public static bool HasPendingArm; public static string PendingArmToken; }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static readonly Logger ModLogger = new(); }
    public sealed class Logger
    {
        public readonly List<string> Debug = new(), Warnings = new();
        public void LogDebug(string text) => Debug.Add(text);
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Diagnostics
{
    public static class ArenaLog
    {
        public static bool DeveloperEnabled;
        public static readonly List<string> Messages = new();
        public static void Developer(string text) { if (DeveloperEnabled) Messages.Add(text); }
    }
}
