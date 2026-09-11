using System.Collections.Generic;
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
            public int BackpackLoadedCount;
        }

        // Reused across calls instead of allocating a fresh List every refresh tick; only ever
        // touched from the main thread (Unity's Update loop), so a static scratch buffer is safe.
        private static readonly List<Magazine> _magazineScratch = new List<Magazine>();

        public static bool TryGetSpareMagSummary(Player player, Weapon weapon, out SpareMagSummary summary)
        {
            try { return CollectSummary(player, weapon, out summary); }
            finally { Reset(); }
        }

        internal static void Reset() => _magazineScratch.Clear();

        private static bool CollectSummary(Player player, Weapon weapon, out SpareMagSummary summary)
        {
            summary = default;

            if (player?.Inventory?.Equipment == null || weapon == null)
            {
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null || weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                return false;
            }

            InventoryEquipment equipment = player.Inventory.Equipment;
            Item backpack = equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
            Item pockets = equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
            // Includes armored rigs (AVS/TV-110 etc.). ArmorVest is a separate
            // slot, absent from native Inventory.FastAccessSlots; do not confuse
            // its optional custom grids with a TacticalVest's quick-reload grids.
            Item tacticalVest = equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;

            _magazineScratch.Clear();
            CollectMagazines(equipment, _magazineScratch);

            for (int i = 0; i < _magazineScratch.Count; i++)
            {
                Magazine magazine = _magazineScratch[i];
                if (!IsSpareLoadedMagazine(magazine, magazineSlot))
                {
                    continue;
                }

                if (backpack != null && magazine.IsChildOf(backpack))
                {
                    summary.BackpackLoadedCount++;
                    continue;
                }

                if (!IsInQuickReloadArea(magazine, pockets, tacticalVest))
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

        private static bool IsInQuickReloadArea(Magazine magazine, Item pockets, Item tacticalVest)
        {
            return (pockets != null && magazine.IsChildOf(pockets))
                   || (tacticalVest != null && magazine.IsChildOf(tacticalVest));
        }

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

        private static void CollectMagazines(CompoundItem item, List<Magazine> result)
        {
            if (item?.Grids != null)
            {
                for (int g = 0; g < item.Grids.Length; g++)
                {
                    Grid grid = item.Grids[g];
                    if (grid?.Items == null)
                    {
                        continue;
                    }

                    foreach (Item gridItem in grid.Items)
                    {
                        if (gridItem is Magazine magazine)
                        {
                            result.Add(magazine);
                        }
                        else if (gridItem is CompoundItem compoundItem)
                        {
                            CollectMagazines(compoundItem, result);
                        }
                    }
                }
            }

            if (item?.Slots == null)
            {
                return;
            }

            for (int s = 0; s < item.Slots.Length; s++)
            {
                Item slotItem = item.Slots[s].ContainedItem;
                if (slotItem == null)
                {
                    continue;
                }

                if (slotItem is Magazine magazine)
                {
                    result.Add(magazine);
                }
                else if (slotItem is CompoundItem compoundItem)
                {
                    CollectMagazines(compoundItem, result);
                }
            }
        }
    }
}
