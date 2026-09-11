using System.Reflection;

// A small model of the SPT 4.1 inventory, quick-throw view and G key, written
// from ../_decompiled (line numbers in the comments). Only what the pick chain
// touches is modelled; Verify-InstalledContracts.ps1 pins every name, signature
// and native call this model relies on against the installed Assembly-CSharp.

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public ConfigEntry(T value) => Value = value;
        public T Value { get; set; }
    }
}

namespace ArenaMode.Config
{
    using BepInEx.Configuration;

    public static class Settings
    {
        public static ConfigEntry<bool> UseItemsAnywhere { get; } = new(true);
    }
}

namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive { get; set; }

        /// <summary>Counts armings; one per arena raid (internal in production).</summary>
        public static int ArmEpoch { get; set; }
    }
}

namespace ArenaMode
{
    public static class Plugin
    {
        public static TestLogger ModLogger { get; } = new();
    }

    public sealed class TestLogger
    {
        public List<string> Warnings { get; } = new();
        public void LogWarning(object value) => Warnings.Add(value?.ToString() ?? string.Empty);
        public void LogInfo(object value) { }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }
}

namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPostfixAttribute : Attribute { }

    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();

        // SPT's ModulePatch.Enable throws on a null target; so does this.
        public void Enable()
        {
            MethodBase target = GetTargetMethod()
                ?? throw new InvalidOperationException($"{GetType().Name}: TargetMethod is null");
            PatchHost.Register(target, GetType());
        }
    }
}

/// <summary>
/// Runs prefixes, the modelled native body and postfixes, binding patch
/// parameters the way Harmony does: __instance, ref __result, original
/// parameters by name, and ___field injection resolved through the declaring
/// type's hierarchy. A binding Harmony would refuse at patch time is refused
/// at registration here too.
/// </summary>
public static class PatchHost
{
    private static readonly Dictionary<MethodBase, List<MethodInfo>> Prefixes = new();
    private static readonly Dictionary<MethodBase, List<MethodInfo>> Postfixes = new();
    private static readonly Stack<Type> Active = new();

    public static List<(MethodBase Target, Type Patch)> Registered { get; } = new();
    public static HashSet<Type> Suppressed { get; } = new();

    /// <summary>Makes registration of a matching patch fail, as a drifted target does in SPT.</summary>
    public static Func<Type, bool> FailRegistration { get; set; }

    /// <summary>A patch class currently executing, for fault injection aimed at one patch.</summary>
    public static bool IsRunning(Func<Type, bool> which) => Active.Any(which);

    public static void Reset()
    {
        Prefixes.Clear();
        Postfixes.Clear();
        Registered.Clear();
        Suppressed.Clear();
        Active.Clear();
        FailRegistration = null;
    }

