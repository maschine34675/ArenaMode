#nullable disable
using Comfort.Common;
using ArenaMode.Shared;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat.Fire;

/// <summary>
/// One extra, process-local incendiary identity per equipment tree. Its native
/// item is the private Molotov; ordinary RGD-5s are never converted or removed.
/// Supply is deliberately after live ownership/entry-loadout capture, so protected
/// own-gear snapshots do not learn the bonus item and clone it on every restore.
/// </summary>
internal static class ArenaFireSupply
{
    private sealed class Grant { internal ThrowWeap Item; }
    private static ConditionalWeakTable<InventoryEquipment, Grant> _grants = new();
    private static WeakReference<GameWorld> _world;
    private static WeakReference<Modes.ArenaMatch> _match;
    private static bool _preloadStarted, _preloadReady, _unavailable;
    private static int _generation;
    private static readonly EquipmentSlot[] Slots = { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
    private static readonly Kits.KitDefinition MolotovKit = new()
    {
        Id = "arena-molotov-supply",
        Fill = new Kits.KitFill { RigContents = new List<Kits.KitStackedItem>
            { new() { Tpl = ArenaMolotovItem.TemplateId, Count = 1 } } }
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
        _preloadStarted = _preloadReady = _unavailable = false;
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
        var item = factory.CreateItem(factory.NextId, ArenaMolotovItem.TemplateId, null) as ThrowWeap;
        if (item == null)
            throw new InvalidOperationException("Arena Molotov is not a native throwable.");
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
        Plugin.ModLogger.LogDebug("Arena Molotov skipped: no compatible free rig/pocket space; existing equipment was kept.");
    }

    internal static bool HasVerifiedTemplate =>
        Session.ArenaServerClient.MolotovAvailableForConfirmedRaid
        && Singleton<ItemFactory>.Instantiated
        && Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(ArenaMolotovItem.TemplateId, out var template)
        && template is ThrowWeapTemplate item && item.Strength == 0
        && item.MinExplosionDistance == 0 && item.MaxExplosionDistance == 0 && item.FragmentsCount == 0
        && item.Contusion.sqrMagnitude == 0 && item.Blindness.sqrMagnitude == 0
        && item.ArmorDistanceDistanceDamage.sqrMagnitude == 0 && item.ExplosionEffectType == ""
        && !item.CanPlantOnGround && !item.PlayFuzeSound && item.ExplDelay > 0 && item.ExplDelay <= 10
        && item.MinTimeToContactExplode < 0;

    private static bool EnsurePreload(GameWorld world)
    {
        if (_unavailable) return false;
        if (!Session.ArenaServerClient.MolotovAvailableForConfirmedRaid)
        {
            _unavailable = true;
            Plugin.ModLogger.LogWarning("Arena Molotov not supplied: the server did not confirm its private item; no RGD-5 substitute is used.");
            return false;
        }
        // Preload's historical API treats absent factories as an empty request.
        // Never mistake that case for an actually usable hands-controller pool.
        if (!Singleton<ItemFactory>.Instantiated || !Singleton<ObjectsFactory>.Instantiated) return false;
        if (!HasVerifiedTemplate)
        {
            _unavailable = true;
            Plugin.ModLogger.LogWarning("Arena Molotov not supplied: its private client template is missing or unsafe; existing equipment was kept.");
            return false;
        }
        if (_preloadReady) return true;
        if (Kits.KitBundlePreloader.IsTemplateWarmed(ArenaMolotovItem.TemplateId))
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
        yield return Kits.KitBundlePreloader.Preload(MolotovKit);
        if (generation != _generation || !Session.ArenaSession.IsActive
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)) yield break;
        _preloadReady = Kits.KitBundlePreloader.IsTemplateWarmed(ArenaMolotovItem.TemplateId);
        if (!_preloadReady)
            Plugin.ModLogger.LogWarning("Arena Molotov not supplied: its native grenade resources could not be loaded; existing equipment was kept.");
    }
}
