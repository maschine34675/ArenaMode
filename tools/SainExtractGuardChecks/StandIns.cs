// Production adapter, modeled external objects. No SAIN/Unity code is copied, and no Harmony
// detours or game behavior are executed: the fake Harmony records the hook, and the program
// calls SAIN's stand-in method and then the registered production postfix, as Harmony would.
using System.Reflection;

namespace EFT
{
    public class AbstractGame { }
    public class GameWorld { public string LocationId = "arena_bay5"; }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Log ModLogger = new();
    }
    public class Log
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Messages = new();
        public void LogDebug(object value) => Messages.Add(value.ToString());
        public void LogWarning(object value) { Warnings.Add(value.ToString()); Messages.Add(value.ToString()); }
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int Last = 0; }
    public sealed class HarmonyMethod(MethodInfo method)
    {
        public MethodInfo method = method;
        public int priority = 400; // Harmony's Priority.Normal
    }
    public sealed class Harmony(string id)
    {
        public string Id { get; } = id;
        public static readonly Dictionary<MethodInfo, (HarmonyMethod Prefix, HarmonyMethod Postfix)> Hooks = new();
        public static bool Fail;
        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (Fail) throw new InvalidOperationException("simulated patch installation failure");
            Hooks[original] = (prefix, postfix);
        }
        public void UnpatchSelf() => Hooks.Clear();
    }
}
namespace SAIN.Components
{
    public class BotComponent { public bool PmcOrScav = true; }
}
namespace SAIN.Components.BotController
{
    // SAIN 4.5.1: public static, no side effects; ExtractLayer.allowedToExtract and
    // TryFindExfilForBot both ask it (tools/SainExtractGuardChecks/Verify-Contracts.ps1).
    public class BotExtractManager
    {
        public static int Calls;
        public static bool IsBotAllowedToExfil(BotComponent bot) { Calls++; return bot.PmcOrScav; }
    }
}
