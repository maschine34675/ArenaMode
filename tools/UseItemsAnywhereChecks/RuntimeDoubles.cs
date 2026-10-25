// Explicit inventory/loader boundaries. Harmony itself is the installed binary;
// no Unity/native-game acceptance is claimed by this desktop CLR harness.
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public string GUID { get; } public Version Version { get; }
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); }
    }
    public class PluginInfo { public object Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value; public ConfigEntry(T value) { Value = value; } } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace ArenaMode.Config { public static class Settings { public static BepInEx.Configuration.ConfigEntry<bool> UseItemsAnywhere = new(true); } }
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive = true, Exact = true; public static int ArmEpoch = 1; public static bool IsExactActiveRaid(object game, object world) => Exact; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress, StandDown; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode
{
    // The production build generates this static class; do not accidentally
    // resolve BepInEx.PluginInfo to this containing-namespace name.
    public static class PluginInfo { }
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger { public readonly List<string> Warnings = new(); public void LogWarning(string value) { Warnings.Add(value); Console.WriteLine(value); } public void LogDebug(string value) { } }
}
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { Pockets, TacticalVest, Backpack }
    public enum CommandStatus { Succeed, Failed }
    public class RemoveItemEventArgs { public CommandStatus Status; public string OwnerId; public Item Item; }
    public class Item { public string Id; public object CurrentAddress = new(); public bool NativeReach; public T GetItemComponent<T>() => default; }
    public class CompoundItem : Item { public List<Item> Items = new(); public List<Item> MissingVitalParts = new(); public bool Contains(Item item) => Items.Any(i => ReferenceEquals(i, item) || i is CompoundItem c && c.Contains(item)); }
    public class ContainerCollection : CompoundItem { }
    public class Weapon : CompoundItem { }
    public class Magazine : Item { }
    public class Ammo : Item { }
    public class ThrowWeap : Item { }
    public class Meds : Item { }
    public class FoodDrink : Item { }
    public class PortableRangeFinder : Item { }
    public class Compass : Item { }
    public class RadioTransmitter : Item { }
    public class KnifeComponent { }
    public class Slot { public Item ContainedItem; }
    public class InventoryEquipment
    {
        public ThrowWeap TopPriorityGrenade;
        public Dictionary<EquipmentSlot, Slot> Slots = Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>().ToDictionary(s => s, s => new Slot());
        public Slot GetSlot(EquipmentSlot slot) => Slots[slot];
        public bool Contains(Item item) => Slots.Values.Any(s => ReferenceEquals(s.ContainedItem, item) || s.ContainedItem is CompoundItem c && c.Contains(item));
    }
    public class Inventory { public InventoryEquipment Equipment = new(); }
    public class InventoryController
    {
        public string ID = "local"; public Inventory Inventory = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public bool IsAtBindablePlace(Item item) => item.NativeReach;
        [MethodImpl(MethodImplOptions.NoInlining)] public bool IsAtReachablePlace(Item item) => item.NativeReach;
        public bool Examined(Item item) => true;
        public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> output, Predicate<T> predicate) where T : Item
            => GetAcceptableItemsNonAlloc(new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest }, output, predicate);
        public IEnumerable<T> GetReachableItemsOfType<T>(Predicate<T> predicate) where T : Item
        { var output = new List<T>(); GetReachableItemsOfTypeNonAlloc(output, predicate); return output; }
        public virtual void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] slots, IList<T> output, Predicate<T> predicate) where T : Item
        { foreach (var slot in slots) if (Inventory.Equipment.GetSlot(slot).ContainedItem is ContainerCollection c) foreach (var item in c.Items.OfType<T>()) if (predicate == null || predicate(item)) output.Add(item); }
    }
}
namespace EFT
{
    using EFT.InventoryLogic;
    public class Health { public bool IsAlive = true; }
    public class Player { public bool IsAI, IsYourPlayer = true; public Health HealthController = new(); public InventoryController InventoryController = new(); }
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public enum GameStatus { Started, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public GameStatus Status = GameStatus.Started; }
    public class FirearmHandsInputTranslator
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public List<Magazine> ReloadExternalMagazine(InventoryController controller, Predicate<Magazine> predicate)
        { var result = new List<Magazine>(); controller.GetReachableItemsOfTypeNonAlloc(result, predicate); return result; }
    }
    public class PlayerInputTranslator { public Player _player; [MethodImpl(MethodImplOptions.NoInlining)] public void vmethod_1() { } }
}
namespace EFT.UI
{
    using EFT.InventoryLogic;
    public class AmmoSelector { public class CG_FindAmmoForWeapon { [MethodImpl(MethodImplOptions.NoInlining)] public IEnumerable<Magazine> method_0(InventoryController controller, Predicate<Magazine> predicate) => controller.GetReachableItemsOfType<Magazine>(predicate); } }
    public class ItemUiContext { [MethodImpl(MethodImplOptions.NoInlining)] public IEnumerable<Magazine> FindSuitableMagazine(InventoryController controller, Predicate<Magazine> predicate) => controller.GetReachableItemsOfType<Magazine>(predicate); }
}
namespace EFT.UI.DragAndDrop
{
    using EFT.InventoryLogic;
    public class FastAccessGrenadeItemView { public InventoryController InventoryController; public void SetNewTopPriorityGrenade() { InventoryController.Inventory.Equipment.TopPriorityGrenade = null; } [MethodImpl(MethodImplOptions.NoInlining)] public void OnItemRemoved(RemoveItemEventArgs obj) { } }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefixAttribute : Attribute { } public class PatchPostfixAttribute : Attribute { } public class PatchTranspilerAttribute : Attribute { }
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var methods = GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            HarmonyMethod Pick<T>() where T : Attribute { var method = methods.SingleOrDefault(m => m.GetCustomAttribute<T>() != null); return method == null ? null : new HarmonyMethod(method); }
            new Harmony(GetType().Name).Patch(GetTargetMethod(), Pick<PatchPrefixAttribute>(), Pick<PatchPostfixAttribute>(), Pick<PatchTranspilerAttribute>());
        }
    }
}
