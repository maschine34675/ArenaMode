using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Kits;

/// <summary>
/// Leaves the contents of the secure container at home.
///
/// The container itself has to come along: it is unlootable, so a body carrying
/// one into the raid and dying with it would lose it — which is why
/// EquipmentSurgeon transfers it in the first place. That transfer is a SAFETY
/// measure, and it accidentally became a supply line: a Kappa full of Grizzlies
/// and Zagustins outclasses the medicine of every kit in the catalogue, which
/// makes the whole medical ladder — and the part of the Tactical buy price that
/// pays for it — decorative.
///
/// Worse, it is invisible. A raid that felt easy could be the tuning or could be
/// what was in the container, and nothing in the log tells the two apart.
///
/// Emptying the live copy is safe because the server snapshots the profile at
/// raid start and restores it at raid end: this is the same mechanism that lets
/// an arena raid cost nothing at all. The items are back in the stash the moment
/// the raid is over, whatever happens in between.
///
/// Not in a protected 5v5 raid. That mode issues no kit: the PMC keeps their
/// own loadout, and the secure container is one of the slots the round rebuild
/// carries over from the dead body as the same object (TeamArenaLoadoutSnapshot
/// leaves it out of the snapshot for exactly that reason; nothing ever puts
/// consumed contents back). There is no medical ladder for a pouch of
/// Grizzlies to outclass, so the argument above has nothing to stand on, and
/// what the emptying did there was worse than nothing. It ran one frame after
/// the latch, destroying the player's own supplies, and any non-consumable it
/// destroyed would have failed the protected-loadout boundary at the first
/// death and ended the match as no contest; and when it did not get that far,
/// the slot was hidden over contents that were still there and still bound to
/// the quick slots — usable by key, invisible on screen, which is how it was
/// reported.
///
/// The same argument applies, for the same reason, to Zombie's own-gear option
/// (item 4, 2026-10-06 follow-up, ArenaSession.ZombieOwnGearActive): it issues
/// no kit either, protects the player's real equipment through
/// ZombieOwnGearLoadout's own TeamArenaLoadoutSnapshot instance instead of
/// PitFireTeam's roster, and the container is never emptied for the same
/// reason — no medical ladder to outclass, and the player's own real supplies
/// would otherwise be destroyed one frame after the latch.
/// </summary>
internal static class ArenaSecureContainer
{
    private const float CheckIntervalSeconds = 2f;

    private static float _nextCheckAt;
    private static bool _loggedFailure;
    private static bool _loggedRefusal;
    private static int _emptied;

    /// <summary>
    /// Whether this raid leaves the container's contents at home at all. Asked
    /// by the emptying below and by the slot-hiding patch alike, so the two
    /// can never disagree again: a hidden slot is only ever an emptied one.
    /// </summary>
    internal static bool LeavesContentsAtHome
        => ArenaSession.IsActive
           && Settings.EmptySecureContainer.Value
           && !PitFireTeamCompat.HasFrozenTeamRoster
           // Zombie's own-gear option (item 4): same reasoning as the frozen
           // PitFireTeam roster check above — own gear has no medical ladder
           // for a pouch of Grizzlies to outclass, and emptying the container
           // would destroy the player's own real supplies. See
           // ZombieOwnGearLoadout's own doc comment.
           && !ArenaSession.ZombieOwnGearActive;

    public static void Reset()
    {
        _nextCheckAt = 0f;
        _loggedFailure = false;
        _loggedRefusal = false;
        _emptied = 0;
    }

    /// <summary>
    /// Polled, like the quick slots, and for the same reason: several paths hand
    /// out equipment and a poll covers all of them, including the ones that do
    /// not exist yet. It finds nothing to do within a second or two of a kit
    /// being issued and then costs a slot lookup.
    /// </summary>
    public static void Tick()
    {
        if (!LeavesContentsAtHome || Time.time < _nextCheckAt)
            return;

        _nextCheckAt = Time.time + CheckIntervalSeconds;

        try
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            var controller = player.InventoryController;
            var equipment = controller?.Inventory?.Equipment;
            if (equipment == null)
                return;

            // Special slots are not emptied here any more: the kits ask for the
            // plain pockets, which HAS none, so there is nothing to empty and
            // nothing of the player's is destroyed mid-round.
            var removed = EmptyContainer(controller, equipment);
            if (removed <= 0)
                return;

            _emptied += removed;
            Plugin.ModLogger.LogInfo(
                $"Arena: left {removed} item(s) out of the secure container ({_emptied} this raid). "
                + "They are in the profile snapshot and come back at raid end.");
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena secure container: emptying failed (silenced for this raid): {ex}");
            }
        }
    }

    private static int EmptyContainer(InventoryController controller, InventoryEquipment equipment)
    {
        var container = equipment.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem as CompoundItem;
        if (container?.Grids == null)
            return 0;

        // Materialised before removing: the grid's own collection is what the
        // removal mutates, and enumerating it while it changes throws.
        var inside = container.Grids.SelectMany(grid => grid.Items).ToList();

        return inside.Count(item => Discard(controller, item));
    }

    private static bool Discard(InventoryController controller, Item item)
    {
        if (item == null)
            return false;

        // Unbound FIRST, because the removal will not do it. ItemManipulator's
        // Remove path never calls TryUnbindItems at all — only the MOVE path
        // does — so a bound container item would be destroyed with its binding
        // intact and the key would look occupied and do nothing. (The first
        // version of this comment blamed the reach widening for that; it is
        // simpler and worse than that, the removal just never unbinds.)
        Unbind(controller, item);

        var result = ItemManipulator.RemoveWithoutRestrictions(item, controller);
        if (!result.Succeeded)
        {
            // A refusal (a locked address, an item the game is busy with) is
            // the one path that leaves the slot hidden over contents that are
            // still there, so it is said out loud rather than swallowed. Once:
            // the poll would otherwise repeat it every two seconds. There is
            // no dry run to ask first — the native Remove with restrictions
            // ignored performs the removal even when told to simulate.
            if (!_loggedRefusal)
            {
                _loggedRefusal = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena secure container: could not leave {item.TemplateId} out ({result.Error}); "
                    + "it stays where it is, unbound (further refusals this raid are not logged).");
            }

            return false;
        }

        // The container changes immediately; only RaiseEvents tells the views.
        // Skipping it is the trap this project has hit twice — the item leaves
        // the model and stays on the screen.
        result.Value.RaiseEvents(controller, CommandStatus.Succeed);
        return true;
    }

    /// <summary>
    /// Unbinds the item AND everything inside it.
    ///
    /// The game's own walker rather than a hand-written check of the one item:
    /// a container holds cases, a case holds a medkit, and the medkit is what
    /// was bound. Looking only at the item being removed left exactly the dead
    /// key this is here to prevent.
    /// </summary>
    private static void Unbind(InventoryController controller, Item item)
    {
        if (controller.Inventory?.FastAccess?.BoundItems == null)
            return;

        var unbound = new List<UnbindResult>();
        ItemManipulator.TryUnbindItems(controller, item, in unbound, simulate: false);

        foreach (var result in unbound)
            result.RaiseEvents(controller, CommandStatus.Succeed);
    }
}