    public static void Register(MethodBase target, Type patchType)
    {
        if (FailRegistration?.Invoke(patchType) == true)
            throw new InvalidOperationException($"{patchType.Name}: controlled patch failure");
        Registered.Add((target, patchType));
        foreach (MethodInfo method in patchType.GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<SPT.Reflection.Patching.PatchPrefixAttribute>() != null)
                Add(Prefixes, target, method);
            if (method.GetCustomAttribute<SPT.Reflection.Patching.PatchPostfixAttribute>() != null)
                Add(Postfixes, target, method);
        }
    }

    /// <summary>
    /// Registers a patch class that is not production code (the third-party
    /// UseItemsAnywhere model) either before or after what is already there.
    /// </summary>
    public static void RegisterExternal(Type patchType, bool first)
    {
        foreach (MethodInfo method in patchType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<SPT.Reflection.Patching.PatchPostfixAttribute>() == null)
                continue;
            var target = (MethodBase)method.GetCustomAttribute<ExternalTargetAttribute>()!.Resolve();
            Validate(target, method);
            if (!Postfixes.TryGetValue(target, out var list))
                Postfixes[target] = list = new List<MethodInfo>();
            if (first)
                list.Insert(0, method);
            else
                list.Add(method);
        }
    }

    private static void Add(Dictionary<MethodBase, List<MethodInfo>> map, MethodBase target, MethodInfo patch)
    {
        Validate(target, patch);
        if (!map.TryGetValue(target, out var list))
            map[target] = list = new List<MethodInfo>();
        list.Add(patch);
    }

    private static void Validate(MethodBase target, MethodInfo patch)
    {
        ParameterInfo[] original = target.GetParameters();
        foreach (ParameterInfo parameter in patch.GetParameters())
        {
            string name = parameter.Name!;
            if (name == "__instance" || name == "__result")
                continue;
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                if (FindField(target.DeclaringType!, name.Substring(3)) == null)
                    throw new InvalidOperationException($"No such field defined in class {target.DeclaringType}: {name}");
                continue;
            }
            if (original.All(p => p.Name != name))
                throw new InvalidOperationException($"Parameter \"{name}\" not found in method {target}");
        }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    public static void Run(MethodBase target, object instance, object[] args, Action body)
    {
        object result = null;
        if (RunPrefixes(target, instance, args, ref result))
            body();
        RunPostfixes(target, instance, args, ref result);
    }

    public static T Run<T>(MethodBase target, object instance, object[] args, Func<T> body)
    {
        object result = default(T);
        if (RunPrefixes(target, instance, args, ref result))
            result = body();
        RunPostfixes(target, instance, args, ref result);
        return (T)result;
    }

    private static bool RunPrefixes(MethodBase target, object instance, object[] args, ref object result)
    {
        bool runOriginal = true;
        if (!Prefixes.TryGetValue(target, out var list))
            return true;
        foreach (MethodInfo prefix in list.ToList())
        {
            if (Suppressed.Contains(prefix.DeclaringType!))
                continue;
            object returned = Invoke(prefix, target, instance, args, ref result);
            if (returned is bool keep && !keep)
                runOriginal = false;
        }
        return runOriginal;
    }

    private static void RunPostfixes(MethodBase target, object instance, object[] args, ref object result)
    {
        if (!Postfixes.TryGetValue(target, out var list))
            return;
        foreach (MethodInfo postfix in list.ToList())
        {
            if (!Suppressed.Contains(postfix.DeclaringType!))
                Invoke(postfix, target, instance, args, ref result);
        }
    }

    private static object Invoke(MethodInfo patch, MethodBase target, object instance, object[] args, ref object result)
    {
        ParameterInfo[] original = target.GetParameters();
        ParameterInfo[] parameters = patch.GetParameters();
        object[] values = new object[parameters.Length];
        int resultIndex = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            string name = parameters[i].Name!;
            if (name == "__instance")
                values[i] = instance;
            else if (name == "__result")
            {
                values[i] = result;
                resultIndex = i;
            }
            else if (name.StartsWith("___", StringComparison.Ordinal))
                values[i] = FindField(target.DeclaringType!, name.Substring(3))!.GetValue(instance);
            else
                values[i] = args[Array.FindIndex(original, p => p.Name == name)];
        }

        Active.Push(patch.DeclaringType!);
        try
        {
            object returned = patch.Invoke(null, values);
            if (resultIndex >= 0 && parameters[resultIndex].ParameterType.IsByRef)
                result = values[resultIndex];
            return returned;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Harmony does not wrap: the patch's own exception reaches the caller.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        finally
        {
            Active.Pop();
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ExternalTargetAttribute : Attribute
{
    private readonly Type _type;
    private readonly string _name;
    public ExternalTargetAttribute(Type type, string name)
    {
        _type = type;
        _name = name;
    }
    public MethodInfo Resolve() => _type.GetMethod(_name)!;
}

/// <summary>Which third-party mods the modelled install carries.</summary>
public static class Install
{
    /// <summary>
    /// UseItemsAnywhere 1.4.0 (../_reference/UseItemsAnywhere-1.4.0): rewrites
    /// Inventory.FastAccessSlots at startup, and its GrenadeThrowingSlots and
    /// GetThrowablePriorityGrenadesList prefixes add Backpack, SecuredContainer
    /// and ArmBand. Its two place postfixes are modelled as a patch class.
    /// </summary>
    public static bool UseItemsAnywhere { get; set; }
}

namespace EFT.InventoryLogic
{
    public enum CommandStatus
    {
        Begin,
        Succeed,
        Failed,
    }

    public enum EquipmentSlot
    {
        FirstPrimaryWeapon,
        SecondPrimaryWeapon,
        Holster,
        Scabbard,
        Backpack,
        SecuredContainer,
        TacticalVest,
        ArmorVest,
        Pockets,
        ArmBand,
    }

    // Only the ordering matters: InventoryExtension sorts by ThrowType.
    public enum ThrowWeapType
    {
        frag_grenade,
        flash_grenade,
        stun_grenade,
        smoke_grenade,
    }

    public interface IContainer
    {
        Item ParentItem { get; }
        IEnumerable<Item> Items { get; }
    }

    public abstract class ItemAddress
    {
        public abstract IContainer Container { get; }
    }

    public sealed class GridItemAddress : ItemAddress
    {
        public GridItemAddress(Grid grid) => Grid = grid;
        public Grid Grid { get; }
        public override IContainer Container => Grid;
    }

    public sealed class SlotItemAddress : ItemAddress
    {
        public SlotItemAddress(Slot slot) => Slot = slot;
        public Slot Slot { get; }
        public override IContainer Container => Slot;
    }

    public sealed class Grid : IContainer
    {
        public Grid(CompoundItem parent) => ParentItem = parent;
        public Item ParentItem { get; }
        public List<Item> Contents { get; } = new();
        public IEnumerable<Item> Items => Contents;
    }

    public sealed class Slot : IContainer
    {
        public Slot(CompoundItem parent) => ParentItem = parent;
        public Item ParentItem { get; }
        public Item ContainedItem { get; set; }
        public IEnumerable<Item> Items => ContainedItem == null ? Array.Empty<Item>() : new[] { ContainedItem };
    }

    public sealed class KnifeComponent { }

    public class Item
    {
        public Item(string id) => Id = id;
        public string Id { get; }
        public ItemAddress CurrentAddress { get; set; }
        public ItemAddress Parent => CurrentAddress;
        public T GetItemComponent<T>() where T : class => null;

        // ItemExtensions.GetAllParentItems: walk up through each container's parent.
        public IEnumerable<Item> GetAllParentItems()
        {
            for (ItemAddress address = CurrentAddress; address != null; )
            {
                Item parent = address.Container.ParentItem;
                yield return parent;
                address = parent.CurrentAddress;
            }
        }

        public override string ToString() => Id;
    }

    public class CompoundItem : Item
    {
        public CompoundItem(string id) : base(id) { }
        public List<Grid> Grids { get; } = new();
        public List<Item> MissingVitalPartsList { get; } = new();
        public IEnumerable<Item> MissingVitalParts => MissingVitalPartsList;
        public virtual IEnumerable<IContainer> Containers => Grids;

        public Grid AddGrid()
        {
            var grid = new Grid(this);
            Grids.Add(grid);
            return grid;
        }

        // ItemExtensions.GetAllItemsFromCollection: the root, then everything below it.
        public IEnumerable<Item> GetAllItemsDeep()
        {
            yield return this;
            foreach (IContainer container in Containers)
            {
                foreach (Item item in container.Items)
                {
                    if (item is CompoundItem compound)
                    {
                        foreach (Item inner in compound.GetAllItemsDeep())
                            yield return inner;
                    }
                    else
                        yield return item;
                }
            }
        }
    }

    public class Weapon : CompoundItem
    {
        public Weapon(string id) : base(id) { }
    }

    public class ThrowWeap : Item
    {
        public ThrowWeap(string id, ThrowWeapType type) : base(id) => ThrowType = type;
        public ThrowWeapType ThrowType { get; }
    }

    public class Meds : Item
    {
        public Meds(string id) : base(id) { }
    }

    public class FoodDrink : Item
    {
        public FoodDrink(string id) : base(id) { }
    }

    public class PortableRangeFinder : Item
    {
        public PortableRangeFinder(string id) : base(id) { }
    }

    public class Compass : Item
    {
        public Compass(string id) : base(id) { }
    }

    public class RadioTransmitter : Item
    {
        public RadioTransmitter(string id) : base(id) { }
    }

    public sealed class InventoryEquipment : CompoundItem
    {
        private readonly Dictionary<EquipmentSlot, Slot> _slots = new();

        public InventoryEquipment(string id) : base(id)
        {
            foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
                _slots[slot] = new Slot(this);
        }

        // InventoryEquipment.cs:76 — a public field, written by the view and hold-G.
        public ThrowWeap TopPriorityGrenade;

        public override IEnumerable<IContainer> Containers => _slots.Values;
        public Slot GetSlot(EquipmentSlot slot) => _slots[slot];

        // InventoryEquipment.cs:124-142; UseItemsAnywhere's GrenadeThrowingSlotsPatch.
        public IReadOnlyList<Slot> GrenadeThrowingSlots => Install.UseItemsAnywhere
            ? new[] { GetSlot(EquipmentSlot.Backpack), GetSlot(EquipmentSlot.TacticalVest), GetSlot(EquipmentSlot.Pockets),
                      GetSlot(EquipmentSlot.SecuredContainer), GetSlot(EquipmentSlot.ArmBand) }
            : new[] { GetSlot(EquipmentSlot.TacticalVest), GetSlot(EquipmentSlot.Pockets) };
    }

    public static class ItemExtensions
    {
        // ItemExtensions.cs:686-696 — deep: any parent on the way up.
        public static bool Contains(this CompoundItem container, Item item)
            => item.GetAllParentItems().Any(parent => ReferenceEquals(parent, container));
    }

    public sealed class Inventory
    {
        public Inventory(InventoryEquipment equipment) => Equipment = equipment;
        public InventoryEquipment Equipment { get; set; }

        // Inventory.cs:174-182.
        public static readonly EquipmentSlot[] BindAvailableSlotsExtended =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster,
            EquipmentSlot.Scabbard, EquipmentSlot.Pockets, EquipmentSlot.TacticalVest,
        };

        // Inventory.cs:193-197; UseItemsAnywhere's Plugin.Awake rewrites the static.
        public static EquipmentSlot[] FastAccessSlots => Install.UseItemsAnywhere
            ? new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer, EquipmentSlot.ArmBand }
            : new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest };

        // Inventory.cs:327-331 — the slot items plus everything inside them.
        public IEnumerable<Item> GetItemsInSlots(IEnumerable<EquipmentSlot> slots)
        {
            Item[] top = slots.Select(slot => Equipment.GetSlot(slot).ContainedItem).ToArray();
            return top.Concat(top.OfType<CompoundItem>().SelectMany(c => c.GetAllItemsDeep()));
        }
    }

    public class ItemEventArgs : EventArgs
    {
        public ItemEventArgs(Item item, CommandStatus status, string ownerId, ItemAddress location)
        {
            Item = item;
            Status = status;
            OwnerId = ownerId;
            Location = location;
        }

        public readonly Item Item;
        public readonly ItemAddress Location;
        public readonly CommandStatus Status;
        public readonly string OwnerId;
    }

    public sealed class RemoveItemEventArgs : ItemEventArgs
    {
        public RemoveItemEventArgs(Item item, ItemAddress from, CommandStatus status, string ownerId)
            : base(item, status, ownerId, from) { }
        public ItemAddress From => Location;
    }

    public sealed class AddItemEventArgs : ItemEventArgs
    {
        public AddItemEventArgs(Item item, ItemAddress to, CommandStatus status, string ownerId)
            : base(item, status, ownerId, to) { }
        public ItemAddress To => Location;
    }

    public class InventoryController
    {
        public InventoryController(string id, Inventory inventory)
        {
            ID = id;
            Inventory = inventory;
        }

        public string ID { get; }
        public Inventory Inventory { get; }
        public HashSet<Item> Unexamined { get; } = new();
        public List<Exception> SwallowedEventExceptions { get; } = new();
        public List<EFT.UI.DragAndDrop.FastAccessGrenadeItemView> Views { get; } = new();

        /// <summary>Throws from IsAtReachablePlace only while a pick patch is asking.</summary>
        public bool FaultWhilePickPatchAsks { get; set; }

        public event Action<RemoveItemEventArgs> RemoveItemEvent;
        public event Action<AddItemEventArgs> AddItemEvent;

        public bool Examined(Item item) => !Unexamined.Contains(item);

        public bool IsAtBindablePlace(Item item)
            => PatchHost.Run(typeof(InventoryController).GetMethod(nameof(IsAtBindablePlace))!, this,
                new object[] { item }, () => NativeIsAtBindablePlace(item));

        public bool IsAtReachablePlace(Item item)
        {
            if (FaultWhilePickPatchAsks && PatchHost.IsRunning(t => t.Name.Contains("Pick")))
                throw new InvalidOperationException("controlled reachability fault");
            return PatchHost.Run(typeof(InventoryController).GetMethod(nameof(IsAtReachablePlace))!, this,
                new object[] { item }, () => NativeIsAtReachablePlace(item));
        }

        private static bool NativeQuickUsable(Item item)
            => item is Weapon || item is ThrowWeap || item.GetItemComponent<KnifeComponent>() != null || item is Meds
               || item is FoodDrink || item is PortableRangeFinder || item is Compass || item is RadioTransmitter;

        // InventoryController.cs:404-421.
        private bool NativeIsAtBindablePlace(Item item)
        {
            if (item.CurrentAddress == null)
                return false;
            var parentSlot = item.Parent.Container.ParentItem.CurrentAddress?.Container as Slot;
            bool atFastAccess = Inventory.FastAccessSlots.Select(Inventory.Equipment.GetSlot).Any(slot => slot == parentSlot);
            bool whole = item is not CompoundItem compound || !compound.MissingVitalParts.Any();
            return atFastAccess && whole && Examined(item) && NativeQuickUsable(item);
        }

        // InventoryController.cs:448-464 (the stash test is moot in a raid).
        private bool NativeIsAtReachablePlace(Item item)
        {
            if (item.CurrentAddress == null)
                return false;
            bool whole = item is not CompoundItem compound || !compound.MissingVitalParts.Any();
            return whole && Inventory.GetItemsInSlots(Inventory.BindAvailableSlotsExtended).Contains(item)
                   && Examined(item) && NativeQuickUsable(item);
        }

        // InventoryController.cs:882-907: direct children of the FastAccessSlots containers.
        public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> list, Predicate<T> predicate) where T : Item
        {
            foreach (EquipmentSlot slot in Inventory.FastAccessSlots)
            {
                if (Inventory.Equipment.GetSlot(slot).ContainedItem is not CompoundItem container)
                    continue;
                foreach (IContainer grid in container.Containers)
                    foreach (Item item in grid.Items)
                        if (item is T typed && (predicate == null || predicate(typed)))
                            list.Add(typed);
            }
        }

        // InventoryExtension.cs:496 and UseItemsAnywhere's replacement: top level, examined, by ThrowType.
        public List<ThrowWeap> GetThrowablePriorityGrenadesList()
        {
            EquipmentSlot[] slots = Install.UseItemsAnywhere
                ? new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.ArmBand, EquipmentSlot.SecuredContainer, EquipmentSlot.Backpack }
                : new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
            var list = new List<ThrowWeap>();
            foreach (EquipmentSlot slot in slots)
                if (Inventory.Equipment.GetSlot(slot).ContainedItem is CompoundItem container)
                    list.AddRange(container.Containers.SelectMany(c => c.Items).OfType<ThrowWeap>().Where(Examined));
            return list.OrderBy(g => g.ThrowType).ToList();
        }

        // ItemController.SafeRemoveItemEvent (ItemController.cs:781-790): ONE try
        // around the whole multicast, so a throwing subscriber silences the rest.
        public void RaiseRemove(RemoveItemEventArgs args)
        {
            try
            {
                RemoveItemEvent?.Invoke(args);
            }
            catch (Exception ex)
            {
                SwallowedEventExceptions.Add(ex);
            }
        }

        public void RaiseAdd(AddItemEventArgs args)
        {
            try
            {
                AddItemEvent?.Invoke(args);
            }
            catch (Exception ex)
            {
                SwallowedEventExceptions.Add(ex);
            }
        }

        public void RaiseSetInHands(Item item)
        {
            foreach (var view in Views)
                view.OnSetInHands(item, CommandStatus.Succeed, ID);
        }

        public static void Detach(Item item)
        {
            switch (item.CurrentAddress)
            {
                case GridItemAddress grid:
                    grid.Grid.Contents.Remove(item);
                    break;
                case SlotItemAddress slot:
                    slot.Slot.ContainedItem = null;
                    break;
            }
            item.CurrentAddress = null;
        }

        public static void Place(Item item, Grid grid)
        {
            grid.Contents.Add(item);
            item.CurrentAddress = new GridItemAddress(grid);
        }

        public static void Equip(Item item, Slot slot)
        {
            slot.ContainedItem = item;
            item.CurrentAddress = new SlotItemAddress(slot);
        }

        // Player.ThrowGrenade -> ItemManipulator.Discard -> GrenadeThrowResult.RaiseEvents
        // (Player.cs:15678-15682, 31952-31967; DiscardResult/RemoveResult/ContainerRemoveResult):
        // the item is gone before Begin and Succeed are raised, From is where it was.
        public void Discard(Item item)
        {
            ItemAddress from = item.CurrentAddress;
            Detach(item);
            RaiseRemove(new RemoveItemEventArgs(item, from, CommandStatus.Begin, ID));
            RaiseRemove(new RemoveItemEventArgs(item, from, CommandStatus.Succeed, ID));
        }

        public void Move(Item item, Grid to)
        {
            ItemAddress from = item.CurrentAddress;
            Detach(item);
            Place(item, to);
            RaiseRemove(new RemoveItemEventArgs(item, from, CommandStatus.Succeed, ID));
            RaiseAdd(new AddItemEventArgs(item, item.CurrentAddress, CommandStatus.Succeed, ID));
        }
    }
}

