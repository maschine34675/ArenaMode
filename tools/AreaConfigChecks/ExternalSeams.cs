// No configuration doubles: ConfigFile, ConfigEntry, AcceptableValueList,
// KeyboardShortcut and Unity enums/structs all come from installed assemblies.
// The unrelated Gun Game preload constant uses the existing catalog seam.
public enum BotDifficulty { easy,normal,hard,impossible }
namespace ArenaMode.Session
{ public static class ArenaSession { public static bool IsActive,ArmedForNextRaid; } }
namespace ArenaMode.Config
{ public static class SettingsSync { public static int Reports;public static void Report()=>Reports++; } }
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger=new(); }
    public sealed class Logger
    {
        public readonly List<string> Warnings=new(),Info=new();
        public void LogWarning(string message)=>Warnings.Add(message);
        public void LogInfo(string message)=>Info.Add(message);
    }
}
