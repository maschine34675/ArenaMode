using System.Collections;
using System.Collections.Generic;
#pragma warning disable CS0649 // Deliberately narrow, non-executed root integration boundary.
namespace ArenaMode.Session
{
    internal static class ArenaSession { public static bool IsActive,ArmedForNextRaid; public static int ArmEpoch; }
    internal static class ArenaServerClient { public static bool TranslocatorAvailableForConfirmedRaid; }
}
namespace ArenaMode.Modes
{
    internal class ArenaMatch { }
    internal static class ArenaMatchService { public static ArenaMatch Current; }
    internal static class ArenaToasts { public static void Show(string message) { } }
}
namespace ArenaMode.Kits
{
    internal class KitDefinition { public string Id; public KitFill Fill; }
    internal class KitFill { public List<KitStackedItem> RigContents; }
    internal class KitStackedItem { public string Tpl;public int Count; }
    internal static class KitBundlePreloader
    {
        public static bool IsPreloading;
        public static bool IsTemplateWarmed(string tpl)=>false;
        public static IEnumerator Preload(KitDefinition kit) { yield break; }
    }
}
namespace ArenaMode.Combat { internal static class WeaponSwapService { public static bool IsSwapping; } }
namespace ArenaMode.Combat.Translocator
{
    internal static class ArenaTranslocatorRuntime
    {
        public static bool IsLivePlayer(EFT.Player player)=>false;
        public static void Primary(EFT.Player player,EFT.InventoryLogic.Item item) { }
        public static void Secondary(EFT.Player player,EFT.InventoryLogic.Item item) { }
    }
}
namespace ArenaMode.Hud { internal static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode
{
    internal static class Plugin { public const string PluginGuid="device-api-check"; public static readonly Log ModLogger = new(); }
    internal class Log { public void LogWarning(string message) { } public void LogDebug(string message) { } }
}