namespace EFT
{
    using EFT.InventoryLogic;

    public class Player
    {
        public Player(InventoryController controller, Weapon primary)
        {
            InventoryController = controller;
            PrimaryWeapon = primary;
            InHands = primary;
        }

        public InventoryController InventoryController { get; set; }
        public Weapon PrimaryWeapon { get; }
        public Item InHands { get; private set; }
        public int FallbackCount { get; private set; }

        // Player.cs:31467-31476. Item.CheckAction(null) succeeds for a null
        // address (Item.cs:629-634), so a discarded pick passes this gate.
        public void SetItemInHands(Item item)
        {
            if (item != null)
                TryProceed(item);
        }

        // A bound number key: Player.SetQuickSlotItem -> TryProceed (Player.cs:31417-31435).
        public void PressBoundKey(Item bound) => TryProceed(bound);

        // Player.cs:31560-31587 + the SetInHands event the view listens to.
        public void TryProceed(Item item)
        {
            if (!InventoryController.IsAtReachablePlace(item))
            {
                SetFirstAvailableItem();
                return;
            }
            InHands = item;
            if (item is ThrowWeap)
                InventoryController.RaiseSetInHands(item);
        }

        // Player.cs:31375-31387: first reachable slot item, the rifle here.
        public void SetFirstAvailableItem()
        {
            FallbackCount++;
            InHands = PrimaryWeapon;
        }

