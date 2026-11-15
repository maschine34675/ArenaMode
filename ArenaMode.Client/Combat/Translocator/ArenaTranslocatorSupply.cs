#nullable disable
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat.Translocator;

/// <summary>One persistent, private device per protected live equipment tree.</summary>
internal static class ArenaTranslocatorSupply
{
    internal const string NativePrefab = ArenaTranslocatorItem.PrefabPath;
    private sealed class Grant { internal Item Item; }
    private static ConditionalWeakTable<InventoryEquipment, Grant> _grants = new();
    private static WeakReference<GameWorld> _world;
    private static WeakReference<Modes.ArenaMatch> _match;
    private static bool _preloadStarted, _preloadReady, _unavailable, _warned;
    private static int _generation;
    private static readonly EquipmentSlot[] Slots = { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
    private static readonly EBoundItem[] QuickSlots = { EBoundItem.Item4, EBoundItem.Item5, EBoundItem.Item6,
        EBoundItem.Item7, EBoundItem.Item8, EBoundItem.Item9, EBoundItem.Item10 };
    private static readonly Kits.KitDefinition DeviceKit = new()
    {
        Id = "arena-translocator-supply",
        Fill = new Kits.KitFill { RigContents = new List<Kits.KitStackedItem>
            { new() { Tpl = ArenaTranslocatorItem.TemplateId, Count = 1 } } }
    };

    internal static void ForgetOutsideRaid(GameWorld world)
    {
        var match = Session.ArenaSession.IsActive ? Modes.ArenaMatchService.Current : null;
        if (match == null) world = null;
        if (world == null && _world == null) return;
        if (world != null && _world != null && _world.TryGetTarget(out var previous)
            && ReferenceEquals(world, previous) && _match != null && _match.TryGetTarget(out var previousMatch)
            && ReferenceEquals(match, previousMatch)) return;
        _grants = new();
        _world = world != null ? new WeakReference<GameWorld>(world) : null;
        _match = world != null ? new WeakReference<Modes.ArenaMatch>(match) : null;
        _preloadStarted = _preloadReady = _unavailable = _warned = false;
        _generation++;
    }

    internal static bool Owns(Player player, Item item)
    {
        try { return OwnsCurrentEquipment(player, item); }
        catch { return false; } // A native inventory graph may be retiring with its body.
    }

    private static bool OwnsCurrentEquipment(Player player, Item item)
    {
        if (!Session.ArenaSession.IsActive || !Singleton<GameWorld>.Instantiated
            || _world == null || !_world.TryGetTarget(out var world)
            || !ReferenceEquals(world, Singleton<GameWorld>.Instance)
            || _match == null || !_match.TryGetTarget(out var match)
            || !ReferenceEquals(match, Modes.ArenaMatchService.Current)
            || player == null || !ReferenceEquals(world.MainPlayer, player)) return false;
        var equipment = player.InventoryController?.Inventory?.Equipment;
        return equipment != null && item?.TemplateId == ArenaTranslocatorItem.TemplateId
            && _grants.TryGetValue(equipment, out var grant) && ReferenceEquals(grant.Item, item)
            && item.IsChildOf(equipment) && player.InventoryController.IsAtReachablePlace(item);
    }

    // Root calls this only after protected entry-loadout capture. Toggling the
    // mutator never resets the equipment ledger or turns a dropped item into a refill.
    internal static void Tick(GameWorld world, Player player)
    {
        try
        {
            ForgetOutsideRaid(world);
            if (!ArenaTranslocatorRuntime.IsLivePlayer(player) || world == null
                || !ReferenceEquals(world.MainPlayer, player)
                || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
                || Respawn.ArenaRespawnDriver.IsRespawnInProgress || WeaponSwapService.IsSwapping
                || player.HandsController == null || player.IsInventoryOpened
                || !(player.InventoryController is Player.SinglePlayerInventoryController inventory)
                || inventory.IsChangingWeapon) return;
            var equipment = inventory.Inventory?.Equipment;
            if (equipment == null || _grants.TryGetValue(equipment, out _) || !EnsurePreload(world)) return;

            // Mark before mutation: an uncertain native/event failure must not
            // become repeated grants. No room means no eviction and no retry loop.
            var grant = new Grant();
            _grants.Add(equipment, grant);
            var item = Singleton<ItemFactory>.Instance.CreateItem(Singleton<ItemFactory>.Instance.NextId,
                ArenaTranslocatorItem.TemplateId, null) as PortableRangeFinder;
            if (item == null) throw new InvalidOperationException("private item is not a native reusable device");
            foreach (var slot in Slots)
            {
                if (!(equipment.GetSlot(slot)?.ContainedItem is CompoundItem container) || container.Grids == null) continue;
                foreach (var grid in container.Grids)
                {
                    var location = grid.FindFreeSpace(item);
                    if (location == null) continue;
                    var address = grid.CreateItemAddress(location);
                    var plan = ItemManipulator.Add(item, address, inventory, simulate: true);
                    if (plan.Failed || !inventory.CanExecute(plan.Value)) continue;
                    var added = ItemManipulator.Add(item, address, inventory);
                    if (added.Failed) continue;
                    grant.Item = item; // Native notification subscribers may throw after physical placement.
                    added.Value.RaiseEvents(inventory, CommandStatus.Begin);
                    added.Value.RaiseEvents(inventory, CommandStatus.Succeed);
                    BindFreeSlot(inventory, item);
                    Plugin.ModLogger.LogDebug("Arena Translocator supplied: one persistent device in free rig/pocket space.");
                    return;
                }
            }
            Plugin.ModLogger.LogDebug("Arena Translocator not supplied: no free rig/pocket space.");
            Modes.ArenaToasts.Show("Translocator: no free rig/pocket space");
        }
        catch (Exception ex) { Warn(ex.GetBaseException().Message); }
    }

    internal static void BindFreeSlot(Player.SinglePlayerInventoryController inventory, Item item)
    {
        if (!inventory.IsAtBindablePlace(item)) return;
        var bound = inventory.Inventory.FastAccess.BoundItems;
        foreach (var value in bound.Values) if (ReferenceEquals(value, item)) return;
        foreach (var slot in QuickSlots)
        {
            if (bound.ContainsKey(slot)) continue;
            var plan = BindResult.Run(inventory, item, slot, simulate: true);
            if (plan.Failed || !inventory.CanExecute(plan.Value)) continue;
            // BindResult.Run can unbind an occupant. Recheck after the simulated
            // boundary and never schedule a transaction that can evict a later binding.
            if (bound.ContainsKey(slot)) continue;
            foreach (var value in bound.Values) if (ReferenceEquals(value, item)) return;
            var applied = BindResult.Run(inventory, item, slot, simulate: false);
            if (applied.Failed) return;
            applied.Value.RaiseEvents(inventory, CommandStatus.Begin);
            applied.Value.RaiseEvents(inventory, CommandStatus.Succeed);
            return;
        }
        // All keys used: normal native inventory binding remains available.
    }

    internal static bool HasVerifiedTemplate => Session.ArenaServerClient.TranslocatorAvailableForConfirmedRaid
        && Singleton<ItemFactory>.Instantiated
        && Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(ArenaTranslocatorItem.TemplateId, out var template)
        && template is PortableRangeFinderTemplate item
        && item._id.ToString() == ArenaTranslocatorItem.TemplateId
        && item.ParentId?.ToString() == ArenaTranslocatorItem.ParentTemplateId
        && item.Width == 1 && item.Height == 1 && item.StackMaxSize == 1
        && item.ExaminedByDefault && !item.CanSellOnRagfair && !item.CanRequireOnRagfair
        && !item.LeftHandItem && item.Prefab.path == NativePrefab && string.IsNullOrEmpty(item.UsePrefab.path);

    private static bool EnsurePreload(GameWorld world)
    {
        if (_unavailable) return false;
        if (!Session.ArenaServerClient.TranslocatorAvailableForConfirmedRaid)
        { _unavailable = true; Warn("server did not confirm its private device"); return false; }
        if (!Singleton<ItemFactory>.Instantiated || !Singleton<ObjectsFactory>.Instantiated) return false;
        if (!HasVerifiedTemplate)
        { _unavailable = true; Warn("private client template is missing or changed"); return false; }
        if (_preloadReady) return true;
        if (Kits.KitBundlePreloader.IsTemplateWarmed(ArenaTranslocatorItem.TemplateId)) return _preloadReady = true;
        if (_preloadStarted || Kits.KitBundlePreloader.IsPreloading) return false;
        if (!Singleton<AbstractGame>.Instantiated || !(Singleton<AbstractGame>.Instance is LocalGame game)
            || !ReferenceEquals(game.GameWorld, world)) return false;
        _preloadStarted = true;
        game.StartCoroutine(Preload(world, _generation));
        return _preloadReady;
    }

    private static IEnumerator Preload(GameWorld world, int generation)
    {
        yield return Kits.KitBundlePreloader.Preload(DeviceKit);
        if (generation != _generation || !Session.ArenaSession.IsActive
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)) yield break;
        _preloadReady = Kits.KitBundlePreloader.IsTemplateWarmed(ArenaTranslocatorItem.TemplateId);
        if (!_preloadReady) { _unavailable = true; Warn("native device resources could not be loaded"); }
    }

    private static void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Translocator unavailable: " + reason + "; existing equipment retained.");
    }
}
