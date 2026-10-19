using System.Reflection;
using System.Collections;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using UnityEngine;

public interface IResult { bool Succeed { get; } }
public class SuccessfulResult : IResult { public bool Succeed => true; }
public class FailedResult(string error) : IResult { public string Error = error; public bool Succeed => false; }
public delegate void Callback(IResult result);
public enum CommandStatus { Begin, Succeed, Failed }
namespace EFT.InventoryLogic.Operations { public interface IInventoryOperation { } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace UnityEngine
{
    public struct Vector3 { }
    public struct Quaternion { }
    public class Transform { public void SetPositionAndRotation(Vector3 p, Quaternion r) { } }
    public class GameObject
    {
        public Transform transform = new();
        public bool Returned;
        public object Component;
        public T GetComponent<T>() where T : class => Component as T;
        public bool Active;
        public void SetActive(bool active) { Active = active; }
    }
    public class BoxCollider { }
}
namespace EFT.CameraControl { public enum ECameraType { Default } }
namespace EFT.AssetsManager
{
    public static class AssetPoolObject { public static void ReturnToPool(GameObject visual) { visual.Returned = true; } }
}
namespace EFT.InventoryLogic
{
    public interface IItemOwner { void RaiseRemoveEvent(RemoveItemEventArgs args); }
    public class ItemAddress(IItemOwner owner, object container = null)
    {
        public object Container = container;
        public IItemOwner GetOwner() => owner; public void RemoveWithoutRestrictions(Item item) => Program.Physical(item);
    }
    public class OwnerItself(IItemOwner owner) : ItemAddress(owner) { }
    public class Item
    {
        public string Id = Guid.NewGuid().ToString();
        public string ShortName = "Weapon";
        public bool ExaminedByDefault = true;
        public ItemAddress Parent;
        public ItemAddress CurrentAddress { get => Parent; set => Parent = value; }
    }
    public class Ammo : Item { public int StackObjectsCount = 1; }
    public class Weapon : Item
    {
        public List<Item> MissingVitalParts = new();
        public List<Item> Contents = new() { new Ammo() };
        public IEnumerable<Item> GetAllItems() => Contents.Prepend(this);
    }
    public class RemoveItemEventArgs { public CommandStatus Status; public Item Item; public ItemAddress From; }
    public class ItemController : IItemOwner
    {
        public event Action<RemoveItemEventArgs> RemoveItemEvent; public void RaiseRemoveEvent(RemoveItemEventArgs args) { RemoveItemEvent?.Invoke(args); }
        public ItemController(Item item, string id, string name) { if (item != null) item.Parent = new OwnerItself(this); }
        public virtual void OutProcess(ItemController executor, Item item, ItemAddress from, ItemAddress to, IInventoryOperation op, Callback callback) => callback(new SuccessfulResult());
        public void Removed(Item item, ItemAddress from) => RemoveItemEvent?.Invoke(new() { Item = item, From = from, Status = CommandStatus.Succeed });
    }
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
    public class Slot(InventoryController owner)
    {
        public Item ContainedItem;
        public bool Compatible = true;
        public ItemAddress CreateItemAddress() => new(owner, this);
    }
    public class Equipment(InventoryController owner)
    {
        public Dictionary<EquipmentSlot, Slot> Slots = Enum.GetValues<EquipmentSlot>().ToDictionary(x => x, _ => new Slot(owner));
        public Slot GetSlot(EquipmentSlot slot) => Slots[slot];
    }
    public class Inventory(InventoryController owner) { public Equipment Equipment = new(owner); }
    public class ItemsCount(Item item, int destroy, int preserve)
    { public Item Item=item; public int NumberToDestroy=destroy, NumberToPreserve=preserve; }
    public class Plan { public Weapon Item; public ItemAddress From, To; public IEnumerable<ItemsCount> ItemsToDestroy; }
    public class PlanResult { public bool Failed; public Plan Value; public object Error; }
    public static class ItemManipulator
    {
        public static int Simulations;
        public static bool DestroyOnMove;
        public static object Rejection;
        public static PlanResult Move(Item item, ItemAddress to, ItemController owner, bool simulate = false)
        {
            if (!simulate) throw new Exception("native simulation required before submission");
            Simulations++;
            var slot = (Slot)to.Container;
            object error = Rejection ?? (((InventoryController)owner).Examined(item) ? null : new InstallNotExaminedError());
            return new() { Failed = error != null || !slot.Compatible || slot.ContainedItem != null, Error = error, Value = new()
                { Item = (Weapon)item, From = item.Parent, To = to, ItemsToDestroy = new[] { new ItemsCount(item, DestroyOnMove ? 1 : 0, DestroyOnMove ? 0 : 1) } } };
        }
    }
    public class InstallNotExaminedError { }
    public class InventoryController : ItemController
    {
        public Inventory Inventory;
        public bool IsChangingWeapon, Allowed = true, ThrowSubmission;
        public int Submitted, Moves;
        public Plan Pending;
        public Callback Completion;
        public InventoryController() : base(null, "", "") { Inventory = new(this); }
        public bool CanExecute(Plan plan) => Allowed;
        public bool Examined(Item item)
        {
            bool result = item.ExaminedByDefault;
            ArenaMode.Pickups.ArenaPickupExamination.Apply(this, item, ref result);
            return result;
        }
        public void RunNetworkTransaction(Plan plan, Callback callback)
        {
            Submitted++;
            Pending = plan;
            Completion = callback;
            if (ThrowSubmission) throw new Exception("queued submission fault");
        }
        // Models native SinglePlayer's deferred execution; source owner OutProcess
        // runs after queue admission, then Move and InProcess without another yield.
        public bool BeginMove()
        {
            bool accepted = false;
            ((ItemController)Pending.From.GetOwner()).OutProcess(this, Pending.Item, Pending.From, Pending.To, null,
                result => accepted = result.Succeed);
            if (!accepted) return false;
            Moves++;
            ((Slot)Pending.To.Container).ContainedItem = Pending.Item;
            Pending.Item.Parent = Pending.To;
            return true;
        }
        public void Finish(bool success = true, bool emitRemove = true)
        {
            if (success && emitRemove && ReferenceEquals(Pending.Item.Parent.GetOwner(), this))
                ((ItemController)Pending.From.GetOwner()).Removed(Pending.Item, Pending.From);
            Completion(success ? new SuccessfulResult() : new FailedResult("refused"));
        }
        public void RollBack()
        {
            ((Slot)Pending.To.Container).ContainedItem = null;
            Pending.Item.Parent = Pending.From;
        }
    }
}
namespace EFT.Interactive
{
    public class LootItem
    {
        public Item Item;
        public ItemController ItemOwner;
        public GameObject gameObject;
        public bool PhysicsStopped;
        public void StopPhysics() => PhysicsStopped = true;
    }
}
namespace EFT
{
    public class Profile { }
    public class Hands { public bool Supported = true; public bool SupportPickup() => Supported; }
    public class Player
    {
        public class SinglePlayerInventoryController : InventoryController { }
        public bool IsAI, Live = true;
        public InventoryController InventoryController = new SinglePlayerInventoryController();
        public Hands HandsController = new();
        public Slot ActiveSlot;
        public Profile Profile = new();
    }
    public class ObjectsFactory
    {
        public int Prefabs;
        public bool NullPrefab;
        public GameObject CreateLootPrefab(Item item, CameraControl.ECameraType camera)
        {
            if (!ArenaMode.Kits.KitBundlePreloader.Done) throw new Exception("preload order violated");
            Prefabs++;
            return NullPrefab ? null : new();
        }
    }
    public interface IBotGame { } public class AbstractGame { } public class LocalGame : AbstractGame, IBotGame { public GameWorld GameWorld; } public class HideoutGame : AbstractGame, IBotGame { } public class GameWorld
    {
        public Player MainPlayer = new();
        public int Created, Destroyed;
        public bool ThrowAfterRegistration;
        public Interactive.LootItem Loot;
        public Interactive.LootItem CreateLootWithRigidbody(GameObject visual, Item item, string name, bool rotation,
            object profiles, out BoxCollider collider, bool sync, bool validation)
        {
            if (sync || !validation) throw new Exception("native validation must remain enabled");
            Created++;
            collider = new();
            Loot = new() { gameObject = visual, Item = item, ItemOwner = (ItemController)item.Parent.GetOwner() };
            visual.Component = Loot;
            var captured = Loot;
            captured.ItemOwner.RemoveItemEvent += args => { if (args.Status == CommandStatus.Succeed) DestroyLoot(captured); };
            if (ThrowAfterRegistration) throw new Exception("native setup after registration failed");
            return Loot;
        }
        public void DestroyLoot(Interactive.LootItem loot) { Destroyed++; loot.gameObject.Returned = true; }
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static Logger ModLogger = new(); }
    public class Logger
    {
        public List<string> Messages = new(); public List<string> Warnings = new();
        public void LogWarning(string message) { Messages.Add(message); Warnings.Add(message); }
        public void LogInfo(string message) => Messages.Add(message); public void LogDebug(string message) => Messages.Add(message);
    }
}
namespace ArenaMode.Kits
{
    public class KitDefinition { }
    public static class KitBundlePreloader
    {
        public static bool Done;
        public static IEnumerator Preload(KitDefinition kit) { yield return null; Done = true; }
    }
    public static class KitBuilder
    {
        public static Weapon Next;
        public static int Built, Registered;
        public static Weapon BuildPrimaryWeapon(KitDefinition kit, int spares, out List<Item> spareItems)
        {
            if (!KitBundlePreloader.Done) throw new Exception("preload precedes build");
            if (spares != 0) throw new Exception("no unplaced spare ammo");
            Built++;
            spareItems = new();
            return Next;
        }
        public static void RegisterAsChecked(EFT.Profile profile, IEnumerable<Item> items) { Registered++; }
    }
}
namespace ArenaMode.Pickups
{
    public static class ArenaPickupRuntime
    {
        public static int Generation;
        public static bool ScopeLive = true;
        public static bool IsLiveActor(EFT.Player player) => ScopeLive && player.Live;
    }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive = true; } }

namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    {
        public string GUID => guid;
        public string Name => name;
        public Version Version => new(version);
    }
    public sealed class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Configuration { public class ConfigEntryBase { } }
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(); }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailPatch, FailUnpatch, DropReceipt;
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (!DropReceipt) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (FailPatch) throw new InvalidOperationException("injected partial-patch failure");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values) info.Prefixes.RemoveAll(p => p.owner == id);
        }
        public static PatchInfo GetPatchInfo(MethodBase target) => Bound.GetValueOrDefault(target);
    }
    public static class AccessTools
    {
        public static bool MissingSettings;
        public static FieldInfo Field(Type type, string name)
            => MissingSettings ? null : type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace UnityEngine { public class MonoBehaviour { } }
namespace ArenaMode.Pickups
{
    internal static class ArenaPickupExamination
    {
        internal static void Remember(IEnumerable<EFT.InventoryLogic.Item> items, EFT.Player player, EFT.GameWorld world, int generation) { }
        internal static void Apply(EFT.InventoryLogic.InventoryController owner, EFT.InventoryLogic.Item item, ref bool result) { }
    }
}