        public void ThrowGrenadeInHands()
        {
            var grenade = (ThrowWeap)InHands;
            InventoryController.Discard(grenade);
            InHands = PrimaryWeapon;
        }

        public void PutAwayToRifle() => InHands = PrimaryWeapon;
    }

    public class PlayerInputTranslator
    {
        private readonly List<ThrowWeap> _preAllocatedGrenadesList = new();

        public PlayerInputTranslator(Player player) => _player = player;

        // PlayerInputTranslator.cs:121 — public in the shipped assembly.
        public Player _player;

        /// <summary>Stands in for UnityEngine.Random.Range(0, count).</summary>
        public static Func<int, int> RandomIndex { get; set; } = _ => 0;

        public virtual void vmethod_1()
            => PatchHost.Run(typeof(PlayerInputTranslator).GetMethod(nameof(vmethod_1))!, this,
                Array.Empty<object>(), NativeTapThrow);

        // PlayerInputTranslator.cs:496-542 (hand-state early returns omitted).
        private void NativeTapThrow()
        {
            _preAllocatedGrenadesList.Clear();
            _player.InventoryController.GetReachableItemsOfTypeNonAlloc(_preAllocatedGrenadesList,
                x => _player.InventoryController.Examined(x));
            int count = _preAllocatedGrenadesList.Count;
            if (count < 1)
                return;
            ThrowWeap pick = _player.InventoryController.Inventory.Equipment.TopPriorityGrenade;
            if (pick != null)
            {
                _player.SetItemInHands(pick);
                _preAllocatedGrenadesList.Clear();
                return;
            }
            pick = count > 1 ? _preAllocatedGrenadesList[RandomIndex(count)] : _preAllocatedGrenadesList[0];
            _player.SetItemInHands(pick);
            _preAllocatedGrenadesList.Clear();
        }
    }
}

