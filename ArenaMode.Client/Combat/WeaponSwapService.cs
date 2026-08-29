using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>
/// Puts a freshly built weapon into a LIVING player's hands.
///
/// Every other equipment change in this mod goes through the respawn pipeline,
/// and for good reason. Gun Game cannot: it hands out a new gun on every kill,
/// and a black screen plus a teleport after each kill would be unplayable. So
/// this is the one place that edits a live inventory — and it follows the game's
/// own recipe for exactly this situation, the hideout shooting range, which
/// swaps a standing player's entire loadout: empty the hands and WAIT for that
/// to finish, remove, add, draw.
///
/// The waiting is the load-bearing part. Hands controllers are built
/// asynchronously, and pulling the item out from under one mid-animation is the
/// oldest documented way to corrupt this game's weapon state. So the swap
/// refuses to start while a weapon change is already in flight, waits for every
/// callback with a timeout, and re-checks that the player is still alive between
/// steps. If the new weapon cannot be seated, the old one goes back in: standing
/// empty-handed in a firefight is worse than keeping the gun you had.
/// </summary>
internal static class WeaponSwapService
{
    /// <summary>Generous — an animation, not a computation. Only stops a hang.</summary>
    private const float StepTimeoutSeconds = 5f;

    /// <summary>
    /// The spare magazines this service handed out last time. Tracked rather
    /// than detected: guessing which magazines in a rig "belong" to the old gun
    /// risks throwing away something the player picked up, and over a full
    /// ladder the leftovers would fill the rig.
    /// </summary>
    private static readonly List<Item> HandedOutSpares = new List<Item>();

    public static bool IsSwapping { get; private set; }

    public static void Reset()
    {
        IsSwapping = false;
        HandedOutSpares.Clear();
    }

    /// <summary>
    /// Returns false when the swap could not even be started — the caller keeps
    /// the weapon it has and is free to ask again later.
    /// </summary>
    public static bool Swap(Player player, Weapon weapon, IList<Item> spares, Action<bool> onComplete)
    {
        if (IsSwapping || player == null || weapon == null)
            return false;

        if (player.HealthController == null || !player.HealthController.IsAlive)
            return false;

        var inventory = player.InventoryController;
        if (inventory == null || inventory.IsChangingWeapon)
            return false;

        var slot = inventory.Inventory?.Equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon);
        if (slot == null)
            return false;

