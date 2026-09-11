using System;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // Geometry of the weapon panel's reserve row, shared by the magazine and
    // grenade views. Magazine (or loose-round) slots pack from the left edge,
    // grenade kinds pack from the right edge with frag outermost: nearly every
    // kit carries frags, so the common case keeps that icon in one place.
    // Hidden slots take no room; each icon says what it counts.
    //
    // When both groups do not fit side by side, the grenades wrap onto a
    // second row below and the panel grows by that row (WeaponInfoPanel
    // keeps that for the rest of the body's life). That is the only height
    // change, and it takes a crowded row: at the HUD font's 10 px digits,
    // every magazine kind plus frags and flashes still fits with a few
    // pixels to spare, while a third grenade kind, stored "+N" grenades or
    // multi-digit loose-round counts can tip it.
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
        // Far below the average glyph width of a count like "2 +1" in any real
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

        internal static bool GrenadesFitBeside(float magazineRight, float grenadeWidth)
            => grenadeWidth <= 0f || magazineRight <= 0f
                || magazineRight + GroupGap + grenadeWidth <= ContentWidth;

        // A wrap is kept once needed; only the caller's new body clears it.
        internal static bool GrenadesWrapped(bool wrappedBefore, float magazineRight, float grenadeWidth)
            => wrappedBefore || !GrenadesFitBeside(magazineRight, grenadeWidth);

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