namespace EFT.UI.DragAndDrop
{
    using EFT.InventoryLogic;

    public abstract class QuickSlotView
    {
        // QuickSlotView.cs:38 — protected, hence ___InventoryController.
        protected InventoryController InventoryController;
    }

    public class FastAccessGrenadeItemView : QuickSlotView
    {
        private List<ThrowWeap> _sortedGrenadeList = new();

        /// <summary>What the quick slot shows (ItemView/Item in the native view).</summary>
        public Item Shown { get; private set; }
        public int SetNewTopPriorityCalls { get; private set; }
        public InventoryController Controller => InventoryController;

        public ThrowWeap TopPriorityGrenade
        {
            get => InventoryController.Inventory.Equipment.TopPriorityGrenade;
            set => InventoryController.Inventory.Equipment.TopPriorityGrenade = value;
        }

        // FastAccessGrenadeItemView.cs:66-107.
        public void Show(InventoryController controller)
        {
            InventoryController = controller;
            controller.Views.Add(this);
            controller.AddItemEvent += OnItemAdded;
            controller.RemoveItemEvent += OnItemRemoved;
            _sortedGrenadeList = controller.GetThrowablePriorityGrenadesList();
            TopPriorityGrenade = controller.Inventory.Equipment.TopPriorityGrenade ?? _sortedGrenadeList.FirstOrDefault();
            Shown = TopPriorityGrenade;
        }

