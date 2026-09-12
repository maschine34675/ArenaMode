// Contract fakes, not Unity/runtime proof. Native references are documented in
// CORPSE-CLEANUP-REVIEW.md. In particular, physical removal, activity pairing
// and reactive slot/model release are deliberately separate observable steps.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public record struct Vector3(float x, float y = 0, float z = 0)
    {
        public static float Distance(Vector3 a, Vector3 b)
            => MathF.Sqrt((a.x-b.x)*(a.x-b.x) + (a.y-b.y)*(a.y-b.y) + (a.z-b.z)*(a.z-b.z));
    }
    public class Transform { public Vector3 position = new(50); }
    public class GameObject
    {
        public readonly List<object> Components = new();
        public bool FailComponentRead;
        public T[] GetComponentsInChildren<T>(bool includeInactive)
            => FailComponentRead ? throw new Exception("component read failed") : Components.OfType<T>().ToArray();
    }
    public class Component : Object
    {
        public GameObject gameObject = new();
        public Transform transform = new();
        public T GetComponent<T>() where T : class => gameObject.Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Renderer : Component { public bool forceRenderingOff; }
    public class Collider : Component { public bool enabled = true; }
    public static class Time { public static float time; }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT.InventoryLogic
{
    public enum CommandStatus { Begin, Succeed, Failed }
    public class Item
    {
        public string TemplateId = "test-template";
        public Slot Parent;
        public readonly List<Item> Children = new();
        public bool RejectRemoval, ThrowBeforeRemoval, ThrowAfterRemoval, ThrowBeginNotification, ThrowSuccessNotification;
    }
    public class InventoryEquipment : Item { public readonly List<Slot> Slots = new(); }
    public class Slot
    {
        public string ID;
        public Item ContainedItem;
        public Item ReactiveItem;
        public UnityEngine.Renderer Model;
        public UnityEngine.GameObject Body;
        public int Returns;
        public void ApplyContainedItem()
        {
            ReactiveItem = ContainedItem;
            if (ReactiveItem == null && Model != null)
            {
                Returns++;
                Body.Components.Remove(Model);
                Model.forceRenderingOff = false; // native pool restoration
                Model = null;
            }
        }
    }
    public record ItemEventArgs(Item Item, CommandStatus Status);
    public record RemoveItemEventArgs(Item Item, CommandStatus Status) : ItemEventArgs(Item, Status);
    public class ItemController
    {
        public Item RootItem;
        public readonly List<ItemEventArgs> ActiveEvents = new();
        public readonly List<(Item Item, CommandStatus Status)> Notifications = new();
        public readonly List<Item> Forgotten = new();
        public readonly List<string> Stages = new();
        public int MissingBegin, Attempts, RemoveCalls, ResizeNotifications;
        public IEnumerable<ItemEventArgs> SelectEvents(Item item)
            => ActiveEvents.Where(e => ReferenceEquals(e.Item, item));
        public void RaiseRemoveEvent(RemoveItemEventArgs args)
        {
            // ItemController.ProcessActivity pairs by exact event type + item.
            if (args.Status == CommandStatus.Begin) ActiveEvents.Add(args);
            else
            {
                var prior = ActiveEvents.FirstOrDefault(e => e.GetType() == args.GetType() && ReferenceEquals(e.Item, args.Item));
                if (prior != null) ActiveEvents.Remove(prior);
                else MissingBegin++;
            }
            Stages.Add(args.Status.ToString());
            Notifications.Add((args.Item, args.Status));
        }
    }
    public class RemoveResult
    {
        private readonly Item _item;
        private readonly Slot _originalSlot;
        public RemoveResult(Item item, Slot originalSlot) { _item = item; _originalSlot = originalSlot; }
        public void RaiseEvents(ItemController controller, CommandStatus status)
        {
            // The retained address still belongs to the corpse after detachment.
            controller.RaiseRemoveEvent(new RemoveItemEventArgs(_item, status));
            // A raw parent ChildrenChanged observer/cache enumeration can throw
            // after activity registration, unlike native SafeRemoveItemEvent.
            if (status == CommandStatus.Begin && _item.ThrowBeginNotification) throw new Exception("begin observer failed");
            if (status == CommandStatus.Succeed && _item.ThrowSuccessNotification) throw new Exception("success observer failed");
            if (status == CommandStatus.Succeed) _originalSlot.ApplyContainedItem();
            controller.ResizeNotifications++;
            if (status == CommandStatus.Succeed) controller.Forgotten.Add(_item);
        }
    }
    public record OperationResult(bool Succeeded, RemoveResult Value = null, string Error = null);
    public static class ItemManipulator
    {
        public static OperationResult RemoveWithoutRestrictions(Item item, ItemController controller)
        {
            controller.Attempts++;
            if (item.ThrowBeforeRemoval) throw new Exception("before removal");
            if (item.RejectRemoval) return new(false, Error: "native removal refused");
            var slot = item.Parent ?? throw new Exception("missing address");
            slot.ContainedItem = null;
            item.Parent = null;
            controller.RemoveCalls++;
            controller.Stages.Add("detach");
            // Slot.OnAddOrRemoveItem is invoked after the mutation in vanilla.
            if (item.ThrowAfterRemoval) throw new Exception("slot observer failed");
            return new(true, new RemoveResult(item, slot));
        }
    }
}
namespace EFT
{
    public class Player : UnityEngine.Component
    {
        protected Interactive.Corpse Corpse;
        public void SetCorpse(Interactive.Corpse corpse) => Corpse = corpse;
        public bool enabled = true;
        public string ProfileId = "local-player";
        public UnityEngine.Vector3 Position;
        public object InteractableObject;
    }
    public class GameWorld
    {
        public Player MainPlayer = new();
        public readonly List<Interactive.Corpse> LootList = new();
    }
    public class IBotGame { public BotsController BotsController; }
    public class BotsController { public BotSpawner BotSpawner; }
    public class BotSpawner { public DeadBodiesController _deadBodiesController; }
    public class DeadBody { public Player Player; }
    public class DeadBodiesController
    {
        public readonly List<DeadBody> Bodies = new();
        public void RemoveBody(DeadBody body) => Bodies.Remove(body);
    }
}
namespace EFT.AssetsManager
{
    public class AssetPoolObject
    {
        public UnityEngine.Collider[] Colliders = Array.Empty<UnityEngine.Collider>();
        public void StoreCollider(UnityEngine.Collider collider) { }
    }
}
namespace EFT.Interactive
{
    public class Ragdoll { public bool _isPhysicsDone = true; }
    public class Bindable<T> { public T Value; }
    public class Corpse : UnityEngine.Component
    {
        public bool HasRagdoll = true;
        public Ragdoll Ragdoll = new();
        public string PlayerProfileID = "opponent";
        public InventoryLogic.ItemController ItemOwner = new();
        public InventoryLogic.Item Item;
        public Bindable<InventoryLogic.Item> ItemInHands = new();
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static Logger ModLogger = new(); }
    public class Logger
    {
        public readonly List<string> Warnings = new();
        public void LogInfo(string text) { }
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Config
{
    public class Setting<T> { public T Value; }
    public static class Settings
    {
        public static Setting<int> MaxCorpses = new() { Value = 1 };
        public static Setting<bool> StripCorpseGear = new() { Value = true };
    }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive; } }
namespace ArenaMode.Loot
{
    public static class OpenLootTracker
    {
        public static object Open;
        public static bool IsOpen(object item) => Open != null && ReferenceEquals(Open, item);
    }
}
namespace ArenaMode.Compat
{
    public static class PitFireTeamCompat
    {
        public static string TeamProfile;
        public static bool IsTeamProfileId(string id) => TeamProfile != null && id == TeamProfile;
    }
}
namespace ArenaMode.Bots
{
    public static class CorpseWeaponRegistry
    {
        public static UnityEngine.GameObject Get(EFT.Interactive.Corpse corpse) => null;
    }
}
