using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>One right-column geometry for map details, area preview and mutators.</summary>
internal static class ArenaMapPanelLayout
{
    internal const float InfoTop = 168f, InfoHeight = 320f, PreviewHeight = InfoHeight;
    internal const float MutatorsHeight = 496f;

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
