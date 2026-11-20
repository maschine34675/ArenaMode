// Only Arena-owned state is replaced; the real game/Unity/BepInEx/Harmony APIs must compile.
namespace ArenaMode.Session { internal static class ArenaSession { internal static bool IsActive => false; internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => false; } }
namespace ArenaMode { internal static class Plugin { internal const string PluginGuid = "com.maschine.ArenaMode"; internal static readonly BepInEx.Logging.ManualLogSource ModLogger = new("native-contract-compile"); } }