        public void Hide()
        {
            InventoryController.AddItemEvent -= OnItemAdded;
            InventoryController.RemoveItemEvent -= OnItemRemoved;
            InventoryController.Views.Remove(this);
        }

        // FastAccessGrenadeItemView.cs:109-125.
        public void SetNewTopPriorityGrenade()
        {
            SetNewTopPriorityCalls++;
            TopPriorityGrenade = _sortedGrenadeList.FirstOrDefault();
            Shown = TopPriorityGrenade;
        }

        // FastAccessGrenadeItemView.cs:138-158 (rig-equip branch omitted: no rig swaps here).
        public void OnItemAdded(AddItemEventArgs obj)
        {
            if (InventoryController.ID != obj.OwnerId || obj.Status != CommandStatus.Succeed)
                return;
            if (obj.Item is ThrowWeap item
                && InventoryController.Inventory.Equipment.GrenadeThrowingSlots.Any(slot => slot.ContainedItem == obj.To.Container.ParentItem)
                && !_sortedGrenadeList.Contains(item))
            {
                _sortedGrenadeList.Add(item);
            }
            if (Shown == null)
                SetNewTopPriorityGrenade();
        }

        public void OnItemRemoved(RemoveItemEventArgs obj)
            => PatchHost.Run(typeof(FastAccessGrenadeItemView).GetMethod(nameof(OnItemRemoved))!, this,
                new object[] { obj }, () => NativeOnItemRemoved(obj));

