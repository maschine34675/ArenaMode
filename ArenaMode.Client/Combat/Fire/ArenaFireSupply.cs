#nullable disable
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat.Fire;

/// <summary>
/// One extra, process-local incendiary identity per equipment tree. Its native
/// donor model is still an RGD-5; ordinary RGD-5s are never converted or removed.
/// Supply is deliberately after live ownership/entry-loadout capture, so protected
/// own-gear snapshots do not learn the bonus item and clone it on every restore.
/// </summary>
internal static class ArenaFireSupply
{
    private sealed class Grant { internal ThrowWeap Item; }
    private static ConditionalWeakTable<InventoryEquipment, Grant> _grants = new();
    private static WeakReference<GameWorld> _world;
    private static WeakReference<Modes.ArenaMatch> _match;
    private static bool _preloadStarted, _preloadReady;
    private static int _generation;
    private static readonly EquipmentSlot[] Slots = { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
    private static readonly Kits.KitDefinition DonorKit = new()
    {
        Id = "arena-molotov-supply",
        Fill = new Kits.KitFill { RigContents = new List<Kits.KitStackedItem>
            { new() { Tpl = ArenaFirePolicy.DonorTemplate, Count = 1 } } }
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
        _preloadStarted = _preloadReady = false;
        _generation++;
    }

    internal static bool Owns(Player player, ThrowWeap item)
    {
        var equipment = player?.InventoryController?.Inventory?.Equipment;
        return equipment != null && item != null && _grants.TryGetValue(equipment, out var grant)
            && ReferenceEquals(grant.Item, item);
    }

    // Called only with the runtime's exact active world/match/local body. Keep
    // the native controller busy checks here as inventory operations can yield
    // across frames even when the match itself is already live.
    internal static void Tick(GameWorld world, Player player)
    {
        if (!ArenaFireRuntime.BotEscapeAvailable || !ArenaFireRuntime.LiveActor(player)
            || player.IsAI || !player.IsYourPlayer || !ReferenceEquals(world.MainPlayer, player)
            || Respawn.ArenaRespawnDriver.IsRespawnInProgress || WeaponSwapService.IsSwapping
            || player.HandsController == null
            || !(player.InventoryController is Player.SinglePlayerInventoryController inventory)
            || inventory.IsChangingWeapon) return;
        var equipment = inventory.Inventory?.Equipment;
        var factory = Singleton<ItemFactory>.Instance;
        if (equipment == null || factory == null || _grants.TryGetValue(equipment, out _)) return;
        if (!EnsurePreload(world)) return;

        // Mark the attempt before touching the graph. A full inventory or an
        // event exception must not become an unlimited retry/duplicate source.
        var grant = new Grant();
        _grants.Add(equipment, grant);
        var item = factory.CreateItem(factory.NextId, ArenaFirePolicy.DonorTemplate, null) as ThrowWeap;
        if (item == null)
            throw new InvalidOperationException("Arena incendiary donor is not a native throwable.");
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

                // Native AddAndRaiseEvents uses precisely this sequence. Retain
                // identity before notifications: a third-party listener can throw
                // after physical placement, which must never grant a second item.
                grant.Item = item;
                try
                {
                    added.Value.RaiseEvents(inventory, CommandStatus.Begin);
                    added.Value.RaiseEvents(inventory, CommandStatus.Succeed);
                }
                finally
                {
                    // G reads this native preference directly. Set it once only;
                    // the player's later grenade selection remains authoritative.
                    if (inventory.Examined(item) && inventory.IsAtReachablePlace(item))
                        equipment.TopPriorityGrenade = item;
                }
                Plugin.ModLogger.LogDebug("Arena incendiary supplied: one extra in rig/pockets; existing grenades preserved.");
                return;
            }
        }
        Plugin.ModLogger.LogInfo("Arena Molotov skipped: no compatible free rig/pocket space; existing equipment was kept.");
    }

    private static bool EnsurePreload(GameWorld world)
    {
        if (_preloadReady) return true;
        // Preload's historical API treats absent factories as an empty request.
        // Never mistake that case for an actually usable hands-controller pool.
        if (!Singleton<ItemFactory>.Instantiated || !Singleton<ObjectsFactory>.Instantiated) return false;
        if (Kits.KitBundlePreloader.IsTemplateWarmed(ArenaFirePolicy.DonorTemplate))
            return _preloadReady = true;
        if (_preloadStarted || Kits.KitBundlePreloader.IsPreloading) return false;
        if (!(Singleton<AbstractGame>.Instance is LocalGame game) || !ReferenceEquals(game.GameWorld, world)) return false;
        _preloadStarted = true;
        // Game hosts it, not the replaceable local body. The preloader already
        // owns resource de-duplication and raid cancellation; this generation
        // prevents an old completion from granting into a later world.
        game.StartCoroutine(Preload(world, _generation));
        return _preloadReady;
    }

    private static IEnumerator Preload(GameWorld world, int generation)
    {
        yield return Kits.KitBundlePreloader.Preload(DonorKit);
        if (generation != _generation || !Session.ArenaSession.IsActive
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)) yield break;
        _preloadReady = Kits.KitBundlePreloader.IsTemplateWarmed(ArenaFirePolicy.DonorTemplate);
        if (!_preloadReady)
            Plugin.ModLogger.LogWarning("Arena Molotov not supplied: its native grenade resources could not be loaded; existing equipment was kept.");
    }
}
