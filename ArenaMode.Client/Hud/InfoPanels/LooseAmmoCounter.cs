using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    // Counts carried cartridges that fit the equipped weapon. Relevant for
    // shell-by-shell weapons (shotguns, bolt actions, break actions), where
    // there are no spare magazines to display and the reserve is loose rounds.
    internal static class LooseAmmoCounter
    {
        internal struct LooseAmmoSummary
        {
            public int ReadyRounds;
            public int BackpackRounds;
        }

        // Reused across calls instead of allocating a fresh List every refresh
        // tick; only ever touched from the main thread (Unity's Update loop).
        private static readonly List<Ammo> _ammoScratch = new List<Ammo>();

        public static bool TryGetSummary(Player player, Weapon weapon, out LooseAmmoSummary summary)
        {
            try { return CollectSummary(player, weapon, out summary); }
            finally { Reset(); }
        }

        internal static void Reset() => _ammoScratch.Clear();

        private static bool CollectSummary(Player player, Weapon weapon, out LooseAmmoSummary summary)
        {
            summary = default;

            if (player?.Inventory?.Equipment == null || weapon == null)
            {
                return false;
            }

            IContainer acceptor = GetAmmoAcceptor(weapon);
            if (acceptor == null)
            {
                return false;
            }

            InventoryEquipment equipment = player.Inventory.Equipment;
            Item backpack = equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
            Item pockets = equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
            // Armored rigs also occupy TacticalVest, just like unarmored rigs.
            Item tacticalVest = equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;

            _ammoScratch.Clear();
            CollectAmmo(equipment, _ammoScratch);

            for (int i = 0; i < _ammoScratch.Count; i++)
            {
                Ammo ammo = _ammoScratch[i];
                if (ammo.IsUsed || !acceptor.CanAccept(ammo))
                {
                    continue;
                }

                // A stack of shells counts as its shell count, not as one item.
                int rounds = ammo.StackObjectsCount > 0 ? ammo.StackObjectsCount : 1;

                if (backpack != null && ammo.IsChildOf(backpack))
                {
                    summary.BackpackRounds += rounds;
                }
                else if ((pockets != null && ammo.IsChildOf(pockets))
                         || (tacticalVest != null && ammo.IsChildOf(tacticalVest)))
                {
                    summary.ReadyRounds += rounds;
                }
            }

            return true;
        }

        // Whatever the weapon feeds from decides which cartridges fit: the
        // chamber for break actions and bolt guns, otherwise the internal
        // magazine's cartridge stack. Both expose the game's own item filter,
        // so caliber and shell-type rules stay identical to the real reload.
        private static IContainer GetAmmoAcceptor(Weapon weapon)
        {
            Slot[] chambers = weapon.Chambers;
            if (chambers != null)
            {
                for (int i = 0; i < chambers.Length; i++)
                {
                    if (chambers[i] != null)
                    {
                        return chambers[i];
                    }
                }
            }

            return weapon.GetCurrentMagazine()?.Cartridges;
        }

        // Walks the equipped item tree the same way the magazine counter does.
        // Cartridges already loaded into a magazine are not reachable here:
        // Magazine exposes its stack through Containers, not through Slots.
        private static void CollectAmmo(CompoundItem item, List<Ammo> result)
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
                        if (gridItem is Ammo ammo)
                        {
                            result.Add(ammo);
                        }
                        else if (gridItem is CompoundItem compoundItem)
                        {
                            CollectAmmo(compoundItem, result);
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

                if (slotItem is Ammo ammo)
                {
                    result.Add(ammo);
                }
                else if (slotItem is CompoundItem compoundItem)
                {
                    CollectAmmo(compoundItem, result);
                }
            }
        }
    }
}
