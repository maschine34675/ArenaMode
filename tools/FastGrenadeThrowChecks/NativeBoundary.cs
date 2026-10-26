// Only Arena-owned policy values are substituted. Every EFT, Unity, BepInEx and
// Harmony member used by the adapter is checked against actual installed DLLs.
namespace ArenaMode.Session { internal static class ArenaSession { internal static bool IsActive => false; internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => false; } }
namespace ArenaMode.Hud { internal static class ArenaInputBlocker { internal static bool Blocked => false; } }
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { internal static bool IsRespawnInProgress => false; internal static bool StandDown => false; } }
namespace ArenaMode.Spectator { internal static class ArenaSpectatorService { internal static bool IsActive => false; } }
namespace ArenaMode.Combat { internal static class WeaponSwapService { internal static bool IsSwapping => false; } }
namespace ArenaMode { internal static class Plugin { internal static readonly BepInEx.Logging.ManualLogSource ModLogger = new("native-contract-compile"); } }
