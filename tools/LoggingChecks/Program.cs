using ArenaMode.Diagnostics;
using BepInEx.Logging;

var events = new List<(LogLevel Level, string Message)>();
using var logger = new ManualLogSource("Arena logging checks");
logger.LogEvent += (_, e) => events.Add((e.Level, e.Data.ToString()!));
ArenaMode.Plugin.ModLogger = logger;
int checks = 0;
void Check(bool okay, string detail)
{
    checks++;
    if (!okay) throw new InvalidOperationException(detail);
}

// Startup before settings are bound must be safe and quiet.
ArenaLog.Developer("unbound");
Check(events.Count == 0 && !ArenaLog.DeveloperEnabled, "unbound developer mode is quiet");
ArenaMode.Config.Settings.DeveloperMode = new();
ArenaLog.Developer("off");
Check(events.Count == 0, "disabled developer mode emits no events");
logger.LogWarning("a real damage failure");
logger.LogError("profile protection failure");
Check(events.SequenceEqual(new[] {
    (LogLevel.Warning, "a real damage failure"),
    (LogLevel.Error, "profile protection failure")
}), "developer mode must not intercept normal warnings or errors");
events.Clear();

ArenaMode.Config.Settings.DeveloperMode.Value = true;
ArenaLog.Developer("movement probe");
Check(ArenaLog.DeveloperEnabled && events.SequenceEqual(new[] {
    (LogLevel.Info, "[dev] movement probe")
}), "enabled probe appears at Info with its developer label");
events.Clear();
logger.LogDebug("ordinary lifecycle detail");
Check(events.Single() == (LogLevel.Debug, "ordinary lifecycle detail"), "normal Debug routing is untouched");
events.Clear();

ArenaMode.Config.Settings.DeveloperMode.Value = false;
ArenaLog.Developer("after disable");
Check(!ArenaLog.DeveloperEnabled && events.Count == 0, "runtime disable takes effect without restart");
ArenaMode.Config.Settings.DeveloperMode.Value = true;
ArenaMode.Plugin.ModLogger = null;
ArenaLog.Developer("shutdown");
Check(events.Count == 0, "missing shutdown logger is safe");
Console.WriteLine($"ARENA_LOGGING_CHECKS_PASSED ({checks})");

namespace ArenaMode
{
    internal static class Plugin { internal static ManualLogSource? ModLogger; }
}
namespace ArenaMode.Config
{
    internal sealed class Toggle { internal bool Value; }
    internal static class Settings { internal static Toggle? DeveloperMode; }
}
