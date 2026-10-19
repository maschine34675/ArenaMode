using System.Reflection;

// Controlled native/reflection boundaries. These checks execute the production
// prefixes and admission policy; they do not certify a Mono Harmony detour.
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    { public string GUID = guid; public Version Version = new(version); }
    public sealed class PluginInfo
    { public object Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader
    { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum WildSpawnType { assault }
    public enum ESideType { Pmc, Savage }
    public sealed class LocalRaidSettings
    { public string serverId, location; public ESideType playerSide; }
    public sealed class SessionResult { }
    public sealed class Profile { public string Id; }
    public sealed class EftClientBackendSession { public Profile Profile; }
    public class AbstractGame { }
    public sealed class LocalGame : AbstractGame
    { public GameWorld GameWorld; public LocalRaidSettings Settings; }
    public interface IPlayer { }
    public sealed class BotsGroup { }
    public sealed class Player : IPlayer
    { public bool IsAI = true; public string ProfileId; public AIData AIData = new(); }
    public sealed class AIData { public BotOwner BotOwner; }
    public sealed class BotOwner
    { public Player GetPlayer; public GetProfileDataParams SpawnProfileData = new(); }
    public sealed class GetProfileDataParams { public BotSpawnParams SpawnParams = new(); }
    public sealed class GameWorld
    {
        public readonly Dictionary<string, Player> Registered = new();
        public Player GetEverExistedPlayerByID(string id) => Registered.TryGetValue(id, out var player) ? player : null;
    }
}
namespace JsonType
{
    public sealed class LocalSettings { public string serverId; }
    public sealed class FlatItem { }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    { public static EFT.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game) => game.Settings; }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public sealed class Logger
    {
        public readonly List<string> Warnings = new();
        public void LogDebug(object text) { }
        public void LogWarning(object text) { Warnings.Add(text.ToString()); }
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public sealed class HarmonyMethod(MethodInfo method) { public MethodInfo Method = method; public int priority; }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class Patches { public List<Patch> Prefixes = new(); }
    public sealed class Harmony(string id)
    {
        private static readonly Dictionary<MethodInfo, Patches> Installed = new();
        public static bool RejectInstallation;
        public static bool ThrowUnpatch;
        public static string RejectPrefix;
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        {
            if (RejectInstallation || prefix.Method.Name == RejectPrefix) return;
            // Preserve Harmony's named-argument requirement in this boundary.
            foreach (var arg in prefix.Method.GetParameters())
                if (!target.GetParameters().Any(p => p.Name == arg.Name && p.ParameterType == arg.ParameterType))
                    throw new InvalidOperationException("Invalid Harmony parameter: " + arg.Name);
            if (!Installed.TryGetValue(target, out var patches)) Installed[target] = patches = new();
            patches.Prefixes.Add(new Patch { owner = id, PatchMethod = prefix.Method });
        }
        public static Patches GetPatchInfo(MethodInfo target) => target != null && Installed.TryGetValue(target, out var p) ? p : null;
        public static void RemovePrefix(string name)
        { foreach (var p in Installed.Values) p.Prefixes.RemoveAll(p => p.PatchMethod.Name == name); }
        public void UnpatchSelf()
        {
            if (ThrowUnpatch) throw new InvalidOperationException("simulated internal unpatch failure");
            foreach (var p in Installed.Values) p.Prefixes.RemoveAll(p => p.owner == id);
        }
    }
}
namespace MoreBotsAPI
{
    [BepInEx.BepInPlugin("com.morebotsapi.tacticaltoaster", "MoreBotsAPI", "2.1.1")]
    public sealed class Plugin { }
}
namespace MoreBotsAPI.Components
{
    // Signature-only fixtures, not copied foreign implementation.
    public sealed class FactionManager
    {
        public Dictionary<string, List<string>> UpdateRevengeFactions = new();
        public void UpdateRevengeByRole(EFT.WildSpawnType role, string playerID) { }
        public void SendRevenges() { }
    }
    public sealed class HuntManager { public void OnBotCreated(EFT.BotOwner bot) { } }
}
namespace MoreBotsAPI.Patches
{
    public sealed class FactionRaidEndPatch
    {
        public static void PatchPostfix(EFT.LocalRaidSettings settings, EFT.SessionResult results,
            JsonType.FlatItem[] lostInsuredItems, Dictionary<string, JsonType.FlatItem[]> transferItems) { }
    }
    public sealed class BotsGroupIsPlayerEnemyPatch
    {
        public static void PatchPostfix(ref bool __result, EFT.BotsGroup __instance, EFT.IPlayer player) { }
    }
}
