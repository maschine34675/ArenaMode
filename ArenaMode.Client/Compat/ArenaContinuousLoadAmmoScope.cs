using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Physical controller ownership and cancellation tickets; never profile/item IDs.</summary>
internal sealed class ArenaContinuousLoadAmmoScope
{
    internal sealed class Lease
    {
        internal readonly object Controller, Body, World, Inventory;
        internal bool Disposed, HasLimit;
        internal long Revision, Operation;
        internal Lease(object controller, object body, object world, object inventory)
        { Controller = controller; Body = body; World = world; Inventory = inventory; }
    }
    internal sealed class Completion
    {
        internal readonly Lease Owner;
        internal readonly long Operation;
        internal Completion(Lease owner) { Owner = owner; Operation = ++owner.Operation; }
    }
    private ConditionalWeakTable<object, Lease> _controllers = new(), _inventories = new();
    private ConditionalWeakTable<object, Completion> _tasks = new();

    internal static bool OwnsRaid(bool exactPair, bool running, bool pmc, string map, string settingsMap,
        bool active, bool armed, bool pending, string armedMap, string token, string nativeIdentity)
        => exactPair && running && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (active || armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeIdentity, token));

    internal Lease BindController(object controller, object body, object bodyWorld, object inventory,
        object currentWorld, object mainPlayer, bool exactRaid, bool local)
    {
        if (controller == null || body == null || inventory == null || bodyWorld == null || !exactRaid || !local
            || !ReferenceEquals(bodyWorld, currentWorld) || !ReferenceEquals(body, mainPlayer)) return null;
        if (_controllers.TryGetValue(controller, out var existing))
            return !existing.Disposed && ReferenceEquals(existing.Body, body) && ReferenceEquals(existing.World, bodyWorld)
                && ReferenceEquals(existing.Inventory, inventory) ? existing : null;
        var lease = new Lease(controller, body, bodyWorld, inventory);
        _controllers.Add(controller, lease);
        _inventories.Remove(inventory); _inventories.Add(inventory, lease);
        return lease;
    }
    internal Lease Find(object controller) => controller != null && _controllers.TryGetValue(controller, out var lease) ? lease : null;
    internal Lease ForInventory(object inventory) => inventory != null && _inventories.TryGetValue(inventory, out var lease) ? lease : null;
    internal Completion CaptureTask(object task, Lease lease)
    {
        if (task == null || lease == null) return null;
        var completion = new Completion(lease);
        _tasks.Remove(task); _tasks.Add(task, completion);
        return completion;
    }
    internal Completion ForTask(object task) => task != null && _tasks.TryGetValue(task, out var completion) ? completion : null;
    internal static bool IsLatest(Completion completion)
        => completion != null && !completion.Owner.Disposed && completion.Operation == completion.Owner.Operation;
    internal static bool IsCurrent(Lease lease, object controller, object world, object mainPlayer, object inventory, bool alive, bool exactRaid)
        => lease != null && !lease.Disposed && alive && exactRaid
            && ReferenceEquals(lease.Controller, controller) && ReferenceEquals(lease.World, world)
            && ReferenceEquals(lease.Body, mainPlayer) && ReferenceEquals(lease.Inventory, inventory);
    internal static bool Retire(Lease lease)
    {
        if (lease == null || lease.Disposed) return false;
        lease.Disposed = true; ++lease.Revision; ++lease.Operation; return true;
    }
    internal static bool MayResume(Lease lease, long ticket, bool current, bool active, bool multiSelect)
        => lease != null && !lease.Disposed && lease.Revision == ticket && current && !active && !multiSelect;
    internal static bool MayClearStatic(object disposing, object current, bool disposingOwned, bool currentOwned)
        => !(disposingOwned || currentOwned) || disposing != null && ReferenceEquals(disposing, current);
    internal void Clear()
    { _controllers = new(); _inventories = new(); _tasks = new(); }
}
