using System;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // Geometry of the weapon panel's reserve row, shared by the magazine and
    // grenade views. Magazine (or loose-round) slots pack from the left edge,
    // grenade kinds pack from the right edge with frag outermost: nearly every
    // kit carries frags, so the common case keeps that icon in one place.
    // Hidden slots take no room; each icon says what it counts.
    //
    // The row never wraps: there is exactly one reserve row, always at the
    // same height (WeaponInfoPanel). When both groups do not fit beside each
    // other at their natural size, FitScale below shrinks both uniformly
    // instead — a crowded row gets smaller icons and text rather than a
    // taller panel.
    //
    // Plain arithmetic without Unity types, so the offline panel checks link
    // and exercise this exact file.
    internal static class ReserveRowLayout
    {
        // Between the 16 px insets of the 280 px panel: the row root keeps
        // 8 px from each panel edge and its slots another 8 px inside it.
        internal const float ContentWidth = 248f;
        internal const float MagazineSpacing = 12f;
        internal const float GrenadeSpacing = 10f;
        internal const float GroupGap = 14f;
        // Below this, further shrinking stops being worth it — an icon at
        // 60% of its drawn size stops reading as what it is. See FitScale.
        internal const float MinFitScale = 0.6f;
        // Far below the average glyph width of a count like "12" in any real
        // font, far above the tenth that TMP's unscaled path would report.
        private const float MinGlyphEm = 0.25f;

        // Writes each visible slot's left edge, measured from the content's
        // left edge, and returns the right edge of the last visible slot, or
        // 0 when none is visible. Hidden slots keep a left edge of 0.
        internal static float PackFromLeft(float[] widths, bool[] visible, float[] lefts)
        {
            float cursor = 0f;
            float right = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                lefts[i] = 0f;
                if (!visible[i]) continue;
                lefts[i] = cursor;
                right = cursor + widths[i];
                cursor = right + MagazineSpacing;
            }

            return right;
        }

        // Writes each visible slot's right edge as its distance from the
        // content's right edge (index 0 outermost) and returns the group's
        // total width, or 0 when no slot is visible.
        internal static float PackFromRight(float[] widths, bool[] visible, float[] rightInsets)
        {
            float cursor = 0f;
            float width = 0f;
            for (int i = 0; i < visible.Length; i++)
            {
                rightInsets[i] = 0f;
                if (!visible[i]) continue;
                rightInsets[i] = cursor;
                width = cursor + widths[i];
                cursor = width + GrenadeSpacing;
            }

            return width;
        }

        // Single scale factor for both reserve groups, applied so magazines
        // stay pinned to the row's left content edge and grenades to its
        // right content edge (SpareMagazineBarView.ApplyScale,
        // GrenadeBarView.ApplyScale) — never centered, so shrinking a group
        // pulls it away from the OTHER group, not toward it.
        //
        // At scale 1 the two groups plus the gap between them must fit
        // ContentWidth: magazineRight + GroupGap + grenadeWidth <= ContentWidth.
        // When they do not, scaling every distance in that sum by the same
        // factor scale = ContentWidth / needed makes the sum equal
        // ContentWidth exactly:
        //   scale * magazineRight + scale * GroupGap + scale * grenadeWidth
        //     == scale * needed == ContentWidth
        // Pinning each group to its own edge rather than recentering only
        // ever widens the real gap beyond what that sum implies (the fixed
        // 8 px inset ahead of each group, see the field comment above, is
        // never part of `needed` and shrinks along with its group, adding
        // slack this derivation does not even count): with
        // gap = ContentWidth - scale * (magazineRight + grenadeWidth), and
        // needed = magazineRight + GroupGap + grenadeWidth,
        //   gap = ContentWidth * GroupGap / needed
        // which is strictly positive for any GroupGap > 0 and any needed > 0
        // — the two groups can never touch at the unclamped scale, however
        // wide their combined content. Below MinFitScale the row stops
        // shrinking further and this guarantee no longer holds; reaching
        // that floor needs several hundred pixels of combined content in a
        // 248 px budget, which no real magazine/grenade count produces.
        internal static float FitScale(float magazineRight, float grenadeWidth)
        {
            // An empty group has nothing to gap against — no shrink needed
            // just because the other group alone is wide.
            if (magazineRight <= 0f || grenadeWidth <= 0f)
            {
                return 1f;
            }

            float needed = magazineRight + GroupGap + grenadeWidth;
            if (needed <= ContentWidth)
            {
                return 1f;
            }

            return Math.Max(MinFitScale, ContentWidth / needed);
        }

        // A slot's footprint: its icon column plus the measured count. A
        // missing, non-finite or implausibly small measurement (TMP scales by
        // 0.1 if its Awake never ran) falls back to a generous per-glyph
        // estimate: packed slots can end up too far apart but never overlap.
        // The HUD font's digits measure 10 px at 18 pt, about 0.56 em.
        internal static float SlotWidth(float countLeft, float measuredText, int glyphs, float fontSize)
        {
            bool usable = !float.IsInfinity(measuredText) && measuredText >= glyphs * fontSize * MinGlyphEm;
            float text = usable ? measuredText : glyphs * fontSize * 0.65f;
            return countLeft + Math.Min(text, ContentWidth);
        }
    }
}
