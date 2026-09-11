using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    internal enum FineArmorZone
    {
        Head = 0,
        Throat,
        ThoraxFront,
        StomachFront,
        PelvisFront,
        LeftSide,
        RightSide,
        LeftUpperArm,
        LeftForearm,
        RightUpperArm,
        RightForearm,
        LeftThigh,
        LeftCalf,
        RightThigh,
        RightCalf,
        ThroatBack,
        ThoraxBack,
        StomachBack,
        PelvisBack
    }

    internal static class ArmorDurabilityCounter
    {
        public const int ZoneCount = 19;

        internal struct ZoneSummary
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

        private const EArmorPlateCollider ChestPlateMask =
            EArmorPlateCollider.Plate_Granit_SAPI_chest | EArmorPlateCollider.Plate_Korund_chest;

        private const EArmorPlateCollider BackPlateMask =
            EArmorPlateCollider.Plate_Granit_SAPI_back | EArmorPlateCollider.Plate_6B13_back;

        private const EArmorPlateCollider LeftSidePlateMask =
            EArmorPlateCollider.Plate_Granit_SSAPI_side_left_high
            | EArmorPlateCollider.Plate_Granit_SSAPI_side_left_low
            | EArmorPlateCollider.Plate_Korund_side_left_high
            | EArmorPlateCollider.Plate_Korund_side_left_low;

        private const EArmorPlateCollider RightSidePlateMask =
            EArmorPlateCollider.Plate_Granit_SSAPI_side_right_high
            | EArmorPlateCollider.Plate_Granit_SSAPI_side_right_low
            | EArmorPlateCollider.Plate_Korund_side_right_high
            | EArmorPlateCollider.Plate_Korund_side_right_low;

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
        private static readonly bool[] _zoneFlags = new bool[ZoneCount];

        // Fills zones (length ZoneCount, indexed by FineArmorZone). A component's
        // full durability is credited to every zone it covers — a zone answers
        // "how much protection is left HERE", so overlapping coverage must not
        // be divided between zones. See ArmorZoneReference for the mapping.
        public static void FillSummary(Player player, ZoneSummary[] zones)
        {
            try { CollectSummary(player, zones); }
            finally { Reset(); }
        }

        internal static void Reset() => _componentScratch.Clear();

        private static void CollectSummary(Player player, ZoneSummary[] zones)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                zones[i] = default;
            }

            InventoryEquipment equipment = player?.Inventory?.Equipment;
            if (equipment == null)
            {
                return;
            }

            CollectFromSlots(equipment, HeadSlots, true, zones);
            CollectFromSlots(equipment, BodySlots, false, zones);
        }

        private static void CollectFromSlots(
            InventoryEquipment equipment,
            EquipmentSlot[] slots,
            bool headGroup,
            ZoneSummary[] zones)
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
                AddComponent(_componentScratch[i], headGroup, zones);
            }
        }

        private static void AddComponent(ArmorComponent armor, bool headGroup, ZoneSummary[] zones)
        {
            if (armor.Repairable == null)
            {
                return;
            }

            System.Array.Clear(_zoneFlags, 0, ZoneCount);
            bool any = false;

            EArmorPlateCollider plateMask = armor.ArmorPlateCollidersMask;
            if (plateMask != 0)
            {
                if ((plateMask & ChestPlateMask) != 0)
                {
                    any = _zoneFlags[(int)FineArmorZone.ThoraxFront] = true;
                }

                if ((plateMask & BackPlateMask) != 0)
                {
                    // A back plate physically shields the whole back, i.e. both bands.
                    _zoneFlags[(int)FineArmorZone.ThoraxBack] = true;
                    any = _zoneFlags[(int)FineArmorZone.StomachBack] = true;
                }

                if ((plateMask & LeftSidePlateMask) != 0)
                {
                    any = _zoneFlags[(int)FineArmorZone.LeftSide] = true;
                }

                if ((plateMask & RightSidePlateMask) != 0)
                {
                    any = _zoneFlags[(int)FineArmorZone.RightSide] = true;
                }
            }
            else
            {
                foreach (EBodyPartColliderType collider in armor.ArmorColliders)
                {
                    FineArmorZone? zone = MapCollider(collider);
                    if (zone.HasValue)
                    {
                        any = _zoneFlags[(int)zone.Value] = true;
                    }
                }
            }

            // Head gear without mappable colliders still protects the head
            // (entire item durability), matching the old per-slot behavior.
            if (!any && headGroup)
            {
                any = _zoneFlags[(int)FineArmorZone.Head] = true;
            }

            if (!any)
            {
                return;
            }

            float current = armor.Repairable.Durability;
            float maximum = armor.Repairable.MaxDurability;
            for (int i = 0; i < ZoneCount; i++)
            {
                if (_zoneFlags[i])
                {
                    zones[i].Add(current, maximum);
                }
            }
        }

        private static FineArmorZone? MapCollider(EBodyPartColliderType collider)
        {
            switch (collider)
            {
                case EBodyPartColliderType.HeadCommon:
                case EBodyPartColliderType.ParietalHead:
                case EBodyPartColliderType.BackHead:
                case EBodyPartColliderType.Ears:
                case EBodyPartColliderType.Eyes:
                case EBodyPartColliderType.Jaw:
                    return FineArmorZone.Head;
                case EBodyPartColliderType.NeckFront:
                    return FineArmorZone.Throat;
                case EBodyPartColliderType.NeckBack:
                    return FineArmorZone.ThroatBack;
                case EBodyPartColliderType.RibcageUp:
                    return FineArmorZone.ThoraxFront;
                case EBodyPartColliderType.RibcageLow:
                    return FineArmorZone.StomachFront;
                case EBodyPartColliderType.Pelvis:
                    return FineArmorZone.PelvisFront;
                case EBodyPartColliderType.SpineTop:
                    return FineArmorZone.ThoraxBack;
                case EBodyPartColliderType.SpineDown:
                    return FineArmorZone.StomachBack;
                case EBodyPartColliderType.PelvisBack:
                    return FineArmorZone.PelvisBack;
                case EBodyPartColliderType.LeftSideChestUp:
                case EBodyPartColliderType.LeftSideChestDown:
                    return FineArmorZone.LeftSide;
                case EBodyPartColliderType.RightSideChestUp:
                case EBodyPartColliderType.RightSideChestDown:
                    return FineArmorZone.RightSide;
                case EBodyPartColliderType.LeftUpperArm:
                    return FineArmorZone.LeftUpperArm;
                case EBodyPartColliderType.LeftForearm:
                    return FineArmorZone.LeftForearm;
                case EBodyPartColliderType.RightUpperArm:
                    return FineArmorZone.RightUpperArm;
                case EBodyPartColliderType.RightForearm:
                    return FineArmorZone.RightForearm;
                case EBodyPartColliderType.LeftThigh:
                    return FineArmorZone.LeftThigh;
                case EBodyPartColliderType.LeftCalf:
                    return FineArmorZone.LeftCalf;
                case EBodyPartColliderType.RightThigh:
                    return FineArmorZone.RightThigh;
                case EBodyPartColliderType.RightCalf:
                    return FineArmorZone.RightCalf;
                default:
                    return null;
            }
        }
    }
}
