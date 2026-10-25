using System.Collections.Generic;
using ArenaMode.Compat;
using ArenaMode.Kits;
using EFT;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    internal static class MagazineInventoryCounter
    {
        internal struct SpareMagSummary
        {
            public int FullCount;
            public int PartialCount;
        }

        // Same equipment-slot array shape GetAcceptableItemsNonAlloc expects
        // (InventoryController.cs:887); a one-element static array so the
        // backpack query below allocates nothing per call.
        private static readonly EquipmentSlot[] BackpackSlot = { EquipmentSlot.Backpack };

        // Reused across calls instead of allocating a fresh List every refresh tick; only ever
        // touched from the main thread (Unity's Update loop), so a static scratch buffer is safe.
        private static readonly List<Magazine> _magazineScratch = new List<Magazine>();
        private static readonly List<Magazine> _backpackScratch = new List<Magazine>();

        public static bool TryGetSpareMagSummary(Player player, Weapon weapon, out SpareMagSummary summary)
        {
            try { return CollectSummary(player, weapon, out summary); }
            finally { Reset(); }
        }

        internal static void Reset()
        {
            _magazineScratch.Clear();
            _backpackScratch.Clear();
        }

        private static bool CollectSummary(Player player, Weapon weapon, out SpareMagSummary summary)
        {
            summary = default;

            InventoryController controller = player?.InventoryController;
            if (controller?.Inventory?.Equipment == null || weapon == null)
            {
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null || weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                return false;
            }

            // The exact candidate set the reload button offers, in the exact
            // order it builds it: FirearmHandsInputTranslator.ReloadExternalMagazine
            // (_decompiled/EFT/FirearmHandsInputTranslator.cs:1206-1207) calls this
            // very query — never a patched copy of it (AGENTS.md: "do not detour
            // constructed generic inventory queries on Mono", BACKPACK-RELOAD-REVIEW.md)
            // — with the one predicate the game applies before ranking candidates:
            // mag.Count > 0. On the local body this resolves to
            // PlayerOwnerInventoryController's override (Player.cs:22345-22360),
            // which additionally skips a rig/pockets container the search
            // controller has not marked searched yet, one level deep
            // (InventoryController.cs:882-907: Inventory.FastAccessSlots' DIRECT
            // children only, nested pouches are never reached this way). The
            // actual pick then also requires ItemManipulator.CheckMoveIgnoringTargetItem
            // to succeed (ItemManipulator.cs:1296), which is not reproduced here —
            // for DISPLAY the coarser rules below (compatible, not the loaded
            // magazine) are what decide the bucket a spare lands in.
            _magazineScratch.Clear();
            if (UseItemsAnywhereCompat.OwnsItemAccess)
            {
                // Match the foreign reload query, including its current enabled state and
                // configured slots. Never guess broader access if its contract has drifted.
                if (!UseItemsAnywhereCompat.TryCollectMagazines(controller, _magazineScratch, magazine => magazine.Count > 0))
                    return false;
            }
            else
            {
                controller.GetReachableItemsOfTypeNonAlloc(_magazineScratch, magazine => magazine.Count > 0);
            }

            // Arena's own second source, appended exactly when the reload path
            // would append it (ArenaBackpackReload, BACKPACK-RELOAD-REVIEW.md):
            // the backpack's direct children, one level deep like the query
            // above — nested pouches inside the backpack are not loadable
            // either, so they are never counted here or anywhere else in this
            // file. Collected into its own scratch buffer and merged by
            // reference so a magazine already found through the native query
            // (impossible for a real item, since it would need two parents at
            // once, but cheap to guard) is never counted twice.
            if (ArenaBackpackReload.AppendsBackpackFor(controller))
            {
                _backpackScratch.Clear();
                controller.GetAcceptableItemsNonAlloc(BackpackSlot, _backpackScratch, magazine => magazine.Count > 0);
                for (int i = 0; i < _backpackScratch.Count; i++)
                {
                    Magazine candidate = _backpackScratch[i];
                    if (!ContainsReference(_magazineScratch, candidate))
                    {
                        _magazineScratch.Add(candidate);
                    }
                }
            }

            for (int i = 0; i < _magazineScratch.Count; i++)
            {
                Magazine magazine = _magazineScratch[i];
                if (!IsSpareLoadedMagazine(magazine, magazineSlot))
                {
                    continue;
                }

                if (magazine.Count >= magazine.MaxCount)
                {
                    summary.FullCount++;
                }
                else
                {
                    summary.PartialCount++;
                }
            }

            return true;
        }

        // Count > 0 is already the native predicate above; what is left is the
        // display-relevant part of "would this actually go into the gun":
        // compatible with ITS magazine slot, and not already inserted
        // somewhere (GetRootItem follows only a merged parent, which an
        // inserted magazine is — in this weapon or another one carried).
        private static bool IsSpareLoadedMagazine(Magazine magazine, Slot magazineSlot)
        {
            if (magazine == null || magazine.Count <= 0)
            {
                return false;
            }

            if (magazine.GetRootItem() is Weapon)
            {
                return false;
            }

            return magazineSlot.CanAccept(magazine);
        }

        private static bool ContainsReference(List<Magazine> list, Magazine item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
