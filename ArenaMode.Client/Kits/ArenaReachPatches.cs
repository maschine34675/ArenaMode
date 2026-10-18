using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
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
///
/// Widening reach has one consequence the game does not track for us: the G
/// key's pick, Equipment.TopPriorityGrenade. Drawing ANY grenade makes it the
/// pick (FastAccessGrenadeItemView.OnSetInHands), and a tap of G draws the pick
/// whenever G has any candidate at all (PlayerInputTranslator.vmethod_1). The
/// view moves the pick on when a grenade leaves, but only when its parent is the
/// item in a GrenadeThrowingSlots slot (OnItemRemoved) — the rig or pockets,
/// plus Backpack, SecuredContainer and ArmBand under the installed
/// UseItemsAnywhere mod. A grenade bound and drawn from anywhere else — the
/// backpack without that mod, a pouch nested in the rig or backpack with it —
/// is thrown and the pick keeps naming it. Every later tap of G then hands
/// TryProceed an item with no address, which IsAtReachablePlace refuses (and
/// WornAndWhole below with it), and the player falls back to
/// SetFirstAvailableItem: G silently stops throwing, even with grenades in the
/// rig, until hold-G or another grenade draw picks again. The two pick patches
/// below close that, one where the game does its own bookkeeping and one at the
/// key.
/// </summary>
internal static class ArenaReachPatches
{
    private static int? _reportedPickFailureEpoch;

    public static void Enable()
    {
        new ArenaReachBindableAnywherePatch().Enable();
        new ArenaReachableAnywherePatch().Enable();
        EnablePickPatch(() => new ArenaReachPickAfterRemovalPatch());
        EnablePickPatch(() => new ArenaReachPickBeforeTapThrowPatch());
    }

    /// <summary>
    /// The pick patches are cleanup whose absence is only the documented defect,
    /// and Plugin.Awake enables every patch in one unguarded sequence: a target
    /// that drifted in a game update (a generated vmethod_1, a parameter named
    /// obj) must not stop everything after this line from loading.
    /// </summary>
    private static void EnablePickPatch(Func<ModulePatch> create)
    {
        try
        {
            create().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena grenade pick patch not enabled; tap-G can stay blocked after a reach-drawn grenade is thrown: {ex}");
        }
    }

    private static bool Wanted =>
        ArenaSession.IsActive && Settings.UseItemsAnywhere.Value;

    /// <summary>
    /// The pick patches gate on the arena alone, not on the setting as well. F12
    /// is live: without the UseItemsAnywhere mod, a backpack pick made while
    /// reach was on is stranded the moment reach is turned off. And what they do
    /// is never worse than native: they only act on a pick G cannot draw, where
    /// the game's only outcome is the SetFirstAvailableItem fallback, and replace
    /// it with the game's own next pick or its own no-pick branch. Most such
    /// picks are reach's. A few are native bookkeeping gaps — an unexamined rig
    /// grenade the view lists anyway, a dropped backpack's grenades left in its
    /// list under that mod — which inside the arena now take the same route.
    /// Outside the arena nothing here runs.
    /// </summary>
    private static bool PickGuardWanted => ArenaSession.IsActive;

    /// <summary>
    /// Once per raid, not per event: a failure here would repeat on every
    /// grenade event. What remains is the game's own handling — at worst the
    /// documented defect — not a broken raid.
    /// </summary>
    private static void ReportPickFailure(Exception ex)
    {
        if (_reportedPickFailureEpoch == ArenaSession.ArmEpoch)
            return;

        _reportedPickFailureEpoch = ArenaSession.ArmEpoch;
        Plugin.ModLogger.LogWarning($"Arena grenade pick cleanup failed; the game's own handling continues (reported once per raid): {ex}");
    }

    /// <summary>
    /// The removal is the pick itself, or a container still holding it — a
    /// dropped backpack, a pouch dragged into loot — whose event names the
    /// container, not the grenade.
    /// </summary>
    private static bool LeftWith(Item removed, ThrowWeap pick)
        => ReferenceEquals(removed, pick) || (removed is CompoundItem container && container.Contains(pick));

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

