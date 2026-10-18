using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Kits;

/// <summary>
/// Hides the secure container from the inventory screen during an arena raid.
///
/// Not "the way a scav raid does", which is what this said first and is wrong
/// about the game: _hiddenFromOthers holds SecuredContainer, so vanilla hides it
/// from OTHER people and never from its owner — a scav sees their own container
/// slot just as a PMC does. Hiding it from the owner is something only this mod
/// does, and only because the arena has emptied it.
///
/// Cosmetic, and downstream of the real change: the container is emptied anyway,
/// so this only stops the player opening a slot that has nothing in it and
/// wondering whether that is a bug. Downstream literally: it asks the emptying
/// service whether this raid empties the container, and hides the slot only
/// then. A protected 5v5 raid keeps the PMC's own loadout, container and all
/// (the container is carried over between rounds as the same object), and
/// shows it — the first version hid it there over contents that were still
/// present and still bound to the quick slots, usable by key and invisible on
/// screen.
///
/// Through the game's own question rather than through its answer.
/// InventoryController keeps the hidden-slot lists in STATIC arrays —
/// _hiddenFromSelf and _hiddenFromOthers — which every controller in the process
/// shares, bots included. Adding to those and restoring them later is the
/// pattern this project has twice had to argue an exception for; here there is
/// no need, because the visibility questions are virtual and asked per call.
/// </summary>
internal static class ArenaSlotVisibilityPatches
{
    public static void Enable()
    {
        new ArenaSlotVisibilitySeeSlotPatch().Enable();
        new ArenaSlotVisibilitySeeEquipmentSlotPatch().Enable();
    }

    private static void Hide(ref bool visible, EquipmentSlot slotName)
    {
        if (!visible || slotName != EquipmentSlot.SecuredContainer)
            return;

        if (!ArenaSecureContainer.LeavesContentsAtHome)
            return;

        visible = false;
    }

    private sealed class ArenaSlotVisibilitySeeSlotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAllowedToSeeSlot));

        [PatchPostfix]
        private static void Postfix(ref bool __result, EquipmentSlot slotName) => Hide(ref __result, slotName);
    }

    /// <summary>
    /// The equipment screen asks a second, near-identical question. Patching one
    /// and not the other would hide the container in one panel and show it,
    /// empty, in the next.
    /// </summary>
    private sealed class ArenaSlotVisibilitySeeEquipmentSlotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAllowedToSeeEquipmentSlot));

        [PatchPostfix]
        private static void Postfix(ref bool __result, EquipmentSlot slotName) => Hide(ref __result, slotName);
    }
}
