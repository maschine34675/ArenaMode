using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    internal enum GrenadeKind
    {
        Frag = 0,
        Flash = 1,
        Smoke = 2
    }

    // Counts the local body's hand grenades by kind, split into what the native
    // quick throw can actually take ("ready") and what is carried elsewhere
    // ("stored"). Hand grenades are ThrowWeap items; 40mm/30mm launcher rounds
    // and rockets are Ammo/Rocket, so an `is ThrowWeap` test already excludes
    // them (EFT.InventoryLogic/ThrowWeap.cs:9, Ammo.cs:10, Rocket.cs:7).
    internal static class GrenadeInventoryCounter
    {
        internal struct GrenadeSummary
        {
            public int ReadyFrag, ReadyFlash, ReadySmoke;
            public int StoredFrag, StoredFlash, StoredSmoke;

            public int Ready(GrenadeKind kind)
                => kind == GrenadeKind.Frag ? ReadyFrag : kind == GrenadeKind.Flash ? ReadyFlash : ReadySmoke;

            public int Stored(GrenadeKind kind)
                => kind == GrenadeKind.Frag ? StoredFrag : kind == GrenadeKind.Flash ? StoredFlash : StoredSmoke;

            public bool IsEmpty
                => ReadyFrag + ReadyFlash + ReadySmoke + StoredFrag + StoredFlash + StoredSmoke == 0;

            internal void Add(GrenadeKind kind, bool ready)
            {
                switch (kind)
                {
                    case GrenadeKind.Frag: if (ready) ReadyFrag++; else StoredFrag++; break;
                    case GrenadeKind.Flash: if (ready) ReadyFlash++; else StoredFlash++; break;
                    default: if (ready) ReadySmoke++; else StoredSmoke++; break;
                }
            }
        }

        // Reused across refreshes; only ever touched from the main thread.
        // Cleared after every read, including failed ones, like the magazine
        // counter, so no item reference outlives a refresh or a body.
        private static readonly List<ThrowWeap> _readyScratch = new List<ThrowWeap>();
        private static readonly List<ThrowWeap> _carriedScratch = new List<ThrowWeap>();

        public static bool TryGetSummary(Player player, out GrenadeSummary summary)
        {
            try { return CollectSummary(player, out summary); } finally { Reset(); }
        }

        internal static void Reset()
        {
            _readyScratch.Clear();
            _carriedScratch.Clear();
        }

        private static bool CollectSummary(Player player, out GrenadeSummary summary)
        {
            summary = default;

            // Natively Player.Inventory reads through this very controller
            // (Player.cs:24511), so take the equipment from it: a missing
            // controller then returns false instead of throwing inside the
            // Inventory getter.
            InventoryController controller = player?.InventoryController;
            InventoryEquipment equipment = controller?.Inventory?.Equipment;
            if (equipment == null)
            {
                return false;
            }

            // "Ready" is the candidate list of the native G key, obtained through
            // the same virtual call (PlayerInputTranslator.cs:502-503):
            // - vanilla: direct children of Inventory.FastAccessSlots, i.e. the
            //   Pockets and TacticalVest containers (Inventory.cs:193-197), with
            //   Examined as the filter;
            // - the local controller's override additionally skips a rig or
            //   pockets that has not been searched yet (Player.cs:22345-22360);
            // - the installed UseItemsAnywhere 1.4.0 rewrites that static slot
            //   list at startup to add Backpack, SecuredContainer and ArmBand, so
            //   loose backpack grenades really are quick-throwable there
            //   (KIT-POLISH-REVIEW.md).
            // Counting ready from this list rather than from our own walk keeps
            // the row equal to G on either install. The helper APPENDS and never
            // clears (InventoryController.cs:887-907), hence the explicit Clear.
            //
            // Known exception: tap-G prefers Equipment.TopPriorityGrenade whenever
            // this list is non-empty (PlayerInputTranslator.cs:509-516). A grenade
            // drawn earlier through a bound number key (Arena's reach patches allow
            // binding one outside rig/pockets) stays that pick, so tap-G can draw a
            // grenade shown here as stored. That takes a deliberate chain of actions.
            _readyScratch.Clear();
            controller.GetReachableItemsOfTypeNonAlloc(_readyScratch, grenade => controller.Examined(grenade));

            // "Carried" is every grenade a player would still call "mine":
            // including pouches nested inside rig/pockets and the backpack.
            // SecuredContainer and ArmorVest are not walked, matching the magazine
            // and loose-round counters; no stock secure container accepts ThrowWeap
            // (its grid ExcludedFilter lists the ThrowWeap node
            // 543be6564bdc2df4348b4568) and no stock armband has a grid.
            _carriedScratch.Clear();
            CollectGrenades(equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem, _carriedScratch);
            CollectGrenades(equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem, _carriedScratch);
            CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch);

            // Never compare a kind against default: GrenadeKind.Frag is 0, the
            // same trap as EBoundItem/ThrowWeapType (AGENTS.md). TryClassify's
            // bool is the only "found" signal.
            for (int i = 0; i < _readyScratch.Count; i++)
            {
                if (TryClassify(_readyScratch[i], out GrenadeKind kind))
                {
                    summary.Add(kind, ready: true);
                }
            }

            // Stored = carried minus ready. Ready grenades outside the walked
            // subtrees (possible under UseItemsAnywhere) are still counted above.
            for (int i = 0; i < _carriedScratch.Count; i++)
            {
                ThrowWeap grenade = _carriedScratch[i];
                if (ContainsReference(_readyScratch, grenade) || !TryClassify(grenade, out GrenadeKind kind))
                {
                    continue;
                }

                summary.Add(kind, ready: false);
            }

            return true;
        }

        // Stock items only use frag_grenade, flash_grenade and smoke_grenade
        // (items.json). stun_grenade joins flash, as the bots group them
        // (BotSuppressGrenade.cs:63). gas/incendiary/sonar are unused by stock
        // items; for those and any modded value, classify by what the grenade
        // does, using the template fields the explosion code itself reads
        // (ExplosionSharedMethods.cs:362/368). ExplosionEffectType and
        // FragmentType are not usable: RDG-2 has no effect type, and smoke and
        // flash grenades still carry a FragmentType. Contusion is nonzero on
        // frags too, so it does not separate flash from frag either.
        internal static bool TryClassify(ThrowWeap grenade, out GrenadeKind kind)
        {
            switch (grenade.ThrowType)
            {
                case ThrowWeapType.frag_grenade:
                    kind = GrenadeKind.Frag;
                    return true;
                case ThrowWeapType.flash_grenade:
                case ThrowWeapType.stun_grenade:
                    kind = GrenadeKind.Flash;
                    return true;
                case ThrowWeapType.smoke_grenade:
                    kind = GrenadeKind.Smoke;
                    return true;
            }

            if (grenade.FragmentsCount > 0 || grenade.MaxExplosionDistance > 0f)
            {
                kind = GrenadeKind.Frag;
                return true;
            }

            if (grenade.Blindness.y > 0f)
            {
                kind = GrenadeKind.Flash;
                return true;
            }

            if (grenade.EmitTime > 0f)
            {
                kind = GrenadeKind.Smoke;
                return true;
            }

            kind = GrenadeKind.Frag;
            return false;
        }

        private static bool ContainsReference(List<ThrowWeap> list, ThrowWeap item)
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

        private static void CollectGrenades(CompoundItem item, List<ThrowWeap> result)
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
                        if (gridItem is ThrowWeap grenade)
                        {
                            result.Add(grenade);
                        }
                        else if (gridItem is CompoundItem compoundItem)
                        {
                            CollectGrenades(compoundItem, result);
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
                if (slotItem is ThrowWeap grenade)
                {
                    result.Add(grenade);
                }
                else if (slotItem is CompoundItem compoundItem)
                {
                    CollectGrenades(compoundItem, result);
                }
            }
        }
    }
}
