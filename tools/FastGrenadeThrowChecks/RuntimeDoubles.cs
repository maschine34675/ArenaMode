using System.Runtime.CompilerServices;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID { get; } public Version Version { get; } public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); } }
    public class BaseUnityPlugin { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive, Exact = true; public static bool IsExactActiveRaid(object game, object world) => Exact; } }
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress, StandDown; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode.Combat { public static class WeaponSwapService { public static bool IsSwapping; } }
namespace ArenaMode { public static class Plugin { public static Logger ModLogger = new(); } public class Logger { public int Warnings; public void LogWarning(string message) { Warnings++; Console.WriteLine(message); } public void LogDebug(string message) { } } }
namespace EFT.InventoryLogic
{
    public class ThrowWeap { public string Id; public object CurrentAddress = new(); public bool Examined = true; }
    public class Equipment { public ThrowWeap TopPriorityGrenade; public List<ThrowWeap> Contents = new(); public bool Contains(ThrowWeap item) => Contents.Contains(item); }
    public class Inventory { public Equipment Equipment = new(); }
    public class InventoryController { public Inventory Inventory = new(); public List<ThrowWeap> Candidates = new(); public bool Examined(ThrowWeap item) => item.Examined; }
    public static class InventoryExtension { public static List<ThrowWeap> GetThrowablePriorityGrenadesList(InventoryController inventory) => inventory.Candidates; }
}
namespace EFT
{
    public class Player { public bool IsYourPlayer = true, IsAI; public Health HealthController = new(); public InventoryLogic.InventoryController InventoryController = new(); }
    public class Health { public bool IsAlive = true; }
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public Player LocalPlayer; }
}
public static class ForeignCalls
{
    public static EFT.InventoryLogic.ThrowWeap Last; public static int Throws; public static bool Low;
    [MethodImpl(MethodImplOptions.NoInlining)] public static EFT.InventoryLogic.ThrowWeap Find(EFT.Player player) => player.InventoryController.Candidates.FirstOrDefault();
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Record(EFT.InventoryLogic.ThrowWeap grenade, bool low) { Last = grenade; Low = low; Throws++; }
}
