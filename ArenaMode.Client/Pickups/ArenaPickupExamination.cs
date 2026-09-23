using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Pickups;

/// <summary>Knowledge of exact Arena pickup instances, never profile/template knowledge.</summary>
internal static class ArenaPickupExamination
{
    private sealed class Grant
    {
        internal WeakReference<Player> Player;
        internal WeakReference<InventoryController> Inventory;
        internal WeakReference<GameWorld> World;
        internal int Generation;
    }

    // Weak keys cannot keep retired weapons/bodies/worlds alive after native cleanup.
    // Generation and body identity invalidate a grant even while an old item survives.
    private static readonly ConditionalWeakTable<Item, Grant> Known = new();

    internal static void Remember(IEnumerable<Item> generatedItems, Player player, GameWorld world, int generation)
    {
        if (!ArenaSession.IsActive || player == null || player.IsAI || world == null
            || !ReferenceEquals(world, Singleton<GameWorld>.Instance)
            || !ReferenceEquals(world.MainPlayer, player) || !ArenaPickupRuntime.IsLiveActor(player)
            || generation != ArenaPickupRuntime.Generation) return;
        var grant = new Grant
        {
            Player = new WeakReference<Player>(player),
            Inventory = new WeakReference<InventoryController>(player.InventoryController),
            World = new WeakReference<GameWorld>(world), Generation = generation
        };
        foreach (var item in generatedItems)
        {
            if (item == null) continue;
            Known.Remove(item);
            Known.Add(item, grant);
        }
    }

    internal static void Apply(InventoryController inventory, Item item, ref bool examined)
    {
        if (examined || !ArenaSession.IsActive || item == null || inventory == null
            || !Known.TryGetValue(item, out var grant)
            || grant.Generation != ArenaPickupRuntime.Generation
            || !grant.Player.TryGetTarget(out var player) || !grant.Inventory.TryGetTarget(out var owner)
            || !grant.World.TryGetTarget(out var world) || world == null
            || !ReferenceEquals(inventory, owner)
            || !ReferenceEquals(player.InventoryController, inventory)
            || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !ReferenceEquals(world.MainPlayer, player)
            || !ArenaPickupRuntime.IsLiveActor(player) || player.IsAI) return;
        examined = true;
    }
}
