using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>One right-column geometry for map details, area preview and mutators.</summary>
internal static class ArenaMapPanelLayout
{
    // Category headings borrow 28px from the preview, retaining readable
    // 22px controls + 18px descriptions and the same bottom edge above READY.
    internal const float InfoTop = 168f, InfoHeight = 224f, PreviewHeight = InfoHeight;
    internal const float MutatorsHeight = 568f;
    internal const float MutatorGroupsTop = 44f, MutatorHeadingHeight = 18f;
    internal const float MutatorRowPitch = 40f, MutatorButtonHeight = 22f;

    // Four groups: movement/equipment in the left column, combat/feedback in
    // the right. The second group's position follows the visible first group,
    // so Fast Bots and Zombie Crowd can share one slot without layout drift.
    internal static float MutatorCellWidth(float width) => (width - 60f) / 2f;
    internal static float MutatorGroupLeft(int group, float width)
        => group < 2 ? 20f : 40f + MutatorCellWidth(width);
    internal static float MutatorGroupTop(int group, int firstGroupCount)
        => MutatorGroupsTop + (group % 2 == 0 ? 0f : MutatorHeadingHeight + firstGroupCount * MutatorRowPitch);
    internal static float MutatorRowTop(int group, int firstGroupCount, int index)
        => MutatorGroupTop(group, firstGroupCount) + MutatorHeadingHeight + index * MutatorRowPitch;

    internal static float Scale(Transform screen)
    {
        float height = (screen as RectTransform)?.rect.height ?? 1080f;
        return height > 0f ? Math.Min(1f, height / 1080f) : 1f;
    }

    internal static float Width(Transform screen)
    {
        float width = (screen as RectTransform)?.rect.width ?? 1920f;
        if (width <= 0f) width = 1920f;
        // 600 also leaves a gap beside vanilla's central weather/time strip.
        return Math.Min(600f, Math.Max(480f, width / Scale(screen) - 1240f));
    }

    internal static void Place(RectTransform root, Transform screen, float top, float height)
    {
        float scale = Scale(screen);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-56f * scale, -top * scale);
        root.sizeDelta = new Vector2(Width(screen), height);
        root.localScale = new Vector3(scale, scale, 1f);
    }

    internal const float MutatorsTop = InfoTop + InfoHeight + 20f;
}