        // FastAccessGrenadeItemView.cs:160-197.
        private void NativeOnItemRemoved(RemoveItemEventArgs obj)
        {
            if (InventoryController.ID != obj.OwnerId || obj.Status != CommandStatus.Succeed)
                return;
            if (obj.Item is ThrowWeap throwWeap
                && InventoryController.Inventory.Equipment.GrenadeThrowingSlots.Any(slot => slot.ContainedItem == obj.From.Container.ParentItem))
            {
                _sortedGrenadeList.Remove(throwWeap);
                if (_sortedGrenadeList.Count == 0)
                {
                    TopPriorityGrenade = null;
                    Shown = null;
                    return;
                }
                if (throwWeap == TopPriorityGrenade)
                    SetNewTopPriorityGrenade();
            }
            // The CompoundItem/TacticalVest branch never matches a grenade: ThrowWeap is
            // not a CompoundItem (ThrowWeap.cs:9).
        }

        // FastAccessGrenadeItemView.cs:199-210.
        public void OnSetInHands(Item item, CommandStatus status, string ownerId)
        {
            if (item is ThrowWeap grenade && InventoryController.ID == ownerId && status == CommandStatus.Succeed)
            {
                TopPriorityGrenade = grenade;
                Shown = grenade;
            }
        }
    }
}

