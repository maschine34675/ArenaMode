using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    internal enum ArmorGroup
    {
        Head = 0,
        Body = 1
    }

    // Minimal armor model (2026-09-25, "HUD minimal reserve"): two aggregates
    // instead of the old 19-zone paper doll. See PlayerStatusPanel for the display.
    internal static class ArmorDurabilityCounter
    {
        public const int GroupCount = 2;

        internal struct GroupSummary
        {
            public float CurrentDurability;
            public float MaxDurability;

            public bool HasArmor => MaxDurability > 0.01f;

            public float Normalized => MaxDurability > 0.01f ? CurrentDurability / MaxDurability : 0f;

            public void Add(float current, float maximum)
            {
                CurrentDurability += current;
                MaxDurability += maximum;
            }
        }

        private static readonly EquipmentSlot[] HeadSlots =
        {
            EquipmentSlot.Headwear,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Eyewear
        };

        private static readonly EquipmentSlot[] BodySlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.ArmorVest
        };

        private static readonly List<ArmorComponent> _componentScratch = new List<ArmorComponent>();

        // Fills groups (length GroupCount, indexed by ArmorGroup) with the sum
        // of every DISTINCT armor component's durability found under that
        // group's slots (GetItemComponentsInChildrenNonAlloc walks plates and
        // helmet mods too, e.g. a visor — ItemExtensions.cs:366-392: the item's
        // own component first, then every merged child's, recursively). A
        // component is added ONCE here even when it physically covers several
        // body regions — a plate carrier's back plate shields both the upper
        // and lower back (ArmorComponent.cs:217-233's GetBothColliders can
        // yield more than one EBodyPartColliderType for one component), but it
        // is still one piece of armor with one durability pool
        // (ArmorComponent.cs:130, RepairableComponent.cs:45-48). The old
        // 19-zone model credited a component's full durability to EVERY zone
        // it covered, so any aggregate built by summing those zones (as this
        // replaces) would have counted such a component once per region it
        // covered instead of once; collecting components into one list per
        // group and summing the list, rather than summing per-zone buckets, is
        // what avoids that.
        public static void FillSummary(Player player, GroupSummary[] groups)
        {
            try { CollectSummary(player, groups); }
            finally { Reset(); }
        }

        internal static void Reset() => _componentScratch.Clear();

        private static void CollectSummary(Player player, GroupSummary[] groups)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = default;
            }

            InventoryEquipment equipment = player?.Inventory?.Equipment;
            if (equipment == null)
            {
                return;
            }

            CollectFromSlots(equipment, HeadSlots, ArmorGroup.Head, groups);
            CollectFromSlots(equipment, BodySlots, ArmorGroup.Body, groups);
        }

        private static void CollectFromSlots(
            InventoryEquipment equipment,
            EquipmentSlot[] slots,
            ArmorGroup group,
            GroupSummary[] groups)
        {
            _componentScratch.Clear();
            for (int i = 0; i < slots.Length; i++)
            {
                Item containedItem = equipment.GetSlot(slots[i]).ContainedItem;
                if (containedItem == null)
                {
                    continue;
                }

                containedItem.GetItemComponentsInChildrenNonAlloc(_componentScratch, true);
            }

            for (int i = 0; i < _componentScratch.Count; i++)
            {
                ArmorComponent armor = _componentScratch[i];
                // Repairable is a readonly RepairableComponent field
                // (ArmorComponent.cs:130); Durability and MaxDurability are its
                // own plain floats (RepairableComponent.cs:45-48). The null
                // check is defensive, kept from the old model.
                if (armor.Repairable == null || armor.Repairable.MaxDurability <= 0f)
                {
                    continue;
                }

                groups[(int)group].Add(armor.Repairable.Durability, armor.Repairable.MaxDurability);
            }
        }
    }
}
