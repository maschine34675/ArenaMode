using System.Collections;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

// Only the mod-owned boundary is replaced. ArenaWeaponPickup itself compiles
// against the installed native EFT/Unity/Comfort assemblies in this project.
namespace ArenaMode
{
    internal static class Plugin { public static Logger ModLogger = new Logger(); }
    internal class Logger { public void LogWarning(string message) { } public void LogInfo(string message) { } }
}
namespace ArenaMode.Session { internal static class ArenaSession { public static bool IsActive => false; } }
namespace ArenaMode.Kits
{
    public class KitDefinition { }
    public static class KitBuilder
    {
        public static Weapon BuildPrimaryWeapon(KitDefinition kit, int count, out List<Item> spares) { spares = null; return null; }
        public static void RegisterAsChecked(Profile profile, IEnumerable<Item> items) { }
    }
    public static class KitBundlePreloader { public static IEnumerator Preload(KitDefinition kit) { yield break; } }
}
namespace ArenaMode.Pickups
{
    internal static class ArenaPickupRuntime
    {
        public static int Generation => 0;
        public static bool IsLiveActor(Player player) => false;
    }
}
