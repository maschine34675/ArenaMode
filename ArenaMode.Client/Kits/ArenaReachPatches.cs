using ArenaMode.Config;
using ArenaMode.Session;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Kits;

/// <summary>
/// Lets anything worn be used and bound, not only what is in a pocket or a rig.
///
/// Vanilla restricts both questions to a short list of slots — Pockets and
/// TacticalVest for binding, those plus the weapon slots for reaching. That is a
/// sensible rule for a looting game and a bad one here: the kit generator puts
/// the bulky medicine where it FITS, and a Grizzly is 2x2 and a Surv12 is 3x1,
/// so the best medical items in the catalogue land in the backpack and become
/// unreachable — exactly the items the top tiers are paying for.
///
/// The technique is the one UseItemsAnywhere uses: widen the LOCATION test and
/// leave everything else alone. That mod ships without a licence file, so this
/// is written from the mechanism rather than from its code, which is the
/// standing rule for the unlicensed references in this project.
///
/// Only the location is relaxed. The type filter is copied faithfully from the
/// method being patched — a weapon, a grenade, a knife, medicine, food, a
/// rangefinder, a compass or a radio — so nothing becomes bindable that the game
/// would not have bound from a pocket. And both patches only ever turn a NO into
/// a yes: a refusal for any other reason still stands.
/// </summary>
internal static class ArenaReachPatches
{
    public static void Enable()
    {
        new BindableAnywherePatch().Enable();
        new ReachableAnywherePatch().Enable();
    }

    private static bool Wanted =>
        ArenaSession.IsActive && Settings.UseItemsAnywhere.Value;

    /// <summary>
    /// The kinds of item the game is willing to quick-use at all, mirrored from
    /// InventoryController.IsAtBindablePlace and IsAtReachablePlace, which share
    /// this test verbatim.
    /// </summary>
    private static bool IsQuickUsable(Item item)
    {
        if (item is Weapon || item is ThrowWeap || item is Meds || item is FoodDrink
            || item is PortableRangeFinder || item is Compass || item is RadioTransmitter)
            return true;

        return item.GetItemComponent<KnifeComponent>() != null;
    }

    /// <summary>Worn somewhere, and not a half-built thing the game would refuse anyway.</summary>
    private static bool WornAndWhole(InventoryController controller, Item item)
    {
        if (item?.CurrentAddress == null || controller?.Inventory?.Equipment == null)
            return false;

        if (item is CompoundItem compound && compound.MissingVitalParts.Any())
            return false;

        return controller.Inventory.Equipment.Contains(item) && controller.Examined(item);
    }

    private sealed class BindableAnywherePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace));

        [PatchPostfix]
        private static void Postfix(InventoryController __instance, ref bool __result, Item item)
        {
            if (__result || !Wanted || !IsQuickUsable(item) || !WornAndWhole(__instance, item))
                return;

            __result = true;
        }
    }

    private sealed class ReachableAnywherePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtReachablePlace));

        [PatchPostfix]
        private static void Postfix(InventoryController __instance, ref bool __result, Item item)
        {
            if (__result || !Wanted || !IsQuickUsable(item) || !WornAndWhole(__instance, item))
                return;

            __result = true;
        }
    }
}
