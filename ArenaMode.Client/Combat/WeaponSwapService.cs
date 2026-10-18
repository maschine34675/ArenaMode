using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

    /// <summary>
    /// Invalidates a coroutine independently of the raid epoch. Reset can run
    /// after GameEnd while an EFT hands callback is still pending; a later swap
    /// in another raid must not be cleared by that old coroutine's Finish.
    /// </summary>
    private static int _operationGeneration;

    public static bool IsSwapping { get; private set; }

    public static void Reset()
    {
        Interlocked.Increment(ref _operationGeneration);
        IsSwapping = false;
        HandedOutSpares.Clear();
    }

    /// <summary>
    /// Returns false when the swap could not even be started — the caller keeps
    /// the weapon it has and is free to ask again later.
    /// </summary>
    public static bool Swap(
        LocalGame game,
        int raidEpoch,
        Player player,
        Weapon weapon,
        IList<Item> spares,
        Action<bool> onComplete)
    {
        if (IsSwapping || player == null || weapon == null
            || !OwnsRaid(game, raidEpoch)
            || !IsCurrentMainPlayer(player))
            return false;

        if (player.HealthController == null || !player.HealthController.IsAlive)
            return false;

        var inventory = player.InventoryController;
        if (inventory == null || inventory.IsChangingWeapon)
            return false;

        var slot = inventory.Inventory?.Equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon);
        if (slot == null)
            return false;

        // Hosted on the GAME, never on the player: the raid-start kit swap and
        // the respawn recovery paths DESTROY the player body, and a coroutine
        // that dies with its host never reaches its own cleanup — which would
        // leave the busy flag latched and the mode silently dead for the raid.
        int operationGeneration = Interlocked.Increment(ref _operationGeneration);
        IsSwapping = true;
        try
        {
            game.StartCoroutine(SwapRoutine(
                game,
                raidEpoch,
                operationGeneration,
                player,
                inventory,
                slot,
                weapon,
                spares,
                onComplete));
            return true;
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _operationGeneration) == operationGeneration)
                IsSwapping = false;

            Plugin.ModLogger.LogWarning($"Gun Game: weapon-swap coroutine could not start: {ex.Message}");
            return false;
        }
    }

    private static IEnumerator SwapRoutine(
        LocalGame game,
        int raidEpoch,
        int operationGeneration,
        Player player,
        InventoryController inventory,
        Slot slot,
        Weapon weapon,
        IList<Item> spares,
        Action<bool> onComplete)
    {
        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            yield break;

        // Holstering first. Removing the item a hands controller is animating is
        // the corruption case this whole class is shaped around.
        var empty = new Step();
        BeginEmptyHands(player, empty);

        var deadline = Time.time + StepTimeoutSeconds;
        while (!empty.Done && Time.time < deadline)
        {
            if (!OwnsOperation(game, raidEpoch, operationGeneration))
                yield break;

            // The body can be destroyed or replaced under us at any yield.
            if (Gone(player) || !IsCurrentMainPlayer(player))
            {
                Finish(game, raidEpoch, operationGeneration, onComplete, false);
                yield break;
            }

            yield return null;
        }

        // The hands callback may have completed on the frame that also stopped
        // the raid, so the loop can exit without another top-of-loop check.
        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            yield break;

        if (!empty.Done || empty.Error != null)
        {
            Plugin.ModLogger.LogWarning(
                $"Gun Game: could not holster before the swap ({empty.Error ?? "timed out"}) — keeping the current weapon.");
            Finish(game, raidEpoch, operationGeneration, onComplete, false);
            yield break;
        }

        // Dying during the holster animation hands the body to the respawn
        // driver, which is about to replace the whole inventory anyway.
        if (Gone(player) || !IsCurrentMainPlayer(player))
        {
            Finish(game, raidEpoch, operationGeneration, onComplete, false);
            yield break;
        }

        // Last gate before the only live inventory/profile mutation in Gun Game.
        // Stop changes LocalGame.Status synchronously, so a Stopping raid cannot
        // cross this boundary even during the native black-screen delay.
        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            yield break;

        if (!Exchange(player, inventory, slot, weapon, spares))
        {
            Finish(game, raidEpoch, operationGeneration, onComplete, false);
            yield break;
        }

        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            yield break;

        var draw = new Step();
        BeginDraw(player, weapon, draw);

        deadline = Time.time + StepTimeoutSeconds;
        while (!draw.Done && Time.time < deadline)
        {
            if (!OwnsOperation(game, raidEpoch, operationGeneration))
                yield break;

            if (Gone(player) || !IsCurrentMainPlayer(player))
            {
                Finish(game, raidEpoch, operationGeneration, onComplete, false);
                yield break;
            }

            yield return null;
        }

        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            yield break;

        if (Gone(player) || !IsCurrentMainPlayer(player))
        {
            Finish(game, raidEpoch, operationGeneration, onComplete, false);
            yield break;
        }

        // The weapon IS seated, so nothing is lost — but the hands are empty,
        // and reporting success here would clear the mode's dirty flag and end
        // the story there: a player standing in a firefight holding nothing,
        // with no retry coming. Report the failure and let the mode try again;
        // the next attempt removes this weapon and seats a fresh one.
        if (!draw.Done || draw.Error != null)
        {
            Plugin.ModLogger.LogWarning(
                $"Gun Game: new weapon seated but not drawn ({draw.Error ?? "timed out"}) — will retry.");
            Finish(game, raidEpoch, operationGeneration, onComplete, false);
            yield break;
        }

        Finish(game, raidEpoch, operationGeneration, onComplete, true);
    }

    /// <summary>
    /// The inventory edit itself, in one uninterrupted block: nothing yields
    /// between taking the old weapon out and putting the new one in, so there is
    /// no frame in which the player owns neither.
    /// </summary>
    private static bool Exchange(Player player, InventoryController inventory, Slot slot, Weapon weapon, IList<Item> spares)
    {
        Item removed = null;

        // BEFORE the weapon comes out. A magazine the player reloaded from is
        // inside that weapon, and once the weapon is detached its contents keep
        // pointing at addresses that no longer lead anywhere — removing one
        // then throws, and the throw used to take the fresh spares with it.
        try
        {
            ClearHandedOutSpares(inventory);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: clearing old spares failed ({ex.Message}).");
        }

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
            HandOutSpares(inventory, spares);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: spare magazines failed ({ex.Message}).");
        }

        // The round counts, same as the kit path does for a respawn — without
        // this every new magazine reads as an unknown quantity until inspected.
        // Scoped to what we just handed out: sweeping the whole inventory would
        // also uncover the count of every magazine the player looted and chose
        // not to inspect.
        try
        {
            if (player.Profile != null)
            {
                var handedOut = new List<Item>(weapon.GetAllItems());
                if (spares != null)
                    handedOut.AddRange(spares);

                Kits.KitBuilder.RegisterAsChecked(player.Profile, handedOut);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: registering magazine counts failed ({ex.Message}).");
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
        try
        {
            foreach (var spare in HandedOutSpares)
            {
                if (spare == null)
                    continue;

                // The remembered list outlives a life: after a death the old
                // spares are lying in a corpse, in an inventory this controller
                // does not own. Reaching into that would delete items out of
                // someone else's body. Only what is demonstrably still carried
                // HERE may be taken away.
                if (!StillCarried(inventory, spare))
                    continue;

                // One awkward spare must not cost the player the rest of them.
                try
                {
                    var result = ItemManipulator.RemoveWithoutRestrictions(spare, inventory);
                    if (result.Failed)
                        Plugin.ModLogger.LogWarning($"Gun Game: removing a stale spare failed: {result.Error}");
                    else
                        ForgetKnown(inventory, spare);
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning($"Gun Game: removing a stale spare threw: {ex.Message}");
                }
            }
        }
        finally
        {
            // Cleared no matter what — a list that survives a failure would try
            // the same doomed removal on every promotion from here on.
            HandedOutSpares.Clear();
        }
    }

    /// <summary>
    /// Tells the player they own this.
    ///
    /// Anything sitting in a grid starts out UNKNOWN — that is the same
    /// machinery that hides a corpse's pockets until you search them, and it
    /// does not care that the container is your own rig. The game normally
    /// clears it as a side effect of adding through ItemManipulator, whose
    /// AddResult marks the item known when the operation reports success. The
    /// grid-level add used here is a layer below that and knows nothing about
    /// searching, so the magazines landed in the rig greyed out and the player
    /// had to search their own vest before they could reload from them.
    ///
    /// Events stay off deliberately. They exist for a genuine find: they credit
    /// search XP and file the item under raid loot. None of that is true of a
    /// magazine this mod put there — the only thing lost is a live cell refresh
    /// on an inventory screen that happens to be open during a firefight.
    /// </summary>
    private static void MarkKnown(InventoryController inventory, Item item)
    {
        try
        {
            var search = inventory.SearchController as IPlayerSearchController;
            search?.SetItemAsKnown(item, raiseEvents: false);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: marking a spare as known failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The counterpart to MarkKnown, and the reason it exists is cost rather
    /// than correctness: the known-item record is a plain dictionary that every
    /// item view searches LINEARLY. Nothing removes an entry when the item goes
    /// away — the engine only does it as a side effect of its own remove
    /// pipeline, which the direct removal here bypasses — so a full ladder would
    /// leave a growing pile of records for magazines that no longer exist.
    ///
    /// Discovering an item writes TWO records and the engine's own forget clears
    /// only one, so the second is cleared by hand. It holds a reference to a
    /// magazine that has just been thrown away; leaving it there would keep the
    /// whole discarded item alive for the rest of the raid.
    /// </summary>
    private static void ForgetKnown(InventoryController inventory, Item item)
    {
        try
        {
            if (!(inventory.SearchController is ActiveSearchController search))
                return;

            search.ForgetItem(item);
            search._discoveredItems?.Remove(item);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: forgetting a spare failed: {ex.Message}");
        }
    }

    private static bool StillCarried(InventoryController inventory, Item item)
    {
        try
        {
            var carried = inventory.Inventory?.GetPlayerItems(EPlayerItems.Equipment);
            return carried != null && System.Linq.Enumerable.Contains(carried, item);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Drops the memory of handed-out spares without touching them — for when
    /// they are gone from this player's reach anyway (a death leaves them on the
    /// corpse) and the next promotion must not go looking for them.
    /// </summary>
    public static void ForgetSpares() => HandedOutSpares.Clear();

    /// <summary>Destroyed, replaced, or dead — all three mean: stop.</summary>
    private static bool Gone(Player player)
        => player == null || player.HealthController == null || !player.HealthController.IsAlive;

    private static void HandOutSpares(InventoryController inventory, IList<Item> spares)
    {
        if (spares == null || spares.Count == 0)
            return;

        var containers = Kits.KitBuilder.GridContainersOf(inventory.Inventory?.Equipment);
        var placed = 0;
        string lastRefusal = null;
        Item lastRefused = null;

        foreach (var spare in spares)
        {
            if (spare == null)
                continue;

            if (Kits.KitBuilder.AddToAnyGridOf(containers, spare, out var reason))
            {
                MarkKnown(inventory, spare);
                HandedOutSpares.Add(spare);
                placed++;
            }
            else
            {
                lastRefusal = reason;
                lastRefused = spare;
            }
        }

        // One line per promotion instead of one per magazine, naming the magazine
        // and its SIZE — which is the part that was missing. Eight of the 139
        // ladder weapons feed from a 2x2 belt box or drum, and a rig built from
        // 1x2 cells cannot hold one; only the backpack's 3x3 can, and only one.
        // So this is Debug when at least one got stowed and a warning only when
        // the player is going into a fight on the loaded magazine alone.
        if (lastRefused != null)
        {
            var message = $"Gun Game: {placed} of {spares.Count} spare magazines fit — "
                          + $"{Kits.KitBuilder.DescribeItem(lastRefused)} fits no free cell ({lastRefusal})";

            if (placed > 0)
                Plugin.ModLogger.LogDebug(message + ".");
            else
                Plugin.ModLogger.LogWarning(message + " — no reserve for this rung.");
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

    private static bool OwnsRaid(LocalGame game, int raidEpoch)
        => ArenaMode.Respawn.ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
           && Singleton<AbstractGame>.Instantiated
           && ReferenceEquals(Singleton<AbstractGame>.Instance, game);

    private static bool OwnsOperation(LocalGame game, int raidEpoch, int operationGeneration)
        => OwnsRaid(game, raidEpoch)
           && IsSwapping
           && Volatile.Read(ref _operationGeneration) == operationGeneration;

    private static bool IsCurrentMainPlayer(Player player)
        => Singleton<GameWorld>.Instantiated
           && ReferenceEquals(Singleton<GameWorld>.Instance?.MainPlayer, player);

    private static void Finish(
        LocalGame game,
        int raidEpoch,
        int operationGeneration,
        Action<bool> onComplete,
        bool success)
    {
        // Reset/GameEnd owns stale cleanup. An old routine must not clear the
        // busy flag of a newer swap or call back into an ended GunGameMode.
        if (!OwnsOperation(game, raidEpoch, operationGeneration))
            return;

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
