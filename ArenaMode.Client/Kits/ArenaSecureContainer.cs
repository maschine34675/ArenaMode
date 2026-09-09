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
/// </summary>
internal static class ArenaSecureContainer
{
    private const float CheckIntervalSeconds = 2f;

    private static float _nextCheckAt;
    private static bool _loggedFailure;
    private static int _emptied;

    public static void Reset()
    {
        _nextCheckAt = 0f;
        _loggedFailure = false;
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
        if (!ArenaSession.IsActive || !Settings.EmptySecureContainer.Value || Time.time < _nextCheckAt)
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
            return false;

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
