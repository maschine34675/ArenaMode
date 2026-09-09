using ArenaMode.Config;
using ArenaMode.Session;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Kits;

/// <summary>
/// Hides the secure container from the inventory screen during an arena raid,
/// the way a scav raid hides it.
///
/// Cosmetic, and downstream of the real change: the container is emptied anyway,
/// so this only stops the player opening a slot that has nothing in it and
/// wondering whether that is a bug.
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
        new SeeSlotPatch().Enable();
        new SeeEquipmentSlotPatch().Enable();
    }

    private static void Hide(ref bool visible, EquipmentSlot slotName)
    {
        if (!visible || slotName != EquipmentSlot.SecuredContainer)
            return;

        if (!ArenaSession.IsActive || !Settings.EmptySecureContainer.Value)
            return;

        visible = false;
    }

    private sealed class SeeSlotPatch : ModulePatch
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
    private sealed class SeeEquipmentSlotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAllowedToSeeEquipmentSlot));

        [PatchPostfix]
        private static void Postfix(ref bool __result, EquipmentSlot slotName) => Hide(ref __result, slotName);
    }
}