/// <summary>
/// The effect of UseItemsAnywhere 1.4.0's two place postfixes (bindable: any
/// examined quick-use item in the equipment; reachable: anything in the
/// equipment), modelled from its mechanism for the user's install. The mod has
/// no licence file: nothing of it is copied, shipped or used by Arena.
/// </summary>
public static class UseItemsAnywhereModel
{
    [SPT.Reflection.Patching.PatchPostfix]
    [ExternalTarget(typeof(EFT.InventoryLogic.InventoryController), nameof(EFT.InventoryLogic.InventoryController.IsAtBindablePlace))]
    public static void Bindable(EFT.InventoryLogic.InventoryController __instance, ref bool __result, EFT.InventoryLogic.Item item)
    {
        if (item is EFT.InventoryLogic.CompoundItem compound && compound.MissingVitalParts.Any())
        {
            __result = false;
            return;
        }
        if ((item is EFT.InventoryLogic.Weapon || item is EFT.InventoryLogic.ThrowWeap || item is EFT.InventoryLogic.Meds)
            && __instance.Examined(item))
        {
            __result = EFT.InventoryLogic.ItemExtensions.Contains(__instance.Inventory.Equipment, item);
        }
    }

    [SPT.Reflection.Patching.PatchPostfix]
    [ExternalTarget(typeof(EFT.InventoryLogic.InventoryController), nameof(EFT.InventoryLogic.InventoryController.IsAtReachablePlace))]
    public static void Reachable(EFT.InventoryLogic.InventoryController __instance, ref bool __result, EFT.InventoryLogic.Item item)
        => __result = EFT.InventoryLogic.ItemExtensions.Contains(__instance.Inventory.Equipment, item);
}
