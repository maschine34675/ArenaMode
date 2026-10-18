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

    // Counts the local body's hand grenades by kind: how many of each are
    // "usable" right now, i.e. could be bound to a number key and drawn
    // (Player.SetQuickSlotItem -> TryProceed, Player.cs:31417-31435,
    // 31560-31666) or drawn with a tap of G. Hand grenades are ThrowWeap
    // items; 40mm/30mm launcher rounds and rockets are Ammo/Rocket, so an
    // `is ThrowWeap` test already excludes them (EFT.InventoryLogic/ThrowWeap.cs:9,
    // Ammo.cs:10, Rocket.cs:7).
    internal static class GrenadeInventoryCounter
    {
        internal struct GrenadeSummary
        {
            public int UsableFrag, UsableFlash, UsableSmoke;
            public int OnGFrag, OnGFlash, OnGSmoke;

            public int Usable(GrenadeKind kind)
                => kind == GrenadeKind.Frag ? UsableFrag : kind == GrenadeKind.Flash ? UsableFlash : UsableSmoke;

            public int OnG(GrenadeKind kind)
                => kind == GrenadeKind.Frag ? OnGFrag : kind == GrenadeKind.Flash ? OnGFlash : OnGSmoke;

            public bool IsEmpty => UsableFrag + UsableFlash + UsableSmoke == 0;

            internal void AddUsable(GrenadeKind kind)
            {
                switch (kind)
                {
                    case GrenadeKind.Frag: UsableFrag++; break;
                    case GrenadeKind.Flash: UsableFlash++; break;
                    default: UsableSmoke++; break;
                }
            }

            internal void AddOnG(GrenadeKind kind)
            {
                switch (kind)
                {
                    case GrenadeKind.Frag: OnGFrag++; break;
                    case GrenadeKind.Flash: OnGFlash++; break;
                    default: OnGSmoke++; break;
                }
            }
        }

        // Reused across refreshes; only ever touched from the main thread.
        // Cleared after every read, including failed ones, like the magazine
        // counter, so no item reference outlives a refresh or a body.
        private static readonly List<ThrowWeap> _onGScratch = new List<ThrowWeap>();
        private static readonly List<ThrowWeap> _carriedScratch = new List<ThrowWeap>();

        public static bool TryGetSummary(Player player, out GrenadeSummary summary)
        {
            try { return CollectSummary(player, out summary); } finally { Reset(); }
        }

        internal static void Reset()
        {
            _onGScratch.Clear();
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

            // "On G" is the native tap-G candidate list, unchanged by Arena:
            // GetReachableItemsOfTypeNonAlloc walks Inventory.FastAccessSlots'
            // (Pockets, TacticalVest) DIRECT children only
            // (InventoryController.cs:882-907, Inventory.cs:193-197), which
            // Arena never rewrites — only the third-party UseItemsAnywhere mod
            // did that, and it is not installed any more. This drives the
            // dimming cue below and, since every grenade on it is also
            // reachable through the widened check below, folds straight into
            // "usable" without a second reachability test.
            _onGScratch.Clear();
            controller.GetReachableItemsOfTypeNonAlloc(_onGScratch, grenade => controller.Examined(grenade));

            // Carried = every grenade a player would still call "mine": rig,
            // pockets and backpack, including pouches nested inside any of
            // them. SecuredContainer and ArmorVest are not walked, matching
            // the magazine and loose-round counters; no stock secure
            // container accepts ThrowWeap (its grid ExcludedFilter lists the
            // ThrowWeap node 543be6564bdc2df4348b4568) and no stock armband
            // has a grid.
            _carriedScratch.Clear();
            CollectGrenades(equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem, _carriedScratch);
            CollectGrenades(equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem as CompoundItem, _carriedScratch);
            CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch);

            // Never compare a kind against default: GrenadeKind.Frag is 0, the
            // same trap as EBoundItem/ThrowWeapType (AGENTS.md). TryClassify's
            // bool is the only "found" signal.
            for (int i = 0; i < _onGScratch.Count; i++)
            {
                if (TryClassify(_onGScratch[i], out GrenadeKind kind))
                {
                    summary.AddOnG(kind);
                }
            }

            for (int i = 0; i < _carriedScratch.Count; i++)
            {
                ThrowWeap grenade = _carriedScratch[i];
                if (!TryClassify(grenade, out GrenadeKind kind))
                {
                    continue;
                }

                // Usable = on G already (cheapest check, and every on-G
                // grenade is also reachable below, so this only saves the
                // call) OR IsAtReachablePlace says so — the exact question
                // Player.TryProceed asks at a bound number key
                // (InventoryController.cs:448-464: native rig/pockets/weapon
                // slots, recursively, so nested pouches inside THOSE count
                // natively). Inside the arena with UseItemsAnywhere on,
                // ArenaReachPatches.ArenaReachableAnywherePatch turns this true for
                // ANY worn, examined, whole grenade — backpack top level or a
                // pouch nested anywhere, including inside the backpack
                // (ArenaMode.Client/Kits/ArenaReachPatches.cs). With it off,
                // only the native rig/pockets recursion applies and a
                // backpack grenade shows as not usable.
                //
                // IsAtReachablePlace is not allocation-free: natively it
                // builds a fresh array of the six BindAvailableSlotsExtended
                // items and chains several LINQ iterators over them before
                // testing Contains (Inventory.cs:174-182, 327-331;
                // ItemExtensions.cs:86-100, 163-172). Called once per carried,
                // not-yet-on-G grenade on every HUD refresh (a handful of
                // grenades, InfoPanelSettings.UpdateIntervalSeconds apart),
                // which is cheap next to a raid's other per-frame allocations
                // but is not free — GC-THRESHOLD-REVIEW.md is the place to
                // look if that ever needs to change.
                bool usable = ContainsReference(_onGScratch, grenade) || controller.IsAtReachablePlace(grenade);
                if (usable)
                {
                    summary.AddUsable(kind);
                }
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
