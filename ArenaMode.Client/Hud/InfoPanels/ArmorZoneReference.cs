namespace ArenaMode.Hud.InfoPanels
{
    /// <summary>
    /// Reference mapping used by <see cref="ArmorDurabilityCounter"/>.
    /// Soft-armor colliders come from item slot filters (e.g. Soft_armor_front/back).
    /// Plate colliders come from <see cref="EArmorPlateCollider"/>.
    /// </summary>
    internal static class ArmorZoneReference
    {
        /*
         * Fine paper-doll zones (FineArmorZone)
         * -------------------------------------
         * Front figure: Head, Throat, ThoraxFront, StomachFront, PelvisFront,
         *               LeftSide, RightSide, Left/RightUpperArm, Left/RightForearm,
         *               Left/RightThigh, Left/RightCalf
         * Back figure:  ThroatBack, ThoraxBack, StomachBack, PelvisBack
         *
         * A component's full durability is credited to EVERY zone it covers
         * (no division): each zone answers "how much protection is left here".
         *
         * EBodyPartColliderType → zone
         * ----------------------------
         * HeadCommon, ParietalHead, BackHead, Ears, Eyes, Jaw → Head
         * NeckFront → Throat            | NeckBack  → ThroatBack
         * RibcageUp → ThoraxFront       | SpineTop  → ThoraxBack
         * RibcageLow → StomachFront     | SpineDown → StomachBack
         * Pelvis → PelvisFront          | PelvisBack → PelvisBack
         * Left/RightSideChestUp/Down → Left/RightSide
         * Left/RightUpperArm → Left/RightUpperArm
         * Left/RightForearm → Left/RightForearm
         * Left/RightThigh → Left/RightThigh, Left/RightCalf → Left/RightCalf
         * (Verified complete against the 4.1 decompiled enum; legs exist there
         *  even though most armor doesn't cover them.)
         *
         * Head-slot components without mappable colliders fall back to Head
         * (entire item durability), matching the old per-slot behavior.
         *
         * EArmorPlateCollider → zone
         * --------------------------
         * Plate_Granit_SAPI_chest, Plate_Korund_chest → ThoraxFront
         * Plate_Granit_SAPI_back, Plate_6B13_back     → ThoraxBack + StomachBack
         * Plate_*_side_left_high/low                  → LeftSide
         * Plate_*_side_right_high/low                 → RightSide
         * (All 12 members of the 4.1 enum are covered.)
         */
    }
}