    private sealed class ArenaReachBindableAnywherePatch : ModulePatch
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

    private sealed class ArenaReachableAnywherePatch : ModulePatch
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

    /// <summary>
    /// Finishes the view's own bookkeeping for the grenades it does not track.
    ///
    /// Runs after the native handler, so a rig or pocket grenade has already been
    /// dealt with and the pick no longer names it; what is left is exactly the
    /// case the native condition missed — the pick itself leaving, or a
    /// container leaving with the pick inside. The replacement is the native one
    /// too, SetNewTopPriorityGrenade: the same next pick and the same quick-slot
    /// icon the game produces for a rig grenade. It comes from the view's own
    /// list, which can hold stale entries of its own (the key backstop below
    /// catches those).
    ///
    /// A pick that only moved out of an untracked place and is still reachable —
    /// backpack into a pouch, with reach on — keeps the pick, because a tap of G
    /// can still draw it. (A rig grenade moved away loses the pick natively,
    /// before this runs.) Reachability is asked through IsAtReachablePlace, the
    /// very question TryProceed asks at the key, so the two cannot disagree.
    ///
    /// The view keeps its controller in a protected field of QuickSlotView,
    /// hence the injected ___InventoryController. Every exception is swallowed:
    /// the game raises this event through one multicast invocation wrapped in a
    /// single try (ItemController.SafeRemoveItemEvent), so a throw here would
    /// skip every subscriber after the view.
    /// </summary>
    private sealed class ArenaReachPickAfterRemovalPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(FastAccessGrenadeItemView), nameof(FastAccessGrenadeItemView.OnItemRemoved));

        [PatchPostfix]
        private static void Postfix(
            FastAccessGrenadeItemView __instance,
            RemoveItemEventArgs obj,
            InventoryController ___InventoryController)
        {
            if (!PickGuardWanted)
                return;

            try
            {
                if (obj == null || obj.Status != CommandStatus.Succeed)
                    return;

                InventoryController controller = ___InventoryController;
                InventoryEquipment equipment = controller?.Inventory?.Equipment;
                if (equipment == null || controller.ID != obj.OwnerId)
                    return;

                ThrowWeap pick = equipment.TopPriorityGrenade;
                if (pick == null || !LeftWith(obj.Item, pick) || controller.IsAtReachablePlace(pick))
                    return;

                __instance.SetNewTopPriorityGrenade();
            }
            catch (Exception ex)
            {
                ReportPickFailure(ex);
            }
        }
    }

    /// <summary>
    /// The backstop at the key, for a pick the view could not fix: reach turned
    /// off after a backpack grenade was picked (without the UseItemsAnywhere
    /// mod), a removal while no quick-slot view was listening, or a replacement
    /// the view took from a stale entry in its own list.
    ///
    /// A pick TryProceed would refuse is cleared, and the native method then runs
    /// its own no-pick branch — a random grenade from G's candidate list, or
    /// nothing if that list is empty — exactly as it does before anything has
    /// been picked; the grenade it draws becomes the pick and the quick-slot icon
    /// through OnSetInHands. Until then the icon still shows the old grenade,
    /// because only the view writes its icon. A pick G can draw is left alone,
    /// however it got there. This runs before the native early returns (hands
    /// busy, and so on); clearing then is harmless, because G could never have
    /// drawn that pick anyway.
    /// </summary>
    private sealed class ArenaReachPickBeforeTapThrowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(PlayerInputTranslator), nameof(PlayerInputTranslator.vmethod_1));

        [PatchPrefix]
        private static void Prefix(PlayerInputTranslator __instance)
        {
            if (!PickGuardWanted)
                return;

            try
            {
                InventoryController controller = __instance?._player?.InventoryController;
                InventoryEquipment equipment = controller?.Inventory?.Equipment;
                ThrowWeap pick = equipment?.TopPriorityGrenade;
                if (pick == null || controller.IsAtReachablePlace(pick))
                    return;

                equipment.TopPriorityGrenade = null;
            }
            catch (Exception ex)
            {
                ReportPickFailure(ex);
            }
        }
    }
}