        IsSwapping = true;
        player.StartCoroutine(SwapRoutine(player, inventory, slot, weapon, spares, onComplete));
        return true;
    }

    private static IEnumerator SwapRoutine(
        Player player,
        InventoryController inventory,
        Slot slot,
        Weapon weapon,
        IList<Item> spares,
        Action<bool> onComplete)
    {
        // Holstering first. Removing the item a hands controller is animating is
        // the corruption case this whole class is shaped around.
        var empty = new Step();
        BeginEmptyHands(player, empty);

        var deadline = Time.time + StepTimeoutSeconds;
        while (!empty.Done && Time.time < deadline)
            yield return null;

        if (!empty.Done || empty.Error != null)
        {
            Plugin.ModLogger.LogWarning(
                $"Gun Game: could not holster before the swap ({empty.Error ?? "timed out"}) — keeping the current weapon.");
            Finish(onComplete, false);
            yield break;
        }

        // Dying during the holster animation hands the body to the respawn
        // driver, which is about to replace the whole inventory anyway.
        if (player.HealthController == null || !player.HealthController.IsAlive)
        {
            Finish(onComplete, false);
            yield break;
        }

        if (!Exchange(inventory, slot, weapon, spares))
        {
            Finish(onComplete, false);
            yield break;
        }

        var draw = new Step();
        BeginDraw(player, weapon, draw);

        deadline = Time.time + StepTimeoutSeconds;
        while (!draw.Done && Time.time < deadline)
            yield return null;

        // The weapon is seated in the slot either way, so a failed draw leaves
        // the player able to pull it out by hand — not worth undoing the swap.
        if (!draw.Done || draw.Error != null)
        {
            Plugin.ModLogger.LogWarning(
                $"Gun Game: new weapon seated but not drawn ({draw.Error ?? "timed out"}).");
        }

        Finish(onComplete, true);
    }

    /// <summary>
    /// The inventory edit itself, in one uninterrupted block: nothing yields
    /// between taking the old weapon out and putting the new one in, so there is
    /// no frame in which the player owns neither.
    /// </summary>
    private static bool Exchange(InventoryController inventory, Slot slot, Weapon weapon, IList<Item> spares)
    {
        Item removed = null;

        try
        {
            removed = slot.ContainedItem;
            if (removed != null)
            {
                var removeResult = ItemManipulator.RemoveWithoutRestrictions(removed, inventory);
                if (removeResult.Failed)
                {
                    Plugin.ModLogger.LogWarning($"Gun Game: removing the old weapon failed: {removeResult.Error}");
                    return false;
                }
            }

            var addResult = ItemManipulator.AddWithoutRestrictions(weapon, slot.CreateItemAddress(), inventory);
            if (addResult.Failed)
            {
                Plugin.ModLogger.LogWarning($"Gun Game: seating the new weapon failed: {addResult.Error}");
                PutBack(inventory, slot, removed);
                return false;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: weapon exchange threw ({ex.Message}).");
            PutBack(inventory, slot, removed);
            return false;
        }

        // Ammunition is a bonus, never a reason to fail a swap that already
        // succeeded — the player has a gun with a loaded magazine either way.
        try
        {
            ClearHandedOutSpares(inventory);
            HandOutSpares(inventory, spares);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: spare magazines failed ({ex.Message}).");
        }

        return true;
    }

    private static void PutBack(InventoryController inventory, Slot slot, Item removed)
    {
        if (removed == null || slot.ContainedItem != null)
            return;

        try
        {
            var result = ItemManipulator.AddWithoutRestrictions(removed, slot.CreateItemAddress(), inventory);
            if (result.Failed)
                Plugin.ModLogger.LogError($"Gun Game: could not restore the old weapon: {result.Error}");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Gun Game: restoring the old weapon threw: {ex.Message}");
        }
    }

    private static void ClearHandedOutSpares(InventoryController inventory)
    {
        foreach (var spare in HandedOutSpares)
        {
            // Looted, dropped or already gone — the address check keeps this from
            // reaching into an inventory the item no longer belongs to.
            if (spare == null || spare.CurrentAddress == null)
                continue;

            var result = ItemManipulator.RemoveWithoutRestrictions(spare, inventory);
            if (result.Failed)
                Plugin.ModLogger.LogWarning($"Gun Game: removing a stale spare failed: {result.Error}");
        }

        HandedOutSpares.Clear();
    }

    private static void HandOutSpares(InventoryController inventory, IList<Item> spares)
    {
        if (spares == null || spares.Count == 0)
            return;

        var containers = Kits.KitBuilder.GridContainersOf(inventory.Inventory?.Equipment);
        foreach (var spare in spares)
        {
            if (spare == null)
                continue;

            if (Kits.KitBuilder.AddToAnyGridOf(containers, spare))
                HandedOutSpares.Add(spare);
            else
                Plugin.ModLogger.LogWarning("Gun Game: no rig space for a spare magazine.");
        }
    }

    private sealed class Step
    {
        public bool Done;
        public string Error;
    }

    private static void BeginEmptyHands(Player player, Step step)
    {
        try
        {
            player.SetEmptyHands(result =>
            {
                step.Error = result.Failed ? (result.Error ?? "unknown error") : null;
                step.Done = true;
            });
        }
        catch (Exception ex)
        {
            step.Error = ex.Message;
            step.Done = true;
        }
    }

    private static void BeginDraw(Player player, Weapon weapon, Step step)
    {
        try
        {
            // The game's own entry point for "put this item in the player's
            // hands", including its IsChangingWeapon guard.
            player.SetItemInHands(weapon, result =>
            {
                step.Error = result.Failed ? (result.Error ?? "unknown error") : null;
                step.Done = true;
            });
        }
        catch (Exception ex)
        {
            step.Error = ex.Message;
            step.Done = true;
        }
    }

    private static void Finish(Action<bool> onComplete, bool success)
    {
        IsSwapping = false;

        try
        {
            onComplete?.Invoke(success);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: swap callback failed: {ex.Message}");
        }
    }
}
